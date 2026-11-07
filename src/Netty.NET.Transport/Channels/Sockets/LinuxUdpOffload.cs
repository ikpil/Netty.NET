using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels.Sockets;

internal static class LinuxUdpOffload
{
    // Linux UAPI values used by netty_epoll_native.c and netty_epoll_linuxsocket.c.
    private const int SolUdp = 17;
    private const int UdpSegment = 103;
    private const int UdpGro = 104;
    private const int DontWait = 0x40;
    private const int ControlTruncated = 0x08;
    private const int Interrupted = 4;
    private const int WouldBlock = 11;
    private const int InvalidArgument = 22;
    private const int ProtocolOptionUnavailable = 92;
    private const int OperationNotSupported = 95;
    private const int DeviceIoError = 5;
    private const int NotImplemented = 38;
    private const int AddressStorageSize = 32; // Enough for sockaddr_in and sockaddr_in6.
    private const int ControlStorageSize = 64;

    internal static void SetGro(Socket socket, bool enabled)
    {
        if (!OperatingSystem.IsLinux())
        {
            if (enabled) throw new PlatformNotSupportedException("UDP_GRO requires Linux.");
            return;
        }

        Span<byte> option = stackalloc byte[sizeof(int)];
        BitConverter.TryWriteBytes(option, enabled ? 1 : 0);
        socket.SetRawSocketOption(SolUdp, UdpGro, option);
    }

    internal static bool CanSend(Socket socket, int length, int segmentSize) =>
        OperatingSystem.IsLinux() && socket.IsBound && length <= 65507 && (length - 1) / segmentSize < 64;

    // Send per-message ancillary data so normal writes never inherit a segment size.
    internal static unsafe bool TrySend(Socket socket, ReadOnlyMemory<byte> payload, int segmentSize,
        IPEndPoint recipient)
    {
        if (recipient?.AddressFamily == AddressFamily.InterNetwork && socket.AddressFamily == AddressFamily.InterNetworkV6
            && socket.DualMode)
            recipient = new IPEndPoint(recipient.Address.MapToIPv6(), recipient.Port);
        var address = recipient?.Serialize();
        Span<byte> control = stackalloc byte[Aligned(sizeof(ControlHeader)) + Aligned(sizeof(ushort))];
        control.Clear();
        fixed (byte* controlPtr = control)
        fixed (byte* name = address == null ? Span<byte>.Empty : address.Buffer.Span[..address.Size])
        fixed (byte* data = payload.Span)
        {
            int headerLength = Aligned(sizeof(ControlHeader));
            *(ControlHeader*)controlPtr = new() { Length = (nuint)(headerLength + sizeof(ushort)), Level = SolUdp, Type = UdpSegment };
            *(ushort*)(controlPtr + headerLength) = (ushort)segmentSize;
            var vector = new IoVector { Data = data, Length = (nuint)payload.Length };
            var message = new MessageHeader
            {
                Name = name, NameLength = (uint)(address?.Size ?? 0), Vectors = &vector, VectorCount = 1,
                Control = controlPtr, ControlLength = (nuint)control.Length
            };
            while (true)
            {
                nint sent = SendMessage(socket.SafeHandle, &message, DontWait);
                if (sent == payload.Length) return true;
                if (sent >= 0) throw new IOException("The UDP aggregate was only partially accepted.");
                int error = Marshal.GetLastPInvokeError();
                if (error == Interrupted) continue;
                if (error is WouldBlock or DeviceIoError or InvalidArgument or ProtocolOptionUnavailable or OperationNotSupported)
                    return false;
                throw new SocketException();
            }
        }
    }

    internal readonly record struct Received(int Length, EndPoint Sender, int SegmentSize, int Offset = 0);

    internal readonly record struct ReadResult(Received Single, Received[] Batch = null, int BytesRead = 0);

    internal static async Task<ReadResult> ReceiveAsync(Socket socket, Memory<byte> payload, IPEndPoint template, int slotSize = 0)
    {
        byte[] readiness = null;
        int slots = slotSize == 0 ? 0 : Math.Min(payload.Length / slotSize, 64);
        while (true)
        {
            if (slots > 1)
            {
                if (TryReceiveBatch(socket, payload, template, slotSize, slots, out bool unsupported) is { } batch)
                    return batch;
                if (unsupported) slots = 0;
            }
            if (slots <= 1 && TryReceive(socket, payload, template) is { } received)
                return new ReadResult(received, BytesRead: received.Length);
            // The CLR's async socket poller owns the wait. Peek leaves the aggregate and its
            // control messages in the socket until recvmsg or recvmmsg can consume them.
            readiness ??= new byte[1];
            await socket.ReceiveAsync(readiness.AsMemory(), SocketFlags.Peek).ConfigureAwait(false);
        }
    }

