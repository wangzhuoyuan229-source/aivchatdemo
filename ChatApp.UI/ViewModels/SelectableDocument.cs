using ChatApp.Core.Models;
using ChatApp.Infrastructure.Data;
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
    public string RelativePath => string.IsNullOrWhiteSpace(Document.SourceRelativePath)
        ? Document.FileName
        : Document.SourceRelativePath;
    public string FileType => Document.FileType;
    public long CharCount => Document.CharCount;
    public int ChunkCount => Document.ChunkCount;
    public int? GroupId => Document.GroupId;
    public DateTime ImportedAt => Document.ImportedAt;
    public bool IsImage => Document.Kind == KnowledgeItemKind.Image;
    public string ItemIcon => IsImage ? "🖼️" : "📄";
    public string Description => Document.SemanticDescription;
    public string Tags => Document.Tags;
    public string DescriptionSourceText => Document.DescriptionSource switch
    {
        ImageDescriptionSource.VisionModel => "多模态识图",
        ImageDescriptionSource.MetadataFallback => "文件名/目录回退",
        ImageDescriptionSource.Manual => "手动编辑",
        _ => string.Empty
    };
    public string ProviderModelText
    {
        get
        {
            var value = string.Join(" / ", new[] { Document.DescriptionProvider, Document.DescriptionModel }
                .Where(item => !string.IsNullOrWhiteSpace(item)));
            return string.IsNullOrWhiteSpace(value) && IsImage ? "未调用多模态服务" : value;
        }
    }
    public string PreviewPath => IsImage && !string.IsNullOrWhiteSpace(Document.StorageKey)
        ? AppPaths.ResolveKnowledgeStorageKey(Document.StorageKey)
        : string.Empty;

    public SelectableDocument(KnowledgeDocument doc)
    {
        Document = doc;
    }
}
