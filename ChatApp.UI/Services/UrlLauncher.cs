using System.Diagnostics;

namespace ChatApp.UI.Services;

public sealed class UrlLauncher : IUrlLauncher
{
    public async Task<bool> TryOpenAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            // Avalonia Launcher is preferred in UI thread; fallback to Process.Start for headless/tests
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
            await Task.CompletedTask;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
