using System.Net;

namespace Arbor.Symbols.Server;

public static class LoopbackAccessFilter
{
    /// <summary>
    /// Decides whether a request from <paramref name="remoteIp"/> may reach the loopback-only
    /// `/ui` endpoints. A <see langword="null"/> address (no remote-IP feature available, e.g.
    /// some test hosts) is allowed through, matching the connection-level trust the endpoints
    /// otherwise rely on.
    /// </summary>
    public static bool IsAllowed(IPAddress? remoteIp) => remoteIp is null || IPAddress.IsLoopback(remoteIp);
}
