using ChatApp.Core.Models;
using ChatApp.Core.Settings;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Infrastructure.Data;

/// <summary>Simple key-value setting row for persisting <see cref="AiSettings"/> as JSON.</summary>
public class Setting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class AppDbContext : DbContext
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<MemoryEntry> MemoryEntries => Set<MemoryEntry>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<KnowledgeGroup> KnowledgeGroups => Set<KnowledgeGroup>();
    public DbSet<RoleKnowledgeGroup> RoleKnowledgeGroups => Set<RoleKnowledgeGroup>();
    public DbSet<Setting> Settings => Set<Setting>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Setting>(e => e.HasKey(x => x.Key));

        b.Entity<Role>(e =>
        {
            e.Property(x => x.Avatar).HasDefaultValue("");
            e.Property(x => x.Description).HasDefaultValue("");
            e.Property(x => x.Background).HasDefaultValue("");
            e.Property(x => x.UserPersona).HasDefaultValue("");
            e.Property(x => x.Personality).HasDefaultValue("");
            e.Property(x => x.SpeakingStyle).HasDefaultValue("");
            e.Property(x => x.SystemPrompt).HasDefaultValue("");
            e.Property(x => x.DialogueExamples).HasDefaultValue("");
            e.Property(x => x.Greeting).HasDefaultValue("");
            e.Property(x => x.PromptTemplateVersion).HasDefaultValue(0);
        });

        b.Entity<RoleKnowledgeGroup>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.KnowledgeGroupId });
            e.HasIndex(x => x.RoleId);
            e.HasIndex(x => x.KnowledgeGroupId);
        });

        b.Entity<Message>(e =>
        {
            e.HasIndex(x => x.ConversationId);
            e.HasIndex(x => new { x.ConversationId, x.Id });
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.CitedDocumentIds).HasDefaultValue("");
            e.HasMany(x => x.Attachments)
                .WithOne(x => x.Message)
                .HasForeignKey(x => x.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MessageAttachment>(e =>
        {
            e.HasIndex(x => x.MessageId);
            e.Property(x => x.StorageKey).HasDefaultValue("");
            e.Property(x => x.MimeType).HasDefaultValue("");
            e.Property(x => x.FileName).HasDefaultValue("");
            e.Property(x => x.Title).HasDefaultValue("");
            e.Property(x => x.Caption).HasDefaultValue("");
        });

        b.Entity<Conversation>(e =>
        {
            e.HasIndex(x => x.RoleId);
            e.HasIndex(x => new { x.IsPinned, x.UpdatedAt });
            e.Property(x => x.Title).HasDefaultValue("");
            e.Property(x => x.Avatar).HasDefaultValue("");
            e.Property(x => x.IsPinned).HasDefaultValue(false);
        });

        b.Entity<ConversationMember>(e =>
        {
            e.HasIndex(x => x.ConversationId);
            e.HasIndex(x => x.RoleId);
            e.Property(x => x.JoinedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        b.Entity<MemoryEntry>(e =>
        {
            e.HasIndex(x => x.RoleId);
            e.Property(x => x.ExternalId).HasDefaultValue("");
        });

        b.Entity<KnowledgeDocument>(e =>
        {
            e.Property(x => x.Title).HasDefaultValue("");
            e.Property(x => x.FileName).HasDefaultValue("");
            e.Property(x => x.StorageKey).HasDefaultValue("");
            e.Property(x => x.MimeType).HasDefaultValue("");
            e.Property(x => x.SemanticDescription).HasDefaultValue("");
            e.Property(x => x.Tags).HasDefaultValue("");
            e.Property(x => x.DescriptionProvider).HasDefaultValue("");
            e.Property(x => x.DescriptionModel).HasDefaultValue("");
            e.Property(x => x.SourceRelativePath).HasDefaultValue("");
            e.HasIndex(x => x.GroupId);
        });

        b.Entity<KnowledgeChunk>(e =>
        {
            e.HasIndex(x => x.DocumentId);
            e.Property(x => x.ExternalId).HasDefaultValue("");
        });

        b.Entity<KnowledgeGroup>(e =>
        {
            e.Property(x => x.Name).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });
    }
}
