using System.Text.Json;
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
            return new AiSettings();
        try
        {
            return JsonSerializer.Deserialize<AiSettings>(row.Value, JsonOpts) ?? new AiSettings();
        }
        catch
        {
            return new AiSettings();
        }
    }

    public async Task SaveAsync(AiSettings settings, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null)
            db.Settings.Add(new Setting { Key = Key, Value = json });
        else
            row.Value = json;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var s = await LoadAsync(ct);
        return !string.IsNullOrWhiteSpace(s.ApiKey) && !string.IsNullOrWhiteSpace(s.ChatModel);
    }
}
