namespace ChatApp.UI.Services;

public interface IUrlLauncher
{
    Task<bool> TryOpenAsync(string url, CancellationToken ct = default);
}
