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

        // Preset roles seeding disabled — new databases start with an empty role library.
        // var roleService = services.GetRequiredService<IRoleService>();
        // await roleService.EnsurePresetsSeededAsync(ct);
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
