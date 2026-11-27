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

    // Reads a native int option as the value type of its ChannelOption: bool flags,
    // unsigned 32-bit thresholds exposed as long, and plain int values.
    internal static object GetOptionValue(Socket socket, ChannelOption option, int level, int name)
    {
        int raw = GetInt(socket, level, name);
        return option switch
        {
            ChannelOption<bool> => raw != 0,
            ChannelOption<long> => (long)unchecked((uint)raw),
            _ => raw
        };
    }

    internal static void SetOptionValue(Socket socket, int level, int name, object value)
    {
        int raw;
        if (value is long threshold)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(threshold);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(threshold, (long)uint.MaxValue);
            raw = unchecked((int)threshold);
        }
        else
            raw = value is bool enabled ? enabled ? 1 : 0 : (int)value;
        SetInt(socket, level, name, raw);
    }

    internal static IPEndPoint GetLocalEndpoint(Socket socket)
    {
        var template = new IPEndPoint(socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        var address = template.Serialize();
        byte[] bytes = new byte[address.Size];
        uint length = (uint)bytes.Length;
        if (GetSocketName(socket.SafeHandle, bytes, ref length) != 0)
            throw new SocketException();
        if (length != bytes.Length)
            throw new SocketException((int)SocketError.Fault);
        bytes.CopyTo(address.Buffer.Span);
        return (IPEndPoint)template.Create(address);
    }

    [DllImport("libc", EntryPoint = "getsockname", SetLastError = true)]
    private static extern int GetSocketName(SafeSocketHandle socket, [Out] byte[] address, ref uint length);
}
