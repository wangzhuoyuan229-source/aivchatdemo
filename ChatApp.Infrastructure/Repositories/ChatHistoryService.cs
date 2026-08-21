using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatApp.Infrastructure.Repositories;

public class ChatHistoryService : IChatHistoryService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<ChatHistoryService> _logger;

    public ChatHistoryService(IDbContextFactory<AppDbContext> factory, ILogger<ChatHistoryService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Conversation>> GetConversationsAsync(int? roleId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Conversations.AsNoTracking();
        if (roleId.HasValue) q = q.Where(c => c.RoleId == roleId.Value);
        return await q
            .OrderByDescending(c => c.IsPinned)
            .ThenByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<Conversation> CreateConversationAsync(int roleId, string? title = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var conv = new Conversation
        {
            RoleId = roleId,
            Type = ConversationType.Private,
            Title = title ?? $"对话 {DateTime.Now:MM-dd HH:mm}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync(ct);
        return conv;
    }

    public async Task<Conversation> CreateGroupConversationAsync(string title, IReadOnlyList<int> memberRoleIds,
        string? avatar = null, CancellationToken ct = default)
    {
        if (memberRoleIds is null || memberRoleIds.Count < 2)
            throw new ArgumentException("群聊至少需要 2 个成员角色。", nameof(memberRoleIds));

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var conv = new Conversation
        {
            RoleId = null,
            Type = ConversationType.Group,
            Title = string.IsNullOrWhiteSpace(title) ? $"群聊 {DateTime.Now:MM-dd HH:mm}" : title,
            Avatar = avatar?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync(ct);

        for (int i = 0; i < memberRoleIds.Count; i++)
        {
            db.ConversationMembers.Add(new ConversationMember
            {
                ConversationId = conv.Id,
                RoleId = memberRoleIds[i],
                DisplayOrder = i,
                JoinedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return conv;
    }

    public async Task<IReadOnlyList<ConversationMember>> GetMembersAsync(int conversationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ConversationMembers.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<Conversation?> GetConversationAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(int conversationId, int limit = 1000, CancellationToken ct = default, int? beforeId = null)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Messages.AsNoTracking()
            .Include(m => m.Attachments)
            .Where(m => m.ConversationId == conversationId);
        if (beforeId.HasValue)
            q = q.Where(m => m.Id < beforeId.Value);
        var recent = await q
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .ToListAsync(ct);
        recent.Reverse();
        return recent;
    }

    public async Task<Message> AddMessageAsync(Message message, CancellationToken ct = default)
    {
        message.CreatedAt = DateTime.UtcNow;
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            db.Messages.Add(message);
            var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == message.ConversationId, ct);
            if (conv is not null) conv.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return message;
        }
        catch
        {
            DeleteAttachmentFiles(message.Attachments.Select(a => a.StorageKey));
            throw;
        }
    }

    public async Task DeleteMessageAsync(int messageId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var msg = await db.Messages.Include(m => m.Attachments).FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (msg is null) return;
        var storageKeys = msg.Attachments.Select(a => a.StorageKey).ToList();
        db.MessageAttachments.RemoveRange(msg.Attachments);
        db.Messages.Remove(msg);
        await db.SaveChangesAsync(ct);
        DeleteAttachmentFiles(storageKeys);
    }

    public async Task<int> DeleteMessagesFromAsync(int conversationId, int messageIdInclusive, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var targets = await db.Messages
            .Where(m => m.ConversationId == conversationId && m.Id >= messageIdInclusive)
            .Select(m => m.Id)
            .ToListAsync(ct);
        if (targets.Count == 0) return 0;

        var attachments = await db.MessageAttachments.Where(a => targets.Contains(a.MessageId)).ToListAsync(ct);
        var storageKeys = attachments.Select(a => a.StorageKey).ToList();
        db.MessageAttachments.RemoveRange(attachments);
        db.Messages.RemoveRange(db.Messages.Where(m => targets.Contains(m.Id)));
        await db.SaveChangesAsync(ct);
        DeleteAttachmentFiles(storageKeys);
        return targets.Count;
    }

    public async Task RenameConversationAsync(int conversationId, string title, CancellationToken ct = default)
    {
        var normalized = title.Trim();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conv is null || string.IsNullOrWhiteSpace(normalized)) return;
        conv.Title = normalized;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetConversationPinnedAsync(int conversationId, bool pinned, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conv is null || conv.IsPinned == pinned) return;
        conv.IsPinned = pinned;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteConversationAsync(int conversationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conv is null) return;
        var messageIds = await db.Messages.Where(m => m.ConversationId == conversationId).Select(m => m.Id).ToListAsync(ct);
        var attachments = await db.MessageAttachments.Where(a => messageIds.Contains(a.MessageId)).ToListAsync(ct);
        var storageKeys = attachments.Select(a => a.StorageKey).ToList();
        db.MessageAttachments.RemoveRange(attachments);
        db.Messages.RemoveRange(db.Messages.Where(m => m.ConversationId == conversationId));
        db.Conversations.Remove(conv);
        await db.SaveChangesAsync(ct);
        DeleteAttachmentFiles(storageKeys);
    }

    public async Task<IReadOnlyList<Message>> SearchAsync(string keyword, int? conversationId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<Message>();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var pattern = $"%{keyword}%";
        var q = db.Messages.AsNoTracking().Include(m => m.Attachments)
            .Where(m => EF.Functions.Like(m.Content, pattern));
        if (conversationId.HasValue) q = q.Where(m => m.ConversationId == conversationId.Value);
        return await q.OrderByDescending(m => m.Id).Take(200).ToListAsync(ct);
    }

    private static void DeleteAttachmentFiles(IEnumerable<string> storageKeys)
    {
        foreach (var key in storageKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            try
            {
                var path = AppPaths.ResolveMessageAttachmentStorageKey(key);
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Database state is authoritative; stale files may be cleaned manually.
            }
        }
    }
}
