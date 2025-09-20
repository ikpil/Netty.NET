using System.Net;

namespace Netty.NET.Common;

internal static class NetUtilLocalhost6LazyHolder
{
    internal static readonly IPAddress LOCALHOST6 = NetUtilInitializations.createLocalhost6();
}