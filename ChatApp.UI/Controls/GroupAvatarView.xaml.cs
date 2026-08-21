using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ChatApp.UI.Controls;

/// <summary>Shows a custom group avatar or a compact collage of up to four member avatars.</summary>
public partial class GroupAvatarView : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<string>?> AvatarsProperty =
        AvaloniaProperty.Register<GroupAvatarView, IReadOnlyList<string>?>(nameof(Avatars));

    public static readonly StyledProperty<string?> CustomAvatarProperty =
        AvaloniaProperty.Register<GroupAvatarView, string?>(nameof(CustomAvatar));

    public IReadOnlyList<string>? Avatars
    {
        get => GetValue(AvatarsProperty);
        set => SetValue(AvatarsProperty, value);
    }

    public string? CustomAvatar
    {
        get => GetValue(CustomAvatarProperty);
        set => SetValue(CustomAvatarProperty, value);
    }

    public GroupAvatarView()
    {
        AvaloniaXamlLoader.Load(this);
        Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AvatarsProperty || change.Property == CustomAvatarProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        if (this.FindControl<Grid>("AvatarGrid") is not { } grid) return;
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();

        if (!string.IsNullOrWhiteSpace(CustomAvatar))
        {
            grid.Children.Add(new AvatarView { Avatar = CustomAvatar });
            return;
        }

        var avatars = Avatars?.Where(a => !string.IsNullOrWhiteSpace(a)).Take(4).ToList() ?? [];
        if (avatars.Count == 0)
        {
            grid.Children.Add(new TextBlock
            {
                Text = "👥",
                FontSize = 20,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
            return;
        }

        var columns = avatars.Count == 1 ? 1 : 2;
        var rows = avatars.Count <= 2 ? 1 : 2;
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        for (var i = 0; i < avatars.Count; i++)
        {
            var frame = new Border
            {
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                ClipToBounds = true,
                Child = new AvatarView { Avatar = avatars[i] }
            };
            Grid.SetColumn(frame, i % columns);
            Grid.SetRow(frame, i / columns);
            grid.Children.Add(frame);
        }
    }
}
