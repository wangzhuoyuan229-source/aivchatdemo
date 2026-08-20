using ChatApp.Core.Models;
using ChatApp.UI.Services;

namespace ChatApp.Tests;

public class ChatExportServiceTests
{
    private static List<Message> SampleMessages() => new()
    {
        new Message
        {
            Id = 1,
            ConversationId = 7,
            RoleId = 3,
            Author = MessageAuthor.User,
            Content = "你好",
            CreatedAt = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc),
            Attachments = new List<MessageAttachment>()
        },
        new Message
        {
            Id = 2,
            ConversationId = 7,
            RoleId = 3,
            Author = MessageAuthor.Assistant,
            Content = "很高兴见到你。",
            CitedDocumentIds = "5,9,5",
            CreatedAt = new DateTime(2026, 8, 20, 10, 0, 5, DateTimeKind.Utc),
            Attachments = new List<MessageAttachment>
            {
                new()
                {
                    Kind = MessageAttachmentKind.Image,
                    FileName = "pic.png",
                    Title = "立绘",
                    Caption = "港口夜景"
                }
            }
        }
    };

    [Fact]
    public void MarkdownIncludesNamesTimesAndAttachmentSnapshots()
    {
        var md = ChatExportService.ToMarkdown("与林溪的对话", SampleMessages(), roleId => "林溪");

        Assert.Contains("# 与林溪的对话", md);
        Assert.Contains("**我**", md);
        Assert.Contains("**林溪**", md);
        Assert.Contains("很高兴见到你。", md);
        Assert.Contains("🖼️ 附件快照：立绘 — 港口夜景", md);
        Assert.DoesNotContain("未知角色", md);
    }

    [Fact]
    public void JsonIsStructuredAndCarriesCitationsAndAttachments()
    {
        var json = ChatExportService.ToJson("会话", SampleMessages(), roleId => "林溪");

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        var assistant = messages[1];
        Assert.Equal("Assistant", assistant.GetProperty("author").GetString());
        Assert.Equal("林溪", assistant.GetProperty("role").GetString());
        Assert.Equal(new[] { 5, 9 },
            assistant.GetProperty("citedDocumentIds").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal("立绘", assistant.GetProperty("attachments")[0].GetProperty("title").GetString());
        Assert.Equal("港口夜景", assistant.GetProperty("attachments")[0].GetProperty("caption").GetString());
    }

    [Fact]
    public void CitedDocumentIdListDeduplicatesAndIgnoresInvalidParts()
    {
        var message = new Message { CitedDocumentIds = " 5, 9 ,5,x,0,-1,11 " };

        var ids = message.GetCitedDocumentIdList();

        Assert.Equal(new[] { 5, 9, 11 }, ids);
    }

    [Fact]
    public void EmptyCitationsYieldEmptyList()
    {
        Assert.Empty(new Message().GetCitedDocumentIdList());
        Assert.Empty(new Message { CitedDocumentIds = " , ," }.GetCitedDocumentIdList());
    }

    [Theory]
    [InlineData("与 林溪/的:对话*", "与 林溪的对话")]
    [InlineData("   ", "conversation")]
    public void FileNameIsSanitized(string input, string expected)
    {
        Assert.Equal(expected, ChatExportService.SanitizeFileName(input));
    }
}
