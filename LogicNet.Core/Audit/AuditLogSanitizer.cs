using System;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LogicNet.Core.Audit;

/// <summary>
///     审计日志脱敏与截断：请求体、查询串、异常消息在落库前统一处理。
///     目标是不让 password / accessToken / refreshToken / authorization / code（验证码）等
///     敏感键值以明文进入数据库或日志。
/// </summary>
public static class AuditLogSanitizer
{
    /// <summary>
    ///     掩码占位符
    /// </summary>
    public const string Mask = "***";

    private const string TruncatedSuffix = "...(truncated)";

    // 完整键名（忽略大小写）直接命中：常见凭据键 + 验证码语义键（code/verifyCode/captchaCode 等）。
    // 刻意不把 "code" 做成片段匹配，避免 permissionCode/roleCode/orderCode 等业务编码被误伤
    private static readonly string[] SensitiveKeys =
    {
        "password", "passwd", "pwd", "accesstoken", "refreshtoken", "authorization", "secret", "token",
        "verificationcode", "captchacode", "verifycode", "mobilecode", "emailcode", "smscode", "otpcode",
        "code"
    };

    // 键名/参数名包含这些片段即视为敏感，覆盖 newPassword、userToken 等变体；
    // 同样不包含 "code"，保证 permissionCode、roleCode 等业务编码保留原值
    private static readonly string[] SensitiveNameFragments =
    {
        "password", "passwd", "pwd", "accesstoken", "refreshtoken", "authorization", "secret", "token",
        "verificationcode", "captchacode", "verifycode", "mobilecode", "emailcode", "smscode", "otpcode"
    };

    // 正则按长度倒序排列，确保 verificationcode 优先于 code 命中
    private static readonly string KeyPattern = string.Join("|",
        SensitiveKeys.OrderByDescending(key => key.Length).Select(Regex.Escape));

    // JSON 形式的 "key": "value" 或 "key": 123
    private static readonly Regex JsonSecretRegex = new(
        "(\"(?:" + KeyPattern + ")\"\\s*:\\s*)(\"(?:[^\"\\\\]|\\\\.)*\"|[^,}\\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 表单/查询串/类 JSON 无引号形式 key=value 或 key: value（键前不能紧跟字母数字，避免误伤 PermissionCode）
    private static readonly Regex KeyValueSecretRegex = new(
        "(?<key>(?<![A-Za-z0-9_])(?:" + KeyPattern + ")\\s*[=:]\\s*)(?<value>[^&;,}\\s]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // 中文按原样输出，便于人工排查
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    ///     请求体脱敏：合法 JSON 按字段递归掩码，非法 JSON 退回正则掩码，最后按 maxLength 截断
    /// </summary>
    public static string SanitizeBody(string body, int maxLength)
    {
        if (string.IsNullOrEmpty(body)) return body;

        var masked = MaskJson(body) ?? MaskByRegex(body);
        return Truncate(masked, maxLength);
    }

    /// <summary>
    ///     查询串/异常消息等文本脱敏（不要求 JSON 结构），并截断
    /// </summary>
    public static string SanitizeText(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var masked = KeyValueSecretRegex.Replace(JsonSecretRegex.Replace(value, "$1\"" + Mask + "\""),
            "${key}" + Mask);
        return Truncate(masked, maxLength);
    }

    /// <summary>
    ///     按字符数截断，超出部分以固定后缀标记，保证结果长度不超过 maxLength
    /// </summary>
    public static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || maxLength <= 0 || value.Length <= maxLength) return value;
        if (maxLength <= TruncatedSuffix.Length) return value.Substring(0, maxLength);
        return value.Substring(0, maxLength - TruncatedSuffix.Length) + TruncatedSuffix;
    }

    private static string MaskJson(string body)
    {
        var trimmed = body.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return null;

        try
        {
            var node = JsonNode.Parse(body);
            if (node is null) return null;
            MaskNode(node);
            return node.ToJsonString(JsonOptions);
        }
        catch (JsonException)
        {
            // 截断或非严格 JSON：退回到正则掩码，确保敏感值仍被打码
            return null;
        }
    }

    private static void MaskNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(item => item.Key).ToArray())
                {
                    if (IsSensitiveKey(key))
                        obj[key] = Mask;
                    else
                        MaskNode(obj[key]);
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++) MaskNode(array[i]);
                break;
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        return IsSensitiveToken(key);
    }

    /// <summary>
    ///     判断 SQL 参数名是否敏感：去掉 @/: /? 前缀后按完整名或敏感片段匹配。
    ///     code/verifyCode/captchaCode 等验证码键会被掩码，permissionCode/roleCode/orderCode
    ///     等业务编码保留原值。用于在拼接参数列表前直接掩码参数值（如 @newPassword），比事后正则匹配更可靠。
    /// </summary>
    public static bool IsSensitiveName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        return IsSensitiveToken(name.Trim().TrimStart('@', ':', '?'));
    }

    private static bool IsSensitiveToken(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        // 忽略下划线等分隔符，兼容 verify_code / permission_code 之类的命名风格
        var normalized = name.Trim().Replace("_", string.Empty);
        return SensitiveKeys.Contains(normalized, StringComparer.OrdinalIgnoreCase)
               || SensitiveNameFragments.Any(fragment =>
                   normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string MaskByRegex(string value)
    {
        var masked = JsonSecretRegex.Replace(value, "$1\"" + Mask + "\"");
        return KeyValueSecretRegex.Replace(masked, "${key}" + Mask);
    }
}
