using System.Net;

namespace Netty.NET.Common;

internal static class NetUtilLocalhostAccessor
{
    public static IPAddress Get()
    {
        // using https://en.wikipedia.org/wiki/Initialization-on-demand_holder_idiom
        return NetUtilLocalhostLazyHolder.LOCALHOST;
    }

    public static void Set(IPAddress ignored)
    {
        // a no-op setter to avoid exceptions when NetUtil is initialized at run-time
    }
}