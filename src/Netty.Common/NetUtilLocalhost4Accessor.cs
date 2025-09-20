using System.Net;

namespace Netty.NET.Common;

internal static class NetUtilLocalhost4Accessor
{
    public static IPAddress get()
    {
        // using https://en.wikipedia.org/wiki/Initialization-on-demand_holder_idiom
        return NetUtilLocalhost4LazyHolder.LOCALHOST4;
    }

    public static void set(IPAddress ignored)
    {
        // a no-op setter to avoid exceptions when NetUtil is initialized at run-time
    }
}