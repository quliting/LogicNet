using Aspose.Words.Replacing;

namespace LogicNet.Application.Test.Service;

[AllowAnonymous]
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