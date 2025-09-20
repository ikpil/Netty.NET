using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace Netty.NET.Common;

internal static class NetUtilNetworkInterfacesLazyHolder
{
    internal static readonly IReadOnlyList<NetworkInterface> NETWORK_INTERFACES = NetUtilInitializations.networkInterfaces();
}