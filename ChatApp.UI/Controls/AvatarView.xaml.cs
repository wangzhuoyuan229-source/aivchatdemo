using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ChatApp.UI.Controls;

public partial class AvatarView : UserControl
{
    public static readonly StyledProperty<string> AvatarProperty =
        AvaloniaProperty.Register<AvatarView, string>(nameof(Avatar), "🎭");

    public string Avatar
    {
        get => GetValue(AvatarProperty);
        set => SetValue(AvatarProperty, value);
    }

    public AvatarView() => AvaloniaXamlLoader.Load(this);
}
