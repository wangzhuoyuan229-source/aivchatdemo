using System.Net;

namespace ChatApp.Core.Security;

/// <summary>Maps failures to useful UI text without exposing provider payloads or credentials.</summary>
public static class UserFacingError
{
    public static string FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
                "鉴权失败，请检查 API Key 和服务权限。",
            HttpRequestException { StatusCode: HttpStatusCode.NotFound } =>
                "服务端点或模型不存在，请检查 API Base URL 和模型 ID。",
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } =>
                "请求过于频繁或额度不足，请稍后再试。",
            HttpRequestException { StatusCode: not null } http when (int)http.StatusCode.Value >= 500 =>
                "远程服务暂时不可用，请稍后再试。",
            HttpRequestException => "网络请求失败，请检查网络和服务端点。",
            TaskCanceledException => "请求超时，请检查网络后重试。",
            OperationCanceledException => "操作已取消。",
            _ => Sanitize(exception.Message)
        };
    }

    private static string Sanitize(string message)
    {
        var safe = SecretRedaction.Redact(message).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrWhiteSpace(safe)) return "操作未能完成，请重试。";
        return safe.Length <= 500 ? safe : safe[..500];
    }
}
