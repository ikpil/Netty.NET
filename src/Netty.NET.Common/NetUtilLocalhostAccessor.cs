using System.Net;

namespace Netty.NET.Common;

internal static class NetUtilLocalhostAccessor
{
    public static IPAddress get()
    {
        // using https://en.wikipedia.org/wiki/Initialization-on-demand_holder_idiom
        return NetUtilLocalhostLazyHolder.LOCALHOST;
    }

    public static void set(IPAddress ignored)
    {
        // a no-op setter to avoid exceptions when NetUtil is initialized at run-time
    }
}