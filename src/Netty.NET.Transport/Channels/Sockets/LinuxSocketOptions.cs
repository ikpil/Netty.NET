using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Netty.NET.Transport.Channels.Sockets;

internal static class LinuxSocketOptions
{
    // Linux UAPI values from asm-generic/socket.h, linux/in.h and linux/tcp.h.
    internal const int SolSocket = 1;
    internal const int SoReusePort = 15;
    internal const int IpFreeBind = 15;
    internal const int IpBindAddressNoPort = 24;
    internal const int TcpCork = 3;
    internal const int TcpMaxSegment = 2;
    internal const int TcpDeferAccept = 9;
    internal const int TcpQuickAck = 12;
    internal const int TcpFastOpen = 23;
    internal const int TcpFastOpenConnect = 30;
    internal const int TcpNotSentLowAt = 25;

    internal static int GetInt(Socket socket, int level, int option)
    {
        Span<byte> value = stackalloc byte[sizeof(int)];
        int length = socket.GetRawSocketOption(level, option, value);
        return BitConverter.ToInt32(value[..length]);
    }

    internal static void SetInt(Socket socket, int level, int option, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BitConverter.TryWriteBytes(bytes, value);
        socket.SetRawSocketOption(level, option, bytes);
    }

    internal static bool GetFlag(Socket socket, int level, int option) => GetInt(socket, level, option) != 0;

    internal static void SetFlag(Socket socket, int level, int option, bool enabled) =>
        SetInt(socket, level, option, enabled ? 1 : 0);

    internal static unsafe IPEndPoint GetLocalEndpoint(Socket socket)
    {
        var template = new IPEndPoint(socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        var address = template.Serialize();
        uint length = (uint)address.Size;
        fixed (byte* bytes = address.Buffer.Span[..address.Size])
        {
            if (GetSocketName(socket.SafeHandle, bytes, ref length) != 0)
                throw new SocketException();
        }
        return (IPEndPoint)template.Create(address);
    }

    [DllImport("libc", EntryPoint = "getsockname", SetLastError = true)]
    private static extern unsafe int GetSocketName(SafeSocketHandle socket, byte* address, ref uint length);
}
