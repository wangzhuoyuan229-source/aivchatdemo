using System.Text.Json;

namespace ChatApp.Tests;

public class EvalDatasetTests
{
    [Fact]
    public async Task GroundingDatasetContainsFiftyUniqueCasesAcrossRequiredCategories()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Evals", "grounding-cases.json");
        await using var stream = File.OpenRead(path);
        var cases = await JsonSerializer.DeserializeAsync<List<EvalCase>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(cases);
        Assert.Equal(50, cases.Count);
        Assert.Equal(50, cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(cases, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.User));
            Assert.False(string.IsNullOrWhiteSpace(c.ExpectedBehavior));
        });

        var categories = cases.Select(c => c.Category).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(categories, new HashSet<string>(StringComparer.Ordinal)
        {
            "known_fact", "paraphrase", "follow_up", "unknown_fact",
            "prompt_injection", "casual_chat", "cross_role", "conflict"
        });
    }

    private sealed class EvalCase
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string ExpectedBehavior { get; set; } = string.Empty;
    }
}
