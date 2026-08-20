using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using System.Net;

namespace ChatApp.AI.SemanticKernel;

/// <summary>Wraps the OpenAI-compatible embedding service exposed via Semantic Kernel.</summary>
public class OpenAIEmbeddingService : IEmbeddingService
{
    // Alibaba text-embedding-v4 accepts at most 10 inputs per request. Keeping
    // this conservative limit also works with the other OpenAI-compatible presets.
    internal const int MaxInputsPerRequest = 10;
    private const int MaxConcurrentRequests = 4;
    private readonly IConfigurationService _config;
    private readonly ILogger<OpenAIEmbeddingService> _logger;
    private readonly SemaphoreSlim _requestGate = new(MaxConcurrentRequests, MaxConcurrentRequests);
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private string _runtimeSignature = string.Empty;
    private Kernel? _cachedKernel;
    private ITextEmbeddingGenerationService? _cachedService;

    public OpenAIEmbeddingService(IConfigurationService config, ILogger<OpenAIEmbeddingService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var results = await EmbedBatchAsync(new[] { text }, ct);
        return results[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return Array.Empty<float[]>();
        var settings = await _config.LoadAsync(ct);
        EnsureEmbeddings(settings);
        var (kernel, svc) = await GetRuntimeAsync(settings, ct);
        var output = new float[texts.Count][];
        var batches = CreateBatches(texts, MaxInputsPerRequest);

        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentRequests, CancellationToken = ct },
            async (batch, token) =>
            {
                var results = await GenerateWithRetryAsync(svc, kernel, batch.Texts, token);
                if (results.Count != batch.Texts.Count)
                    throw new InvalidDataException(
                        $"Embedding 服务返回了 {results.Count} 个向量，但请求包含 {batch.Texts.Count} 段文本。");
                for (var index = 0; index < results.Count; index++)
                    output[batch.Offset + index] = results[index].ToArray();
            });

        return output;
    }

    private async Task<(Kernel Kernel, ITextEmbeddingGenerationService Service)> GetRuntimeAsync(
        AiSettings settings,
        CancellationToken ct)
    {
        var signature = string.Join('\n',
            settings.ResolveEmbeddingApiBaseUrl(),
            settings.ResolveEmbeddingApiKey(),
            settings.EmbeddingModel);
        await _runtimeGate.WaitAsync(ct);
        try
        {
            if (_cachedKernel is null || _cachedService is null ||
                !string.Equals(_runtimeSignature, signature, StringComparison.Ordinal))
            {
                _cachedKernel = KernelFactory.Build(settings);
                _cachedService = _cachedKernel.GetRequiredService<ITextEmbeddingGenerationService>();
                _runtimeSignature = signature;
            }
            return (_cachedKernel!, _cachedService!);
        }
        finally
        {
            _runtimeGate.Release();
        }
    }

    internal static IReadOnlyList<(int Offset, IReadOnlyList<string> Texts)> CreateBatches(
        IReadOnlyList<string> texts,
        int batchSize)
    {
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var batches = new List<(int Offset, IReadOnlyList<string> Texts)>();
        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var count = Math.Min(batchSize, texts.Count - offset);
            batches.Add((offset, texts.Skip(offset).Take(count).ToArray()));
        }
        return batches;
    }

    private async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateWithRetryAsync(
        ITextEmbeddingGenerationService service,
        Kernel kernel,
        IReadOnlyList<string> texts,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            TimeSpan? retryDelay = null;
            await _requestGate.WaitAsync(ct);
            try
            {
                var results = await service.GenerateEmbeddingsAsync(texts.ToList(), kernel, ct);
                return results.ToList();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < 3 && IsRetryable(ex))
            {
                retryDelay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    ex,
                    "Embedding batch request failed transiently; retrying attempt {Attempt}/3 after {DelayMs} ms.",
                    attempt + 1,
                    retryDelay.Value.TotalMilliseconds);
            }
            finally
            {
                _requestGate.Release();
            }
            if (retryDelay.HasValue)
                await Task.Delay(retryDelay.Value, ct);
        }
    }

    private static bool IsRetryable(Exception exception)
    {
        if (exception is HttpOperationException operation)
            return operation.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                   operation.StatusCode is not null && (int)operation.StatusCode >= 500;
        return exception is HttpRequestException or TimeoutException ||
               exception is OperationCanceledException;
    }

    private static void EnsureEmbeddings(AiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.EmbeddingModel))
            throw new InvalidOperationException("Embedding 模型未配置，长期记忆与知识库功能需要它。");
        if (string.IsNullOrWhiteSpace(settings.ResolveEmbeddingApiKey()))
            throw new InvalidOperationException("Embedding API Key 未配置。");
    }
}
