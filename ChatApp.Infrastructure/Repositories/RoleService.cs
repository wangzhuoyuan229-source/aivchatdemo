using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatApp.Infrastructure.Repositories;

public class RoleService : IRoleService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<RoleService> _logger;

    public RoleService(IDbContextFactory<AppDbContext> factory, ILogger<RoleService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);
    }

    public async Task<Role?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<Role> CreateAsync(Role role, CancellationToken ct = default)
    {
        role.IsPreset = false;
        role.CreatedAt = DateTime.UtcNow;
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return role;
    }

    public async Task UpdateAsync(Role role, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Roles.Update(role);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return;

        // 级联删除：先收集私聊会话 ID，删除其下所有消息，再删会话、记忆条目，最后删角色本身
        var conversationIds = await db.Conversations
            .Where(c => c.RoleId == id)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (conversationIds.Count > 0)
        {
            db.Messages.RemoveRange(db.Messages.Where(m => conversationIds.Contains(m.ConversationId)));
            db.Conversations.RemoveRange(db.Conversations.Where(c => c.RoleId == id));
        }

        // 群聊成员清理：移除该角色在所有群聊中的成员记录，
        // 并删除因成员被清空而变空的群聊（连同其消息）。
        var groupIdsWithRole = await db.Conversations
            .Where(c => c.Type == ConversationType.Group
                && db.ConversationMembers.Any(m => m.ConversationId == c.Id && m.RoleId == id))
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (groupIdsWithRole.Count > 0)
        {
            db.ConversationMembers.RemoveRange(
                db.ConversationMembers.Where(m => m.RoleId == id));
            await db.SaveChangesAsync(ct);

            var emptyGroupIds = await db.Conversations
                .Where(c => c.Type == ConversationType.Group
                    && groupIdsWithRole.Contains(c.Id)
                    && !db.ConversationMembers.Any(m => m.ConversationId == c.Id))
                .Select(c => c.Id)
                .ToListAsync(ct);

            if (emptyGroupIds.Count > 0)
            {
                db.Messages.RemoveRange(db.Messages.Where(m => emptyGroupIds.Contains(m.ConversationId)));
                db.Conversations.RemoveRange(db.Conversations.Where(c => emptyGroupIds.Contains(c.Id)));
            }
        }

        db.MemoryEntries.RemoveRange(db.MemoryEntries.Where(m => m.RoleId == id));
        db.Roles.Remove(role);

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Deleted role {Id} ({IsPreset}) along with {Conv} conversation(s).", id, role.IsPreset, conversationIds.Count);
    }

    public async Task EnsurePresetsSeededAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.Roles.AnyAsync(r => r.IsPreset, ct))
        {
            _logger.LogDebug("Preset roles already seeded.");
            return;
        }

        foreach (var preset in PresetRoles.All)
        {
            preset.IsPreset = true;
            db.Roles.Add(preset);
        }
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} preset roles.", PresetRoles.All.Length);
    }
}
