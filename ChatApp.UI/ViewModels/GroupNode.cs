using ChatApp.Core.Models;

namespace ChatApp.UI.ViewModels;

/// <summary>
/// 知识库左侧分组树的虚拟节点。
/// GroupId == -1 表示「全部」；GroupId == null 表示「未分组」；其他表示具体分组。
/// </summary>
public class GroupNode
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>-1: 全部；null: 未分组；正数: 具体分组 Id</summary>
    public int? GroupId { get; set; }

    public KnowledgeGroup? SourceGroup { get; set; }

    public int DocumentCount { get; set; }
}
