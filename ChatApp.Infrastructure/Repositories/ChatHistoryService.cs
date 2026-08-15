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
        return await q.OrderByDescending(c => c.UpdatedAt).ToListAsync(ct);
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

    public async Task<Conversation> CreateGroupConversationAsync(string title, IReadOnlyList<int> memberRoleIds, CancellationToken ct = default)
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

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(int conversationId, int limit = 1000, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var recent = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .ToListAsync(ct);
        recent.Reverse();
        return recent;
    }

    public async Task<Message> AddMessageAsync(Message message, CancellationToken ct = default)
    {
        message.CreatedAt = DateTime.UtcNow;
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Messages.Add(message);
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == message.ConversationId, ct);
        if (conv is not null) conv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return message;
    }

    public async Task DeleteMessageAsync(int messageId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (msg is null) return;
        db.Messages.Remove(msg);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteConversationAsync(int conversationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conv is null) return;
        db.Messages.RemoveRange(db.Messages.Where(m => m.ConversationId == conversationId));
        db.Conversations.Remove(conv);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Message>> SearchAsync(string keyword, int? conversationId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<Message>();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var pattern = $"%{keyword}%";
        var q = db.Messages.AsNoTracking().Where(m => EF.Functions.Like(m.Content, pattern));
        if (conversationId.HasValue) q = q.Where(m => m.ConversationId == conversationId.Value);
        return await q.OrderByDescending(m => m.Id).Take(200).ToListAsync(ct);
    }
}
