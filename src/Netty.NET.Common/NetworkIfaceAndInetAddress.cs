using System.Net;
using System.Net.NetworkInformation;

namespace Netty.NET.Common;

public class NetworkIfaceAndInetAddress
{
    public readonly NetworkInterface Iface;
    public readonly IPAddress Address;

    public NetworkIfaceAndInetAddress(NetworkInterface iface, IPAddress address)
    {
        Iface = iface;
        Address = address;
    }
}