using System.Net;

namespace KynticAI.Scout.Infrastructure.Configuration;

public static class CorsOriginValidator
{
    public static bool TryValidate(string? origin, bool hostedMode, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(origin))
        {
            error = "origin is empty";
            return false;
        }

        var candidate = origin.Trim();
        if (candidate == "*" || candidate.Contains('*', StringComparison.Ordinal))
        {
            error = "wildcards are not allowed";
            return false;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "origin must be an absolute HTTP(S) origin";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "origin must not contain user information";
            return false;
        }

        if (uri.AbsolutePath is not "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "origin must not contain a path, query, or fragment";
            return false;
        }

        if (hostedMode && uri.Scheme == Uri.UriSchemeHttp && !IsLoopbackHost(uri.Host))
        {
            error = "production origins must use HTTPS unless they are loopback development origins";
            return false;
        }

        return true;
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
