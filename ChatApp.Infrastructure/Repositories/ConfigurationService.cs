using System.Text.Json;
using ChatApp.Core.Security;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Infrastructure.Repositories;

public class ConfigurationService : IConfigurationService
{
    private const string Key = "ai";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<AppDbContext> _factory;

    public ConfigurationService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<AiSettings> LoadAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Value))
            return new AiSettings { UseUnifiedApi = true };
        try
        {
            var settings = JsonSerializer.Deserialize<AiSettings>(row.Value, JsonOpts) ?? new AiSettings();
            // Preserve legacy databases that predate UseUnifiedApi (v1.2.0): missing flag must stay independent (false)
            if (row.Value.IndexOf("useUnifiedApi", StringComparison.OrdinalIgnoreCase) < 0)
                settings.UseUnifiedApi = false;
            settings.MigrateToRemoteApiOnly();
            RegisterSecrets(settings);
            return settings;
        }
        catch
        {
            return new AiSettings { UseUnifiedApi = true };
        }
    }

    public async Task SaveAsync(AiSettings settings, CancellationToken ct = default)
    {
        RegisterSecrets(settings);
        settings.ApiBaseUrl = RemoteApiEndpointPolicy
            .NormalizeOrThrow(settings.ApiBaseUrl)
            .ToString().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(settings.EmbeddingApiBaseUrl))
        {
            settings.EmbeddingApiBaseUrl = RemoteApiEndpointPolicy
                .NormalizeOrThrow(settings.EmbeddingApiBaseUrl)
                .ToString().TrimEnd('/');
        }
        if (!string.IsNullOrWhiteSpace(settings.VisionApiBaseUrl))
        {
            settings.VisionApiBaseUrl = RemoteApiEndpointPolicy
                .NormalizeHostedApiOrThrow(settings.VisionApiBaseUrl)
                .ToString().TrimEnd('/');
        }
        await using var db = await _factory.CreateDbContextAsync(ct);
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null)
            db.Settings.Add(new Setting { Key = Key, Value = json });
        else
            row.Value = json;
        await db.SaveChangesAsync(ct);
    }

    private static void RegisterSecrets(AiSettings settings)
    {
        SecretRedaction.Register(settings.ApiKey);
        SecretRedaction.Register(settings.ResolveEmbeddingApiKey());
        SecretRedaction.Register(settings.ResolveVisionApiKey());
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var s = await LoadAsync(ct);
        return RemoteApiEndpointPolicy.TryNormalize(s.ApiBaseUrl, out _, out _) &&
               !string.IsNullOrWhiteSpace(s.ApiKey) &&
               !string.IsNullOrWhiteSpace(s.ChatModel);
    }
}
