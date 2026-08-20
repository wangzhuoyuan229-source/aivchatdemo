using ChatApp.Core.Services;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Repositories;
using ChatApp.Infrastructure.VectorStore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChatApp.Infrastructure;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        var connectionString = $"Data Source={AppPaths.DbPath}";
        services.AddDbContextFactory<AppDbContext>(opts => opts.UseSqlite(connectionString), ServiceLifetime.Singleton);

        services.AddSingleton<IVectorStore, SqliteVectorStore>();
        services.AddSingleton<IRoleService, RoleService>();
        services.AddSingleton<IChatHistoryService, ChatHistoryService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IUiSettingsService, UiSettingsService>();

        return services;
    }

    /// <summary>Creates the database schema and seeds preset roles. Call once at startup.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);

        // 对于旧版本数据库（EnsureCreated 不会更新已有表），手动迁移：添加 KnowledgeGroups 表和 GroupId 列
        await MigrateKnowledgeGroupsAsync(db, ct);
        // 群聊功能迁移：添加 ConversationMembers 表、Conversations.Type 列、RoleId 改可空
        await MigrateGroupChatAsync(db, ct);
        // 严格知识约束：角色示范对话 + 角色到知识分组的显式绑定
        await MigrateGroundedDialogueAsync(db, ct);
        // 图片知识项、独立消息附件与历史快照。
        await MigrateKnowledgeImagesAsync(db, ct);
        // 会话置顶 + 知识引用溯源列。
        await MigrateConversationExtrasAsync(db, ct);
        // 计划 3：消息/会话查询索引（幂等）。
        await MigratePlan3IndexesAsync(db, ct);

        // Preset roles seeding disabled — new databases start with an empty role library.
        // var roleService = services.GetRequiredService<IRoleService>();
        // await roleService.EnsurePresetsSeededAsync(ct);
    }

    /// <summary>
    /// Adds authored dialogue examples and explicit role-to-knowledge-group bindings.
    /// Existing roles intentionally start with no bindings so unrelated knowledge is
    /// never made visible implicitly. Idempotent for both old and newly-created stores.
    /// </summary>
    internal static async Task MigrateGroundedDialogueAsync(AppDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection() as SqliteConnection;
        if (conn is null) return;

        await conn.OpenAsync(ct);
        try
        {
            var roleColumns = await ReadColumnsAsync(conn, "Roles", ct);
            if (!roleColumns.ContainsKey("DialogueExamples"))
            {
                using var addExamples = new SqliteCommand(
                    "ALTER TABLE \"Roles\" ADD COLUMN \"DialogueExamples\" TEXT NOT NULL DEFAULT '';", conn);
                await addExamples.ExecuteNonQueryAsync(ct);
            }
            if (!roleColumns.ContainsKey("UserPersona"))
            {
                using var addUserPersona = new SqliteCommand(
                    "ALTER TABLE \"Roles\" ADD COLUMN \"UserPersona\" TEXT NOT NULL DEFAULT '';", conn);
                await addUserPersona.ExecuteNonQueryAsync(ct);
            }

            using (var createBindings = new SqliteCommand(
                "CREATE TABLE IF NOT EXISTS \"RoleKnowledgeGroups\" (" +
                "\"RoleId\" INTEGER NOT NULL, " +
                "\"KnowledgeGroupId\" INTEGER NOT NULL, " +
                "CONSTRAINT \"PK_RoleKnowledgeGroups\" PRIMARY KEY (\"RoleId\", \"KnowledgeGroupId\"));", conn))
            {
                await createBindings.ExecuteNonQueryAsync(ct);
            }

            using (var roleIndex = new SqliteCommand(
                "CREATE INDEX IF NOT EXISTS \"IX_RoleKnowledgeGroups_RoleId\" ON \"RoleKnowledgeGroups\" (\"RoleId\");", conn))
            {
                await roleIndex.ExecuteNonQueryAsync(ct);
            }

            using (var groupIndex = new SqliteCommand(
                "CREATE INDEX IF NOT EXISTS \"IX_RoleKnowledgeGroups_KnowledgeGroupId\" ON \"RoleKnowledgeGroups\" (\"KnowledgeGroupId\");", conn))
            {
                await groupIndex.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    /// <summary>Adds image knowledge metadata and immutable message attachments. Idempotent.</summary>
    internal static async Task MigrateKnowledgeImagesAsync(AppDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection() as SqliteConnection;
        if (conn is null) return;

        await conn.OpenAsync(ct);
        try
        {
            var columns = await ReadColumnsAsync(conn, "KnowledgeDocuments", ct);
            var additions = new (string Name, string Sql)[]
            {
                ("Kind", "INTEGER NOT NULL DEFAULT 0"),
                ("StorageKey", "TEXT NOT NULL DEFAULT ''"),
                ("MimeType", "TEXT NOT NULL DEFAULT ''"),
                ("FileSize", "INTEGER NOT NULL DEFAULT 0"),
                ("SemanticDescription", "TEXT NOT NULL DEFAULT ''"),
                ("Tags", "TEXT NOT NULL DEFAULT ''"),
                ("DescriptionSource", "INTEGER NOT NULL DEFAULT 0"),
                ("DescriptionProvider", "TEXT NOT NULL DEFAULT ''"),
                ("DescriptionModel", "TEXT NOT NULL DEFAULT ''"),
                ("SourceRelativePath", "TEXT NOT NULL DEFAULT ''")
            };
            foreach (var (name, sql) in additions)
            {
                if (columns.ContainsKey(name)) continue;
                using var add = new SqliteCommand(
                    $"ALTER TABLE \"KnowledgeDocuments\" ADD COLUMN \"{name}\" {sql};", conn);
                await add.ExecuteNonQueryAsync(ct);
            }

            using (var create = new SqliteCommand(
                "CREATE TABLE IF NOT EXISTS \"MessageAttachments\" (" +
                "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_MessageAttachments\" PRIMARY KEY AUTOINCREMENT, " +
                "\"MessageId\" INTEGER NOT NULL, " +
                "\"Kind\" INTEGER NOT NULL DEFAULT 0, " +
                "\"StorageKey\" TEXT NOT NULL DEFAULT '', " +
                "\"MimeType\" TEXT NOT NULL DEFAULT '', " +
                "\"FileName\" TEXT NOT NULL DEFAULT '', " +
                "\"Title\" TEXT NOT NULL DEFAULT '', " +
                "\"Caption\" TEXT NOT NULL DEFAULT '', " +
                "\"SourceKnowledgeDocumentId\" INTEGER NULL);", conn))
            {
                await create.ExecuteNonQueryAsync(ct);
            }
            var attachmentColumns = await ReadColumnsAsync(conn, "MessageAttachments", ct);
            if (!attachmentColumns.ContainsKey("Title"))
            {
                using var addTitle = new SqliteCommand(
                    "ALTER TABLE \"MessageAttachments\" ADD COLUMN \"Title\" TEXT NOT NULL DEFAULT '';", conn);
                await addTitle.ExecuteNonQueryAsync(ct);
            }
            using var index = new SqliteCommand(
                "CREATE INDEX IF NOT EXISTS \"IX_MessageAttachments_MessageId\" ON \"MessageAttachments\" (\"MessageId\");", conn);
            await index.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    /// <summary>
    /// 兼容旧数据库：如果 KnowledgeGroups 表不存在，则创建它并给 KnowledgeDocuments 加 GroupId 列。
    /// 新建数据库已经由 EnsureCreated 处理，这里只处理旧库升级。
    /// </summary>
    private static async Task MigrateKnowledgeGroupsAsync(AppDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection() as SqliteConnection;
        if (conn is null) return;

        await conn.OpenAsync(ct);
        try
        {
            // 检查 KnowledgeGroups 表是否存在
            using (var checkTable = new SqliteCommand(
                "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='KnowledgeGroups';", conn))
            {
                var exists = Convert.ToInt32(await checkTable.ExecuteScalarAsync(ct)) > 0;
                if (exists) return; // 已迁移过
            }

            // 创建 KnowledgeGroups 表
            using (var createTable = new SqliteCommand(
                "CREATE TABLE \"KnowledgeGroups\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_KnowledgeGroups\" PRIMARY KEY AUTOINCREMENT, \"Name\" TEXT NOT NULL, \"CreatedAt\" TEXT NOT NULL);", conn))
            {
                await createTable.ExecuteNonQueryAsync(ct);
            }
            using (var createIndex = new SqliteCommand(
                "CREATE UNIQUE INDEX \"IX_KnowledgeGroups_Name\" ON \"KnowledgeGroups\" (\"Name\");", conn))
            {
                await createIndex.ExecuteNonQueryAsync(ct);
            }

            // 给 KnowledgeDocuments 表添加 GroupId 列（可空）
            using (var addColumn = new SqliteCommand(
                "ALTER TABLE \"KnowledgeDocuments\" ADD COLUMN \"GroupId\" INTEGER NULL;", conn))
            {
                await addColumn.ExecuteNonQueryAsync(ct);
            }
            using (var createIndex2 = new SqliteCommand(
                "CREATE INDEX \"IX_KnowledgeDocuments_GroupId\" ON \"KnowledgeDocuments\" (\"GroupId\");", conn))
            {
                await createIndex2.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    /// <summary>
    /// 兼容旧数据库：群聊功能迁移。
    /// 1) 创建 ConversationMembers 表；
    /// 2) 给 Conversations 加 Type 列（INTEGER NOT NULL DEFAULT 0 = Private）；
    /// 3) 把 Conversations.RoleId 由 NOT NULL 改为可空（SQLite 需表重建）。
    /// 新建数据库已由 EnsureCreated 处理，这里只处理旧库升级。幂等。
    /// </summary>
    private static async Task MigrateGroupChatAsync(AppDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection() as SqliteConnection;
        if (conn is null) return;

        await conn.OpenAsync(ct);
        try
        {
            // 总闸：ConversationMembers 表已存在则视为已迁移
            using (var checkTable = new SqliteCommand(
                "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='ConversationMembers';", conn))
            {
                var exists = Convert.ToInt32(await checkTable.ExecuteScalarAsync(ct)) > 0;
                if (exists) return;
            }

            // (a) 创建 ConversationMembers 表
            using (var createMembers = new SqliteCommand(
                "CREATE TABLE \"ConversationMembers\" (" +
                "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_ConversationMembers\" PRIMARY KEY AUTOINCREMENT, " +
                "\"ConversationId\" INTEGER NOT NULL, " +
                "\"RoleId\" INTEGER NOT NULL, " +
                "\"JoinedAt\" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, " +
                "\"DisplayOrder\" INTEGER NOT NULL DEFAULT 0);", conn))
            {
                await createMembers.ExecuteNonQueryAsync(ct);
            }
            using (var ix1 = new SqliteCommand(
                "CREATE INDEX \"IX_ConversationMembers_ConversationId\" ON \"ConversationMembers\" (\"ConversationId\");", conn))
            {
                await ix1.ExecuteNonQueryAsync(ct);
            }
            using (var ix2 = new SqliteCommand(
                "CREATE INDEX \"IX_ConversationMembers_RoleId\" ON \"ConversationMembers\" (\"RoleId\");", conn))
            {
                await ix2.ExecuteNonQueryAsync(ct);
            }

            // (b) Conversations 加 Type 列（若不存在）
            var convCols = await ReadColumnsAsync(conn, "Conversations", ct);
            if (!convCols.ContainsKey("Type"))
            {
                using (var addType = new SqliteCommand(
                    "ALTER TABLE \"Conversations\" ADD COLUMN \"Type\" INTEGER NOT NULL DEFAULT 0;", conn))
                {
                    await addType.ExecuteNonQueryAsync(ct);
                }
            }

            // (c) RoleId 改可空：仅当当前 RoleId 列为 NOT NULL 时执行表重建
            if (convCols.TryGetValue("RoleId", out var roleIdNotNull) && roleIdNotNull)
            {
                // 重新读取（若上一步加了 Type，列表已变化）；确认 Type 列存在
                var colsAfter = await ReadColumnsAsync(conn, "Conversations", ct);
                var hasType = colsAfter.ContainsKey("Type");

                using var rebuild = new SqliteCommand(
                    "CREATE TABLE \"Conversations_new\" (" +
                    "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Conversations\" PRIMARY KEY AUTOINCREMENT, " +
                    "\"RoleId\" INTEGER NULL, " +
                    (hasType ? "\"Type\" INTEGER NOT NULL DEFAULT 0, " : "") +
                    "\"Title\" TEXT NOT NULL DEFAULT '', " +
                    "\"CreatedAt\" TEXT NOT NULL, " +
                    "\"UpdatedAt\" TEXT NOT NULL);", conn);
                await rebuild.ExecuteNonQueryAsync(ct);

                var colList = hasType
                    ? "\"Id\",\"RoleId\",\"Type\",\"Title\",\"CreatedAt\",\"UpdatedAt\""
                    : "\"Id\",\"RoleId\",\"Title\",\"CreatedAt\",\"UpdatedAt\"";
                using (var copy = new SqliteCommand(
                    $"INSERT INTO \"Conversations_new\" ({colList}) SELECT {colList} FROM \"Conversations\";", conn))
                {
                    await copy.ExecuteNonQueryAsync(ct);
                }
                using (var drop = new SqliteCommand("DROP TABLE \"Conversations\";", conn))
                {
                    await drop.ExecuteNonQueryAsync(ct);
                }
                using (var rename = new SqliteCommand("ALTER TABLE \"Conversations_new\" RENAME TO \"Conversations\";", conn))
                {
                    await rename.ExecuteNonQueryAsync(ct);
                }
                using (var ixRole = new SqliteCommand(
                    "CREATE INDEX \"IX_Conversations_RoleId\" ON \"Conversations\" (\"RoleId\");", conn))
                {
                    await ixRole.ExecuteNonQueryAsync(ct);
                }
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    /// <summary>Adds conversation pinning and message citation columns. Idempotent.</summary>
    internal static async Task MigrateConversationExtrasAsync(AppDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection() as SqliteConnection;
        if (conn is null) return;

        await conn.OpenAsync(ct);
        try
        {
            var conversationColumns = await ReadColumnsAsync(conn, "Conversations", ct);
            if (!conversationColumns.ContainsKey("IsPinned"))
            {
                using var addPinned = new SqliteCommand(
                    "ALTER TABLE \"Conversations\" ADD COLUMN \"IsPinned\" INTEGER NOT NULL DEFAULT 0;", conn);
                await addPinned.ExecuteNonQueryAsync(ct);
            }

            var messageColumns = await ReadColumnsAsync(conn, "Messages", ct);
            if (messageColumns.Count > 0 && !messageColumns.ContainsKey("CitedDocumentIds"))
            {
                using var addCitations = new SqliteCommand(
                    "ALTER TABLE \"Messages\" ADD COLUMN \"CitedDocumentIds\" TEXT NOT NULL DEFAULT '';", conn);
                await addCitations.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    /// <summary>Adds plan-3 query indexes (message window + pinned sort). Idempotent.</summary>
    internal static async Task MigratePlan3IndexesAsync(AppDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection() as SqliteConnection;
        if (conn is null) return;

        await conn.OpenAsync(ct);
        try
        {
            await using var messageIndex = new SqliteCommand(
                "CREATE INDEX IF NOT EXISTS \"IX_Messages_ConversationId_Id\" " +
                "ON \"Messages\" (\"ConversationId\", \"Id\");", conn);
            await messageIndex.ExecuteNonQueryAsync(ct);

            await using var conversationIndex = new SqliteCommand(
                "CREATE INDEX IF NOT EXISTS \"IX_Conversations_IsPinned_UpdatedAt\" " +
                "ON \"Conversations\" (\"IsPinned\", \"UpdatedAt\");", conn);
            await conversationIndex.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    /// <summary>Returns a map of column-name → notnull flag for the given table.</summary>
    private static async Task<Dictionary<string, bool>> ReadColumnsAsync(SqliteConnection conn, string table, CancellationToken ct)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqliteCommand($"PRAGMA table_info(\"{table}\");", conn);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            // columns: cid(0), name(1), type(2), notnull(3), dflt_value(4), pk(5)
            var name = reader.GetString(1);
            var notnull = reader.GetInt32(3) == 1;
            result[name] = notnull;
        }
        return result;
    }
}
