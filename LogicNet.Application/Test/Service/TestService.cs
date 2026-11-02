using Aspose.Words.Replacing;

namespace LogicNet.Application.Test.Service;

// 移除匿名开放：该服务操作服务器文件（Aspose 读写 Word），全局授权要求登录后方可调用
public class TestService : LogicBase
{
    public void ChangeWordContentAsync()
    {
        var filePath = Path.Combine(App.WebHostEnvironment.WebRootPath, "Temp", "text.docx");
        Aspose.Words.Document doc = new Aspose.Words.Document(filePath);
        // 2. 简单文本替换：把 "旧文本" 替换为 "新文本"
        FindReplaceOptions options = new FindReplaceOptions();
        doc.Range.Replace("{Name}", "屈立亭", options);

        // 3. 保存结果
        doc.Save(Path.Combine(App.WebHostEnvironment.WebRootPath, "Temp", "textEnd.docx"));
    }
}