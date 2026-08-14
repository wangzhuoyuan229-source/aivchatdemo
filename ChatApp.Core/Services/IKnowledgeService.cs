using ChatApp.Core.Models;

namespace ChatApp.Core.Services;

/// <summary>Knowledge-base import, chunking, vectorization and retrieval (F5).</summary>
public interface IKnowledgeService
{
    Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsAsync(CancellationToken ct = default);

    /// <summary>按分组列出文档。groupId = null 表示「未分组」。</summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsByGroupAsync(int? groupId, CancellationToken ct = default);

    Task<KnowledgeDocument?> GetDocumentAsync(int id, CancellationToken ct = default);

    /// <summary>Imports a file (.txt/.md/.pdf), chunks it, embeds the chunks and stores them.</summary>
    /// <param name="groupId">Optional target group. Null = ungrouped.</param>
    Task<KnowledgeDocument> ImportAsync(string filePath, IProgress<(int done, int total)>? progress = null, CancellationToken ct = default, int? groupId = null);

    /// <summary>Imports all supported files (.txt/.md/.markdown/.pdf) from a directory, optionally recursively.</summary>
    /// <param name="groupId">Optional target group. Null = ungrouped.</param>
    Task<IReadOnlyList<KnowledgeDocument>> ImportDirectoryAsync(string directoryPath, bool recursive, IProgress<(int doneFiles, int totalFiles, string currentFile)>? progress = null, CancellationToken ct = default, int? groupId = null);

    Task<IReadOnlyList<KnowledgeChunk>> GetChunksAsync(int documentId, CancellationToken ct = default);

    /// <summary>Retrieves the Top-K chunks relevant to the query.</summary>
    Task<IReadOnlyList<VectorSearchHit>> RetrieveAsync(string query, int topK, CancellationToken ct = default);

    Task DeleteDocumentAsync(int id, CancellationToken ct = default);

    // ----- 知识库分组管理 -----

    Task<IReadOnlyList<KnowledgeGroup>> ListGroupsAsync(CancellationToken ct = default);

    Task<KnowledgeGroup> CreateGroupAsync(string name, CancellationToken ct = default);

    Task RenameGroupAsync(int id, string newName, CancellationToken ct = default);

    /// <summary>
    /// 删除分组。
    /// </summary>
    /// <param name="deleteDocuments">true: 一并删除该组下所有文档（含向量）。false: 把组内文档移到「未分组」。</param>
    Task DeleteGroupAsync(int id, bool deleteDocuments, CancellationToken ct = default);

    /// <summary>把文档移动到指定分组。groupId 为 null 表示移到「未分组」。</summary>
    Task MoveDocumentAsync(int documentId, int? groupId, CancellationToken ct = default);

    // ----- 批量操作 -----

    /// <summary>批量删除文档（含向量与分块）。</summary>
    Task DeleteDocumentsAsync(IReadOnlyList<int> documentIds, CancellationToken ct = default);

    /// <summary>批量移动文档到指定分组。</summary>
    Task MoveDocumentsAsync(IReadOnlyList<int> documentIds, int? groupId, CancellationToken ct = default);
}
