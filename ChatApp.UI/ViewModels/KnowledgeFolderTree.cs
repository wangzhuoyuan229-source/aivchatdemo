using ChatApp.Core.Models;

namespace ChatApp.UI.ViewModels;

/// <summary>
/// Builds a flattened, indented virtual folder tree from persisted source-relative paths.
/// Knowledge groups remain the authorization boundary; folders are navigation nodes inside a group.
/// </summary>
public static class KnowledgeFolderTree
{
    public static IReadOnlyList<GroupNode> Build(
        IReadOnlyList<KnowledgeGroup> groups,
        IReadOnlyList<KnowledgeDocument> documents)
    {
        var nodes = new List<GroupNode>
        {
            new() { DisplayName = "全部", GroupId = -1, DocumentCount = documents.Count }
        };

        AddGroup(nodes, "未分组", null, null, documents.Where(document => document.GroupId is null).ToList());
        foreach (var group in groups)
        {
            AddGroup(
                nodes,
                group.Name,
                group.Id,
                group,
                documents.Where(document => document.GroupId == group.Id).ToList());
        }
        return nodes;
    }

    public static bool Contains(KnowledgeDocument document, string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return true;
        var directory = GetDirectoryPath(document.SourceRelativePath);
        var normalizedFolder = Normalize(folderPath);
        return directory.Equals(normalizedFolder, StringComparison.OrdinalIgnoreCase) ||
               directory.StartsWith(normalizedFolder + "/", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetDirectoryPath(string? sourceRelativePath)
    {
        var normalized = Normalize(sourceRelativePath);
        var separator = normalized.LastIndexOf('/');
        return separator <= 0 ? string.Empty : normalized[..separator];
    }

    private static void AddGroup(
        ICollection<GroupNode> nodes,
        string displayName,
        int? groupId,
        KnowledgeGroup? sourceGroup,
        IReadOnlyList<KnowledgeDocument> documents)
    {
        nodes.Add(new GroupNode
        {
            DisplayName = displayName,
            GroupId = groupId,
            SourceGroup = sourceGroup,
            DocumentCount = documents.Count
        });

        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            var directory = GetDirectoryPath(document.SourceRelativePath);
            if (directory.Length == 0) continue;
            var segments = directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var length = 1; length <= segments.Length; length++)
            {
                var ancestor = string.Join('/', segments.Take(length));
                paths.TryAdd(ancestor, ancestor);
            }
        }

        foreach (var folderPath in paths.Values.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            nodes.Add(new GroupNode
            {
                DisplayName = folderPath.Split('/')[^1],
                GroupId = groupId,
                FolderPath = folderPath,
                Depth = folderPath.Split('/').Length,
                DocumentCount = documents.Count(document => Contains(document, folderPath))
            });
        }
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return string.Join('/', path
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment is not "." and not ".."));
    }
}
