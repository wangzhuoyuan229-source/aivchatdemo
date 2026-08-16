using System.Diagnostics;
using ChatApp.Core.Models;
using ChatApp.Infrastructure.Data;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;

namespace ChatApp.UI.ViewModels;

public sealed partial class ChatAttachmentViewModel
{
    public ChatAttachmentViewModel(MessageAttachment attachment)
    {
        FileName = attachment.FileName;
        Title = string.IsNullOrWhiteSpace(attachment.Title) ? attachment.FileName : attachment.Title;
        Caption = attachment.Caption;
        ImagePath = string.IsNullOrWhiteSpace(attachment.StorageKey)
            ? string.Empty
            : AppPaths.ResolveMessageAttachmentStorageKey(attachment.StorageKey);
        if (!string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath))
        {
            try
            {
                using var stream = File.OpenRead(ImagePath);
                Preview = Bitmap.DecodeToWidth(stream, 360, BitmapInterpolationMode.MediumQuality);
            }
            catch
            {
                Preview = null;
            }
        }
    }

    public string FileName { get; }

    public string Title { get; }

    public string Caption { get; }

    public string ImagePath { get; }

    public Bitmap? Preview { get; }

    public bool IsAvailable => Preview is not null;

    [RelayCommand]
    private void Open()
    {
        if (!IsAvailable) return;
        try { Process.Start(new ProcessStartInfo(ImagePath) { UseShellExecute = true }); }
        catch { /* The placeholder/tooltip remains available if the OS viewer fails. */ }
    }
}
