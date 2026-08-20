using ChatApp.Core.Settings;

namespace ChatApp.Core.Services;

/// <summary>Loads and persists UI-only preferences (theme, reading).</summary>
public interface IUiSettingsService
{
    Task<UiSettings> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(UiSettings settings, CancellationToken ct = default);
}
