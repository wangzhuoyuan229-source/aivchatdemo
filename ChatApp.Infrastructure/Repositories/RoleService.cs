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
    private readonly IVectorStore? _vectors;

    public RoleService(
        IDbContextFactory<AppDbContext> factory,
        ILogger<RoleService> logger,
        IVectorStore? vectors = null)
    {
        _factory = factory;
        _logger = logger;
        _vectors = vectors;
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

    public async Task<IReadOnlyList<int>> GetKnowledgeGroupIdsAsync(int roleId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RoleKnowledgeGroups.AsNoTracking()
            .Where(x => x.RoleId == roleId)
            .OrderBy(x => x.KnowledgeGroupId)
            .Select(x => x.KnowledgeGroupId)
            .ToListAsync(ct);
    }

    public async Task SetKnowledgeGroupIdsAsync(int roleId, IReadOnlyCollection<int> groupIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);

        await using var db = await _factory.CreateDbContextAsync(ct);
        if (!await db.Roles.AnyAsync(r => r.Id == roleId, ct))
            throw new KeyNotFoundException($"角色不存在：{roleId}");

        var normalized = groupIds.Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        if (normalized.Length > 0)
        {
            var existingGroupIds = await db.KnowledgeGroups
                .Where(g => normalized.Contains(g.Id))
                .Select(g => g.Id)
                .ToListAsync(ct);
            var missing = normalized.Except(existingGroupIds).ToArray();
            if (missing.Length > 0)
                throw new KeyNotFoundException($"知识分组不存在：{string.Join(", ", missing)}");
        }

        var current = await db.RoleKnowledgeGroups.Where(x => x.RoleId == roleId).ToListAsync(ct);
        db.RoleKnowledgeGroups.RemoveRange(current.Where(x => !normalized.Contains(x.KnowledgeGroupId)));
        var currentIds = current.Select(x => x.KnowledgeGroupId).ToHashSet();
        foreach (var groupId in normalized.Where(id => !currentIds.Contains(id)))
        {
            db.RoleKnowledgeGroups.Add(new RoleKnowledgeGroup
            {
                RoleId = roleId,
                KnowledgeGroupId = groupId
            });
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Role {RoleId} knowledge bindings replaced with {Count} group(s).", roleId, normalized.Length);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return;
        var attachmentStorageKeys = new HashSet<string>(StringComparer.Ordinal);
        var memoryVectorIds = await db.MemoryEntries
            .Where(memory => memory.RoleId == id && memory.ExternalId != string.Empty)
            .Select(memory => memory.ExternalId)
            .Distinct()
            .ToListAsync(ct);

        // 级联删除：先收集私聊会话 ID，删除其下所有消息，再删会话、记忆条目，最后删角色本身
        var conversationIds = await db.Conversations
            .Where(c => c.RoleId == id)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (conversationIds.Count > 0)
        {
            var messageIds = await db.Messages.Where(m => conversationIds.Contains(m.ConversationId))
                .Select(m => m.Id).ToListAsync(ct);
            var attachments = await db.MessageAttachments.Where(a => messageIds.Contains(a.MessageId)).ToListAsync(ct);
            foreach (var attachment in attachments) attachmentStorageKeys.Add(attachment.StorageKey);
            db.MessageAttachments.RemoveRange(attachments);
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
                var messageIds = await db.Messages.Where(m => emptyGroupIds.Contains(m.ConversationId))
                    .Select(m => m.Id).ToListAsync(ct);
                var attachments = await db.MessageAttachments.Where(a => messageIds.Contains(a.MessageId)).ToListAsync(ct);
                foreach (var attachment in attachments) attachmentStorageKeys.Add(attachment.StorageKey);
                db.MessageAttachments.RemoveRange(attachments);
                db.Messages.RemoveRange(db.Messages.Where(m => emptyGroupIds.Contains(m.ConversationId)));
                db.Conversations.RemoveRange(db.Conversations.Where(c => emptyGroupIds.Contains(c.Id)));
            }
        }

        db.MemoryEntries.RemoveRange(db.MemoryEntries.Where(m => m.RoleId == id));
        db.RoleKnowledgeGroups.RemoveRange(db.RoleKnowledgeGroups.Where(x => x.RoleId == id));
        db.Roles.Remove(role);

        await db.SaveChangesAsync(ct);
        if (_vectors is not null)
        {
            foreach (var vectorId in memoryVectorIds)
            {
                try { await _vectors.DeleteAsync(vectorId, CancellationToken.None); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete shared memory vector {VectorId} for role {RoleId}.", vectorId, id);
                }
            }
        }
        foreach (var storageKey in attachmentStorageKeys.Where(key => !string.IsNullOrWhiteSpace(key)))
        {
            try
            {
                var path = AppPaths.ResolveMessageAttachmentStorageKey(storageKey);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* database deletion remains authoritative */ }
        }
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
