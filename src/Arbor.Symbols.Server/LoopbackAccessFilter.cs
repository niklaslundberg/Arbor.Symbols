using System.Net;

namespace Arbor.Symbols.Server;

public static class LoopbackAccessFilter
{
    /// <summary>
    /// Decides whether a request from <paramref name="remoteIp"/> may reach the loopback-only
    /// `/ui` endpoints. Fails closed: a <see langword="null"/> address (no remote-IP feature
    /// available — real Kestrel connections always populate it, but some hosting setups might
    /// not) is treated as not allowed rather than trusted by default.
    /// </summary>
    public static bool IsAllowed(IPAddress? remoteIp) => remoteIp is not null && IPAddress.IsLoopback(remoteIp);
}