    private static unsafe Received? TryReceive(Socket socket, Memory<byte> payload, IPEndPoint template)
    {
        var address = template.Serialize();
        Span<byte> control = stackalloc byte[ControlStorageSize];
        fixed (byte* name = address.Buffer.Span[..address.Size])
        fixed (byte* data = payload.Span)
        fixed (byte* controlPtr = control)
        {
            var vector = new IoVector { Data = data, Length = (nuint)payload.Length };
            var message = new MessageHeader
            {
                Name = name, NameLength = (uint)address.Size, Vectors = &vector, VectorCount = 1,
                Control = controlPtr, ControlLength = (nuint)control.Length
            };
            while (true)
            {
                nint read = ReceiveMessage(socket.SafeHandle, &message, DontWait);
                if (read >= 0)
                {
                    CheckControl(message.Flags);
                    return new Received((int)read, template.Create(address), ReadSegmentSize(control[..(int)message.ControlLength]));
                }
                int error = Marshal.GetLastPInvokeError();
                if (error == Interrupted) continue;
                if (error == WouldBlock) return null;
                throw new SocketException();
            }
        }
    }

    private static unsafe ReadResult? TryReceiveBatch(Socket socket, Memory<byte> payload, IPEndPoint template,
        int slotSize, int slots, out bool unsupported)
    {
        unsupported = false;
        var messages = stackalloc MultiMessageHeader[slots];
        var vectors = stackalloc IoVector[slots];
        var names = stackalloc byte[slots * AddressStorageSize];
        var controls = stackalloc byte[slots * ControlStorageSize];
        fixed (byte* data = payload.Span)
        {
            for (int i = 0; i < slots; i++)
            {
                vectors[i] = new IoVector { Data = data + i * slotSize, Length = (nuint)slotSize };
                messages[i] = new MultiMessageHeader
                {
                    Message = new MessageHeader
                    {
                        Name = names + i * AddressStorageSize, NameLength = AddressStorageSize,
                        Vectors = &vectors[i], VectorCount = 1,
                        Control = controls + i * ControlStorageSize, ControlLength = ControlStorageSize
                    }
                };
            }
            int count;
            while (true)
            {
                try { count = ReceiveMessages(socket.SafeHandle, messages, (uint)slots, DontWait, IntPtr.Zero); }
                catch (EntryPointNotFoundException) { unsupported = true; return null; }
                if (count > 0) break;
                if (count == 0) return null;
                int error = Marshal.GetLastPInvokeError();
                if (error == Interrupted) continue;
                if (error == WouldBlock) return null;
                if (error == NotImplemented) { unsupported = true; return null; }
                throw new SocketException();
            }

            var packets = new Received[count];
            for (int i = 0; i < count; i++)
            {
                var message = messages[i].Message;
                CheckControl(message.Flags);
                var address = template.Serialize();
                new ReadOnlySpan<byte>(message.Name, (int)message.NameLength).CopyTo(address.Buffer.Span);
                int segmentSize = ReadSegmentSize(new ReadOnlySpan<byte>(message.Control, (int)message.ControlLength));
                packets[i] = new Received((int)messages[i].Length, template.Create(address), segmentSize, i * slotSize);
            }
            // As in the source scattering path, charge a full slot for each received message.
            return new ReadResult(default, packets, count * slotSize);
        }
    }

    private static void CheckControl(int flags)
    {
        if ((flags & ControlTruncated) != 0)
            throw new IOException("UDP receive control information was truncated.");
    }

    private static unsafe int ReadSegmentSize(ReadOnlySpan<byte> control)
    {
        int headerLength = Aligned(sizeof(ControlHeader));
        while (control.Length >= headerLength)
        {
            var header = MemoryMarshal.Read<ControlHeader>(control);
            if (header.Length < (nuint)headerLength || header.Length > (nuint)control.Length) break;
            int length = (int)header.Length;
            if (header.Level == SolUdp && header.Type == UdpGro && length >= headerLength + sizeof(int))
                return BitConverter.ToInt32(control[headerLength..]); // The kernel reports the segment size as an int
            int next = Aligned(length);
            if (next > control.Length) break;
            control = control[next..];
        }
        return 0;
    }

    private static int Aligned(int size) => (size + IntPtr.Size - 1) & -IntPtr.Size;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct IoVector { internal byte* Data; internal nuint Length; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ControlHeader { internal nuint Length; internal int Level; internal int Type; }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct MessageHeader
    {
        internal byte* Name;
        internal uint NameLength;
        internal IoVector* Vectors;
        internal nuint VectorCount;
        internal byte* Control;
        internal nuint ControlLength;
        internal int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MultiMessageHeader { internal MessageHeader Message; internal uint Length; }

    [DllImport("libc", EntryPoint = "sendmsg", SetLastError = true)]
    private static extern unsafe nint SendMessage(SafeSocketHandle socket, MessageHeader* message, int flags);

    [DllImport("libc", EntryPoint = "recvmsg", SetLastError = true)]
    private static extern unsafe nint ReceiveMessage(SafeSocketHandle socket, MessageHeader* message, int flags);

    [DllImport("libc", EntryPoint = "recvmmsg", SetLastError = true)]
    private static extern unsafe int ReceiveMessages(SafeSocketHandle socket, MultiMessageHeader* messages,
        uint count, int flags, IntPtr timeout);
}
