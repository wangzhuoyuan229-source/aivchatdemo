using ChatApp.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatApp.UI.ViewModels;

public partial class SelectableKnowledgeGroup : ObservableObject
{
    public KnowledgeGroup Group { get; }
    public int Id => Group.Id;
    public string Name => Group.Name;

    [ObservableProperty] private bool _isSelected;

    public SelectableKnowledgeGroup(KnowledgeGroup group, bool isSelected = false)
    {
        Group = group;
        _isSelected = isSelected;
    }
}
