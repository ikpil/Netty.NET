using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace Netty.NET.Common;

internal static class NetUtilNetworkInterfacesAccessor
{
    public static IReadOnlyList<NetworkInterface> get()
    {
        // using https://en.wikipedia.org/wiki/Initialization-on-demand_holder_idiom
        return NetUtilNetworkInterfacesLazyHolder.NETWORK_INTERFACES;
    }

    public static void set(ICollection<NetworkInterface> ignored)
    {
        // a no-op setter to avoid exceptions when NetUtil is initialized at run-time
    }
}