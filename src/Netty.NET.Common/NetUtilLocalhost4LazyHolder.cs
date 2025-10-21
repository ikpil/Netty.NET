using System.Net;

namespace Netty.NET.Common;

internal static class NetUtilLocalhost4LazyHolder
{
    public static readonly IPAddress LOCALHOST4 = NetUtilInitializations.createLocalhost4();
}