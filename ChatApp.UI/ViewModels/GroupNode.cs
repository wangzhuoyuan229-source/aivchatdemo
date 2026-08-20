using ChatApp.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatApp.UI.ViewModels;

/// <summary>
/// 知识库左侧分组树的虚拟节点。
/// GroupId == -1 表示「全部」；GroupId == null 表示「未分组」；其他表示具体分组。
/// 支持折叠/展开：IsExpanded 控制子节点可见性，HasChildren 表示是否可折叠。
/// </summary>
public partial class GroupNode : ObservableObject
{
    [ObservableProperty] private string _displayName = string.Empty;

    /// <summary>-1: 全部；null: 未分组；正数: 具体分组 Id</summary>
    [ObservableProperty] private int? _groupId;

    [ObservableProperty] private KnowledgeGroup? _sourceGroup;

    [ObservableProperty] private int _documentCount;

    /// <summary>当前分组内的虚拟目录路径；空字符串表示分组根节点。</summary>
    [ObservableProperty] private string _folderPath = string.Empty;

    [ObservableProperty] private int _depth;

    /// <summary>是否展开子节点。折叠时所有后代隐藏。</summary>
    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>是否有子文件夹/子分组。</summary>
    [ObservableProperty] private bool _hasChildren;

    /// <summary>是否在列表中可见。由祖先的 IsExpanded 决定。</summary>
    [ObservableProperty] private bool _isVisible = true;

    public bool IsFolder => !string.IsNullOrWhiteSpace(FolderPath);

    public string Icon => IsFolder ? "📁" : "🗂";

    public double IndentWidth => Depth * 14d;

    public string SelectionKey => $"{GroupId?.ToString() ?? "ungrouped"}|{FolderPath}";

    /// <summary>折叠按钮字形：展开显示 ▾，折叠显示 ▸。</summary>
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));
    partial void OnDepthChanged(int value) => OnPropertyChanged(nameof(IndentWidth));
    partial void OnFolderPathChanged(string value)
    {
        OnPropertyChanged(nameof(IsFolder));
        OnPropertyChanged(nameof(Icon));
    }
}
