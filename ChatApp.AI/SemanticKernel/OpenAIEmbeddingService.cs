using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;

namespace ChatApp.AI.SemanticKernel;

/// <summary>Wraps the OpenAI-compatible embedding service exposed via Semantic Kernel.</summary>
public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly IConfigurationService _config;
    private readonly ILogger<OpenAIEmbeddingService> _logger;

    public OpenAIEmbeddingService(IConfigurationService config, ILogger<OpenAIEmbeddingService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var settings = await _config.LoadAsync(ct);
        EnsureEmbeddings(settings);
        var kernel = KernelFactory.Build(settings);
        var svc = kernel.GetRequiredService<ITextEmbeddingGenerationService>();
        var results = await svc.GenerateEmbeddingsAsync(new List<string> { text }, kernel, ct);
        return results[0].ToArray();
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return Array.Empty<float[]>();
        var settings = await _config.LoadAsync(ct);
        EnsureEmbeddings(settings);
        var kernel = KernelFactory.Build(settings);
        var svc = kernel.GetRequiredService<ITextEmbeddingGenerationService>();
        var results = await svc.GenerateEmbeddingsAsync(texts.ToList(), kernel, ct);
        return results.Select(r => r.ToArray()).ToList();
    }

    private static void EnsureEmbeddings(AiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.EmbeddingModel))
            throw new InvalidOperationException("Embedding 模型未配置，长期记忆与知识库功能需要它。");
    }
}
