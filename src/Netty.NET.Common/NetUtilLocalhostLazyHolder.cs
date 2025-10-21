using System.Net;

namespace Netty.NET.Common;

internal static class NetUtilLocalhostLazyHolder
{
    internal static readonly IPAddress LOCALHOST = NetUtilInitializations
        .determineLoopback(
            NetUtilNetworkInterfacesLazyHolder.NETWORK_INTERFACES,
            NetUtilLocalhost4LazyHolder.LOCALHOST4,
            NetUtilLocalhost6LazyHolder.LOCALHOST6
        )
        .Address;
}