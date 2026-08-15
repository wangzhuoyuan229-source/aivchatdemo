using System.Net;
using System.Net.Sockets;

namespace ChatApp.Core.Settings;

/// <summary>
/// Enforces the product policy that model traffic must go to a hosted HTTPS API.
/// Loopback, private-network and local-development endpoints are deliberately rejected.
/// </summary>
public static class RemoteApiEndpointPolicy
{
    public const string DefaultBaseUrl = "https://api.deepseek.com/v1";

    public static Uri NormalizeOrThrow(string? baseUrl, string fieldName = "API Base URL")
    {
        if (TryNormalize(baseUrl, out var endpoint, out var error))
            return endpoint;

        throw new InvalidOperationException($"{fieldName} 无效：{error}");
    }

    public static bool TryNormalize(string? baseUrl, out Uri endpoint, out string error)
    {
        endpoint = new Uri(DefaultBaseUrl);
        error = string.Empty;

        var value = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (value.Length == 0)
        {
            error = "不能为空。";
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            error = "请输入完整的 HTTPS 地址。";
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "仅允许远程 HTTPS API，不支持本地模型或 HTTP 端点。";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            error = "地址中不能包含账号、查询参数或片段。";
            return false;
        }

        if (IsLocalOrPrivateHost(parsed.Host))
        {
            error = "不支持 localhost、回环地址或私有网络中的本地模型服务。";
            return false;
        }

        var path = parsed.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            path += "/v1";

        var builder = new UriBuilder(parsed) { Path = path };
        endpoint = builder.Uri;
        return true;
    }

    private static bool IsLocalOrPrivateHost(string host)
    {
        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized is "localhost" or "0.0.0.0" ||
            normalized.EndsWith(".localhost", StringComparison.Ordinal) ||
            normalized.EndsWith(".local", StringComparison.Ordinal))
            return true;

        if (!IPAddress.TryParse(normalized, out var address))
            return false;

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None))
            return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
                   (bytes[0] & 0xfe) == 0xfc;
        }

        return false;
    }
}
