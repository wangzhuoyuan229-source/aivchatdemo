using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Models;

namespace ChatApp.Tests;

public class KnowledgeImageSelectionTests
{
    private static readonly KnowledgeImageHit[] Candidates =
    [
        new() { DocumentId = 10, Title = "A" },
        new() { DocumentId = 20, Title = "B" },
        new() { DocumentId = 30, Title = "C" },
        new() { DocumentId = 40, Title = "D" }
    ];

    [Fact]
    public void SelectionRejectsUnknownDeduplicatesAndLimitsToThree()
    {
        var result = KnowledgeImageSelection.Parse(
            "回答[[knowledge-image:10]][[knowledge-image:999]][[knowledge-image:10]]" +
            "[[knowledge-image:20]][[knowledge-image:30]][[knowledge-image:40]]",
            Candidates);

        Assert.Equal("回答", result.Text);
        Assert.Equal(new[] { 10, 20, 30 }, result.DocumentIds);
    }

    [Fact]
    public void StreamFilterHidesMarkerSplitAcrossChunks()
    {
        var filter = new KnowledgeImageSelection.StreamFilter();
        var visible = filter.Push("这是回") +
                      filter.Push("复[[know") +
                      filter.Push("ledge-image:10]]尾声") +
                      filter.Complete();

        Assert.Equal("这是回复尾声", visible);
        Assert.DoesNotContain("knowledge-image", visible);
    }

    [Fact]
    public void PromptProvidesMetadataButNotOriginalImageBytes()
    {
        var prompt = ChatOrchestrator.BuildSystemPrompt(
            new Role { Name = "角色" },
            Array.Empty<VectorSearchHit>(),
            new KnowledgeRetrievalResult { Status = KnowledgeRetrievalStatus.Found, ImageHits = Candidates },
            Candidates);

        Assert.Contains("图片ID=10", prompt);
        Assert.Contains("0—3", prompt);
        Assert.Contains("[[knowledge-image:图片ID]]", prompt);
    }
}
