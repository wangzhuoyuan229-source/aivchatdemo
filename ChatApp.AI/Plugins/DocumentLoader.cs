using UglyToad.PdfPig;

namespace ChatApp.AI.Plugins;

/// <summary>Loads text from .txt/.md/.pdf files (F5).</summary>
public static class DocumentLoader
{
    public static async Task<string> LoadAsync(string filePath, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "txt" or "md" or "markdown" => await File.ReadAllTextAsync(filePath, ct),
            "pdf" => LoadPdf(filePath),
            _ => throw new NotSupportedException($"不支持的文件类型：.{ext}（仅支持 txt/md/pdf）")
        };
    }

    private static string LoadPdf(string filePath)
    {
        var sb = new System.Text.StringBuilder();
        using var doc = PdfDocument.Open(filePath);
        foreach (var page in doc.GetPages())
        {
            sb.AppendLine(page.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string DetectTitle(string fileName)
    {
        return Path.GetFileNameWithoutExtension(fileName);
    }
}
