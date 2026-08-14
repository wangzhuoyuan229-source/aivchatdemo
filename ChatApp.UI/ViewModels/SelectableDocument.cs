using ChatApp.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatApp.UI.ViewModels;

/// <summary>
/// 知识库文档的可选中包装项，用于批量管理。
/// 注意：所有透传属性均为只读，UI 绑定时必须使用 Mode=OneWay 或 TextBlock（不要用 Run.Text）。
/// </summary>
public class SelectableDocument : ObservableObject
{
    public KnowledgeDocument Document { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    // 透传属性，便于 XAML 直接绑定
    public int Id => Document.Id;
    public string Title => Document.Title;
    public string FileName => Document.FileName;
    public string FileType => Document.FileType;
    public long CharCount => Document.CharCount;
    public int ChunkCount => Document.ChunkCount;
    public int? GroupId => Document.GroupId;
    public DateTime ImportedAt => Document.ImportedAt;

    public SelectableDocument(KnowledgeDocument doc)
    {
        Document = doc;
    }
}
