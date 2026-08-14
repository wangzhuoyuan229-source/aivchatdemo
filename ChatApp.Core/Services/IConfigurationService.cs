using ChatApp.Core.Settings;

namespace ChatApp.Core.Services;

/// <summary>Persists and exposes user AI settings (BYOK).</summary>
public interface IConfigurationService
{
    Task<AiSettings> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(AiSettings settings, CancellationToken ct = default);

    /// <summary>True when an API key and chat model are configured.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);
}
