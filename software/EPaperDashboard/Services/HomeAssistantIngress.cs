using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services;

/// <summary>
/// Decides whether a request really came through Home Assistant ingress. The <c>X-Ingress-Path</c> header alone
/// proves nothing: only the Supervisor proxies ingress, from a fixed address on the internal network, and never
/// through the device port. Keep the Supervisor out of <c>ForwardedHeadersOptions.KnownProxies</c>, otherwise
/// <c>X-Forwarded-For</c> would replace the address checked here.
/// </summary>
public static partial class HomeAssistantIngress
{
    public static readonly IPAddress SupervisorAddress = IPAddress.Parse("172.30.32.2");

    public static bool IsTrusted(HttpContext context, int devicePort, bool allowLoopback)
    {
        if (!context.Request.Headers.ContainsKey(Constants.IngressPathHeader) || context.Connection.LocalPort == devicePort)
        {
            return false;
        }

        var remote = context.Connection.RemoteIpAddress;
        if (remote is null)
        {
            return false;
        }

        if (remote.IsIPv4MappedToIPv6)
        {
            remote = remote.MapToIPv4();
        }

        return remote.Equals(SupervisorAddress) || (allowLoopback && IPAddress.IsLoopback(remote));
    }

    /// <summary>Reads the ingress path, normalized to a leading and no trailing slash, if it has the Supervisor's format.</summary>
    public static bool TryGetPath(HttpContext context, [NotNullWhen(true)] out string? path)
    {
        path = null;
        var value = context.Request.Headers[Constants.IngressPathHeader].ToString().Trim();
        if (!value.StartsWith('/'))
        {
            value = "/" + value;
        }

        value = value.TrimEnd('/');
        if (!IngressPathPattern().IsMatch(value))
        {
            return false;
        }

        path = value;
        return true;
    }

    [GeneratedRegex("^/api/hassio_ingress/[A-Za-z0-9_-]+$")]
    private static partial Regex IngressPathPattern();
}
