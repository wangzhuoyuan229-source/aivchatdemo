using System.Text.Json;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Infrastructure.Repositories;

/// <summary>Persists <see cref="UiSettings"/> as JSON in the Settings table (key = "ui").</summary>
public class UiSettingsService : IUiSettingsService
{
    private const string Key = "ui";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<AppDbContext> _factory;

    public UiSettingsService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<UiSettings> LoadAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Value))
            return new UiSettings();
        try
        {
            return JsonSerializer.Deserialize<UiSettings>(row.Value, JsonOpts) ?? new UiSettings();
        }
        catch
        {
            return new UiSettings();
        }
    }

    public async Task SaveAsync(UiSettings settings, CancellationToken ct = default)
    {
        settings.ChatFontSize = Math.Clamp(settings.ChatFontSize, 12, 22);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null)
            db.Settings.Add(new Setting { Key = Key, Value = json });
        else
            row.Value = json;
        await db.SaveChangesAsync(ct);
    }
}
