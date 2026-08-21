using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ChatApp.UI.ViewModels;

namespace ChatApp.UI.Views;

public partial class CreateGroupChatWindow : Window
{
    public CreateGroupChatWindow() => AvaloniaXamlLoader.Load(this);

    private async void SelectGroupAvatar_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CreateGroupChatViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择群聊头像",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/webp"]
                }
            ]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenReadAsync();
            if (stream.Length > 5 * 1024 * 1024)
            {
                vm.ErrorText = "群聊头像不能超过 5 MB。";
                return;
            }
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            var mime = extension switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            vm.Avatar = $"data:{mime};base64,{Convert.ToBase64String(memory.ToArray())}";
            vm.ErrorText = string.Empty;
        }
        catch
        {
            vm.ErrorText = "无法读取所选头像，请选择其他图片。";
        }
    }
}
