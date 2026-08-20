using System.Text;
using System.Text.Json;
using ChatApp.Core.Models;

namespace ChatApp.UI.Services;

/// <summary>Pure conversation exporters (Markdown / JSON). No IO so it stays testable.</summary>
public static class ChatExportService
{
    public static string ToMarkdown(
        string title,
        IReadOnlyList<Message> messages,
        Func<int, string> roleNameResolver)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(string.IsNullOrWhiteSpace(title) ? "会话导出" : title);
        sb.Append("导出时间：").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine();
        foreach (var m in messages)
        {
            var who = m.Author == MessageAuthor.User
                ? "我"
                : roleNameResolver(m.RoleId);
            sb.Append("**").Append(who).Append("** · ")
              .AppendLine(m.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine(m.Content);
            foreach (var a in m.Attachments.Where(a => a.Kind == MessageAttachmentKind.Image))
            {
                sb.Append("> 🖼️ 附件快照：")
                  .Append(string.IsNullOrWhiteSpace(a.Title) ? a.FileName : a.Title);
                if (!string.IsNullOrWhiteSpace(a.Caption)) sb.Append(" — ").Append(a.Caption);
                sb.AppendLine();
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string ToJson(
        string title,
        IReadOnlyList<Message> messages,
        Func<int, string> roleNameResolver)
    {
        var payload = new
        {
            title,
            exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            messages = messages.Select(m => new
            {
                id = m.Id,
                author = m.Author.ToString(),
                role = m.Author == MessageAuthor.User ? "我" : roleNameResolver(m.RoleId),
                content = m.Content,
                createdAt = m.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                citedDocumentIds = m.GetCitedDocumentIdList(),
                attachments = m.Attachments
                    .Where(a => a.Kind == MessageAttachmentKind.Image)
                    .Select(a => new
                    {
                        title = string.IsNullOrWhiteSpace(a.Title) ? a.FileName : a.Title,
                        caption = a.Caption
                    })
                    .ToList()
            }).ToList()
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars()
        .Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })
        .Where(c => c > 31)
        .Distinct()
        .ToArray();

    public static string SanitizeFileName(string title)
    {
        var cleaned = new string(title.Where(c => !InvalidFileNameChars.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "conversation" : cleaned;
    }
}
