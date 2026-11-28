/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Channels.Sockets;

/**
 * An NIO datagram {@link Channel} that sends and receives an
 * {@link AddressedEnvelope AddressedEnvelope<ByteBuf, SocketAddress>}.
 *
 * @see AddressedEnvelope
 * @see DatagramPacket
 */
// CLR: a non-blocking System.Net.Sockets.Socket (SocketType.Dgram, ProtocolType.Udp) replaces the java.nio
// DatagramChannel; receive/send are non-blocking ReceiveFrom/SendTo calls on OP_READ/OP_WRITE readiness.
// https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.receivefrom
public class NioDatagramChannel : AbstractNioMessageChannel, IDatagramChannel
{
    private readonly bool _linuxDualMode;
    private EndPoint _connectedAddress;
    private IPEndPoint _linuxLocalAddress;
    private IPEndPoint _linuxBoundAddress;
    private readonly Dictionary<(IPAddress Group, long Index, IPAddress Source), Membership> _memberships = new();
    private sealed class Membership(IPAddress address)
    {
        internal readonly IPAddress Address = address;
        internal readonly HashSet<IPAddress> Blocked = new();
    }
    internal bool LinuxDualMode => _linuxDualMode;
    private static readonly ChannelMetadata METADATA = new ChannelMetadata(true, 16);
    /**
     * Create a new instance which will use the Operation Systems default {@link SocketProtocolFamily}.
     */
    // CLR: DatagramChannel.open() uses INET6 when IPv6 is available (OpenJDK DatagramChannelImpl:
    // Net.isIPv6Available() ? INET6 : INET) and clears IPV6_V6ONLY; a CLR Socket has no default family, so this opens
    // the same: an IPv6 socket with Socket.DualMode when IPv4 is also available, otherwise IPv4
    // (https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.dualmode).
    public NioDatagramChannel() : this(DefaultFamily(), Socket.OSSupportsIPv6 && Socket.OSSupportsIPv4)
    {
    }

    private static AddressFamily DefaultFamily()
    {
        return Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
    }

    public NioDatagramChannel(AddressFamily family) : this(family, false)
    {
    }

    /**
     * Create a new instance from the given {@link DatagramChannel}.
     */
    public NioDatagramChannel(Socket socket) : base(null, ValidateDatagramSocket(socket), NioIoOps.READ, METADATA)
    {
        // CLR: an IPv6 socket in dual mode carries IPv4 traffic; the Linux dual-mode membership path needs to know.
        _linuxDualMode = OperatingSystem.IsLinux() && socket.AddressFamily == AddressFamily.InterNetworkV6 && socket.DualMode;
    }

    private static Socket ValidateDatagramSocket(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        // CLR: the Java parameter type DatagramChannel is a run-time check on the socket type.
        if (socket.SocketType != SocketType.Dgram)
        {
            throw new ArgumentException("A datagram socket is required.", nameof(socket));
        }

        return socket;
    }

    /**
     * Create a new instance using the given {@link InternetProtocolFamily}. If {@code null} is used it will depend
     * on the Operation Systems default which will be chosen.
     *
     * @deprecated use {@link NioDatagramChannel#NioDatagramChannel(SocketProtocolFamily)}
     */
    [Obsolete("use DatagramChannel(SocketProtocolFamily?)")]
    public NioDatagramChannel(InternetProtocolFamily? ipFamily)
        : this(ipFamily == null ? null : ipFamily.Value.ToSocketProtocolFamily())
    {
    }

    /**
     * Create a new instance using the given {@link SocketProtocolFamily}. If {@code null} is used it will depend
     * on the Operation Systems default which will be chosen.
     */
    // CLR: null opens the OS default family as newSocket(provider) does (see the parameterless constructor). INET opens
    // IPv4; INET6 opens IPv6 with IPv4-mapped traffic when IPv4 is available, as OpenJDK Net.socket0 clears
    // IPV6_V6ONLY.
    public NioDatagramChannel(SocketProtocolFamily? protocolFamily)
        : this(NewSocketFamily(protocolFamily), NewSocketFamily(protocolFamily) == AddressFamily.InterNetworkV6 && Socket.OSSupportsIPv4)
    {
    }

    private static AddressFamily NewSocketFamily(SocketProtocolFamily? ipFamily)
    {
        if (ipFamily == null)
        {
            return DefaultFamily();
        }

        AddressFamily family = ipFamily.Value.ToJdkFamily();
        // CLR: DatagramChannel.open(family) throws UnsupportedOperationException for an unavailable family;
        // its API note states that Unix domain sockets are not supported.
        if (family == AddressFamily.Unix || family == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
        {
            throw new NotSupportedException("ProtocolFamily is not supported: " + ipFamily);
        }

        return family;
    }

    /// <summary>Creates a UDP channel with optional IPv4-mapped traffic on an IPv6 socket.</summary>
    public NioDatagramChannel(AddressFamily family, bool dualMode) : base(null, NewSocket(family, dualMode), NioIoOps.READ, METADATA)
    {
        _linuxDualMode = OperatingSystem.IsLinux() && dualMode;

        try
        {
            if (dualMode)
            {
                JavaChannel().DualMode = true;
            }

            if (OperatingSystem.IsLinux())
            {
                if (Socket.OSSupportsIPv4 && (family == AddressFamily.InterNetwork || dualMode))
                    DisableMulticastAll(SocketOptionLevel.IP, 49); // IP_MULTICAST_ALL

                if (family == AddressFamily.InterNetworkV6)
                    DisableMulticastAll(SocketOptionLevel.IPv6, 29); // IPV6_MULTICAST_ALL
            }
        }
        catch
        {
            JavaChannel().Dispose();
            throw;
        }
    }

    private static Socket NewSocket(AddressFamily family, bool dualMode)
    {
        if (dualMode && family != AddressFamily.InterNetworkV6)
            throw new ArgumentException("Dual mode requires an IPv6 socket.", nameof(family));

        try
        {
            return new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        }
        catch (SocketException e)
        {
            throw new ChannelException("Failed to open a socket.", e);
        }
    }

    private void DisableMulticastAll(SocketOptionLevel level, int option)
    {
        // As in OpenJDK, receive only memberships held by this socket. Older kernels may lack the option.
        // https://github.com/openjdk/jdk/blob/master/src/java.base/unix/native/libnio/ch/Net.c
        Span<byte> disabled = stackalloc byte[sizeof(int)];
        disabled.Clear();

        try
        {
            JavaChannel().SetRawSocketOption((int)level, option, disabled);
        }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.ProtocolOption)
        {
        }
    }

    protected override EndPoint LocalAddress0()
    {
        return Normalize(Volatile.Read(ref _linuxLocalAddress) ?? JavaChannel().LocalEndPoint);
    }

    protected override EndPoint RemoteAddress0()
    {
        return Volatile.Read(ref _connectedAddress);
    }

    protected override IChannelConfig NewConfig()
    {
        return new DefaultDatagramChannelConfig(this, JavaChannel());
    }

    protected override Task DoBindAsync(EndPoint address, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (JavaChannel().AddressFamily == AddressFamily.InterNetworkV6 && address is IPEndPoint ip && ip.Address.Equals(IPAddress.Any))
            address = new IPEndPoint(IPAddress.IPv6Any, ip.Port);

        JavaChannel().Bind(address);
        SetActive(true);
        return Task.CompletedTask;
    }

    protected override bool DoConnect(EndPoint remoteAddress, EndPoint localAddress)
    {
        if (localAddress != null)
        {
            JavaChannel().Bind(localAddress);
        }

        bool success = false;

        try
        {
            var bound = OperatingSystem.IsLinux() ? _linuxBoundAddress ?? (IPEndPoint)JavaChannel().LocalEndPoint : null;
            JavaChannel().Connect(remoteAddress);

            if (OperatingSystem.IsLinux())
            {
                var connected = LinuxSocketOptions.GetLocalEndpoint(JavaChannel());
                _linuxBoundAddress = bound ?? new IPEndPoint(AnyAddress, connected.Port);
                Volatile.Write(ref _linuxLocalAddress, connected);
            }

            Volatile.Write(ref _connectedAddress, Normalize(remoteAddress));
            SetActive(true);
            success = true;
            return true;
        }
        finally
        {
            if (!success)
            {
                DoClose();
            }
        }
    }

    protected override void DoFinishConnect()
    {
        throw new InvalidOperationException();
    }

    protected override Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsConnected)
        {
            // Winsock DisconnectEx is for connection-oriented sockets. Connecting a datagram socket
            // to a zero address removes its default peer without replacing the bound socket.
            // https://learn.microsoft.com/windows/win32/api/winsock2/nf-winsock2-connect
            if (OperatingSystem.IsWindows())
                JavaChannel().Connect(new IPEndPoint(AnyAddress, 0));
            else if (OperatingSystem.IsLinux())
                DisconnectLinux();
            else
                JavaChannel().Disconnect(true);

            Volatile.Write(ref _connectedAddress, null);
            SetActive(JavaChannel().IsBound || ActiveOnRegistration);
        }

        return Task.CompletedTask;
    }

    private IPAddress AnyAddress => JavaChannel().AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;

    private void DisconnectLinux()
    {
        // AF_UNSPEC detaches the UDP peer while keeping the descriptor, its socket options and pending receives.
        // Linux may release an ephemeral port. Restore the original binding just as the JDK channel does.
        // https://github.com/openjdk/jdk/blob/master/src/java.base/share/classes/sun/nio/ch/DatagramChannelImpl.java
        byte[] unspecified = new byte[16]; // struct sockaddr with sa_family = AF_UNSPEC (0)
        if (ConnectNative(JavaChannel().SafeHandle, unspecified, (uint)unspecified.Length) != 0)
            throw new SocketException();

        try
        {
            var local = LinuxSocketOptions.GetLocalEndpoint(JavaChannel());
            if (local.Port == 0)
            {
                JavaChannel().Bind(_linuxBoundAddress);
                local = LinuxSocketOptions.GetLocalEndpoint(JavaChannel());
            }

            Volatile.Write(ref _linuxLocalAddress, local);
        }
        catch
        {
            // The peer is already detached. Close if we cannot restore the promised binding.
            _ = Close();
            throw;
        }
    }

    [DllImport("libc", EntryPoint = "connect", SetLastError = true)]
    private static extern int ConnectNative(SafeSocketHandle socket, byte[] address, uint length);

    public bool IsConnected => RemoteAddress != null;
#pragma warning disable CS0612, CS0618 // Preserve the original optional active-on-registration setting.
    private bool ActiveOnRegistration =>
        Config.TryGetOption(ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, out bool active) && active;
#pragma warning restore CS0612, CS0618
    protected override void DoRegister()
    {
        base.DoRegister();
        SetActive(JavaChannel().IsBound || ActiveOnRegistration);
    }

    internal void SetUdpGro(bool enabled)
    {
        if (enabled)
        {
            throw new PlatformNotSupportedException("UDP_GRO is unavailable on the managed transport.");
        }
    }

    internal void SetUdpSlotSize(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (size != 0)
        {
            throw new PlatformNotSupportedException("UDP scattering is unavailable on the managed transport.");
        }
    }

    /**
     * If {@code true} is used <a href="https://man7.org/linux/man-pages/man7/ip.7.html">IP_RECVORIGDSTADDR</a> is
     * enabled, {@code false} for disable it. Default is disabled.
     */
    internal void SetReceiveOriginalDestination(bool enabled)
    {
        if (enabled)
        {
            throw new PlatformNotSupportedException("IP_RECVORIGDSTADDR is unavailable on the managed transport.");
        }
    }

    protected override void DoDeregister()
    {
        base.DoDeregister();
        SetActive(JavaChannel().IsBound);
    }

    protected override void DoClose()
    {
        base.DoClose();
        _memberships.Clear();
        JavaChannel().Dispose();
    }
    protected override bool ContinueOnWriteError
    {
        get
        {
            // Continue on write error as a DatagramChannel can write to multiple remote peers
            //
            // See https://github.com/netty/netty/issues/2665
            return true;
        }
    }
    private static readonly string EXPECTED_TYPES =
            " (expected: " + StringUtil.SimpleClassName(typeof(DatagramPacket)) + ", " +
            StringUtil.SimpleClassName(typeof(IAddressedEnvelope<,>)) + '<' +
            StringUtil.SimpleClassName(typeof(ByteBuf)) + ", " +
            StringUtil.SimpleClassName(typeof(EndPoint)) + ">, " +
            StringUtil.SimpleClassName(typeof(ByteBuf)) + ')';

    private static void CheckUnresolved(IAddressedEnvelope<object, EndPoint> envelope)
    {
        // CLR: an unresolved InetSocketAddress is a DnsEndPoint here; SocketError.HostNotFound stands for
        // UnresolvedAddressException (https://learn.microsoft.com/dotnet/api/system.net.dnsendpoint).
        if (envelope.Recipient is DnsEndPoint)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }
    }

    protected override object FilterOutboundMessage(object msg)
    {
        if (msg is DatagramPacket p)
        {
            CheckUnresolved(p);
            ByteBuf content = p.Content;
            // CLR: preserve the segment size for the managed datagram send loop.
            if (IsSingleDirectBuffer(content) || p is SegmentedDatagramPacket)
            {
                return p;
            }

            return new DatagramPacket(NewDirectBuffer(p, content), p.Recipient);
        }

        if (msg is ByteBuf buf)
        {
            if (IsSingleDirectBuffer(buf))
            {
                return buf;
            }

            return NewDirectBuffer(buf);
        }

        if (msg is IAddressedEnvelope<object, EndPoint> e)
        {
            CheckUnresolved(e);

            if (e.Content is ByteBuf content)
            {
                if (IsSingleDirectBuffer(content))
                {
                    return e;
                }

                return new DefaultAddressedEnvelope<ByteBuf, EndPoint>(NewDirectBuffer(e, content), e.Recipient);
            }
        }

        throw new NotSupportedException(
                "unsupported message type: " + StringUtil.SimpleClassName(msg) + EXPECTED_TYPES);
    }

    /**
     * Checks if the specified buffer is a direct buffer and is composed of a single NIO buffer.
     * (We check this because otherwise we need to make it a non-composite buffer.)
     */
    private static bool IsSingleDirectBuffer(ByteBuf buf)
    {
        // CLR: nioBufferCount() == 1 is a single-segment readable sequence (ByteBuf.ReadableSequence).
        return buf.IsDirect && buf.ReadableSequence.IsSingleSegment;
    }

    protected override bool DoWriteMessage(object msg, ChannelOutboundBuffer @in)
    {
        EndPoint remoteAddress;
        ByteBuf data;

        if (msg is IAddressedEnvelope<ByteBuf, EndPoint> envelope)
        {
            remoteAddress = envelope.Recipient;
            data = envelope.Content;
        }
        else
        {
            data = (ByteBuf)msg;
            remoteAddress = null;
        }

        int dataLen = data.ReadableBytes;
        if (dataLen == 0)
        {
            return true;
        }
        // CLR: DatagramChannel.write throws NotYetConnectedException; SocketError.NotConnected stands for it.
        if (remoteAddress == null && !IsConnected)
        {
            throw new SocketException((int)SocketError.NotConnected);
        }

        ReadOnlySequence<byte> nioData = data.ReadableSequence;

        if (msg is SegmentedDatagramPacket segmented && dataLen > segmented.SegmentSize)
        {
            int segmentSize = segmented.SegmentSize;

            // Each segment is a separate datagram. Resume at the last accepted byte
            // if the socket becomes non-writable partway through this message.
            for (int offset = (int)@in.CurrentProgress; offset < dataLen;)
            {
                int count = Math.Min(segmentSize, dataLen - offset);
                if (SendDatagram(nioData.Slice(offset, count), remoteAddress) <= 0)
                {
                    return false;
                }

                offset += count;
                @in.Progress(count);
            }

            return true;
        }

        int writtenBytes = SendDatagram(nioData, remoteAddress);
        return writtenBytes > 0;
    }

    // CLR: javaChannel().send(ByteBuffer, SocketAddress)/write(ByteBuffer) as non-blocking SendTo/Send; WouldBlock
    // sends zero bytes. A datagram is sent atomically, so a segmented payload is gathered into one borrow.
    private int SendDatagram(ReadOnlySequence<byte> sequence, EndPoint target)
    {
        byte[] staging = null;

        try
        {
            ReadOnlyMemory<byte> memory = sequence.First;

            if (!sequence.IsSingleSegment)
            {
                int length = (int)sequence.Length;
                staging = ArrayPool<byte>.Shared.Rent(length);
                sequence.CopyTo(staging);
                memory = staging.AsMemory(0, length);
            }

            try
            {
                int sent = target != null
                    ? JavaChannel().SendTo(memory.Span, SocketFlags.None, target)
                    : JavaChannel().Send(memory.Span, SocketFlags.None);
                if (sent != memory.Length)
                    throw new IOException("A UDP datagram was only partially accepted.");

                return sent;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
            {
                return 0;
            }
        }
        finally
        {
            if (staging != null)
            {
                ArrayPool<byte>.Shared.Return(staging);
            }
        }
    }

    protected override int DoReadMessages(List<object> buf)
    {
        Socket ch = JavaChannel();
        IChannelConfig config = Config;
        IRecvByteBufAllocator.IHandle allocHandle = RecvBufAllocHandle();

        ByteBuf data = allocHandle.Allocate(config.GetAllocator());
        allocHandle.AttemptedBytesRead(data.WritableBytes);
        bool free = true;

        try
        {
            Memory<byte> nioData = data.AsMemory(data.WriterIndex, data.WritableBytes);
            IPEndPoint any = ch.AddressFamily == AddressFamily.InterNetworkV6
                ? new IPEndPoint(IPAddress.IPv6Any, 0) : new IPEndPoint(IPAddress.Any, 0);
            SocketReceiveFromResult result;

            try
            {
                result = ReceiveDatagram(nioData, any);
            }
            catch (SocketException error) when (error.SocketErrorCode == SocketError.WouldBlock)
            {
                return 0;
            }

            allocHandle.LastBytesRead(result.ReceivedBytes);
            data.WriterIndex += result.ReceivedBytes;
            buf.Add(new DatagramPacket(data, (IPEndPoint)LocalAddress, (IPEndPoint)Normalize(result.RemoteEndPoint)));
            free = false;
            return 1;
        }
        finally
        {
            if (free)
            {
                data.Release();
            }
        }
    }

    protected virtual SocketReceiveFromResult ReceiveDatagram(Memory<byte> memory, EndPoint remote)
    {
        // Windows reports WSAEMSGSIZE for a datagram larger than the buffer, where DatagramChannel.receive
        // silently truncates it. Receive one complete UDP datagram into bounded pooled staging, then apply the
        // Java truncation.
        if (OperatingSystem.IsWindows() && memory.Length < 65536)
        {
            byte[] staging = ArrayPool<byte>.Shared.Rent(65536);

            try
            {
                int received = JavaChannel().ReceiveFrom(staging, 0, 65536, SocketFlags.None, ref remote);
                int count = Math.Min(received, memory.Length);
                staging.AsMemory(0, count).CopyTo(memory);

                return new SocketReceiveFromResult { ReceivedBytes = count, RemoteEndPoint = remote };
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(staging);
            }
        }

        int length = JavaChannel().ReceiveFrom(memory.Span, SocketFlags.None, ref remote);
        return new SocketReceiveFromResult { ReceivedBytes = length, RemoteEndPoint = remote };
    }

    protected override bool ContinueReading(IRecvByteBufAllocator.IHandle allocHandle)
    {
        if (allocHandle is IRecvByteBufAllocator.IExtendedHandle extendedHandle)
        {
            // We use the TRUE_SUPPLIER as it is also ok to read less then what we did try to read (as long
            // as we read anything).
            return extendedHandle.ContinueReading(static () => true);
        }

        return allocHandle.ContinueReading();
    }

    protected override bool CloseOnReadError(Exception cause)
    {
        // We do not want to close on SocketException when using DatagramChannel as we usually can continue receiving.
        // See https://github.com/netty/netty/issues/5893
        if (cause is SocketException)
        {
            return false;
        }

        return base.CloseOnReadError(cause);
    }

    public Task JoinGroupAsync(IPAddress multicastAddress, IPAddress interfaceAddress = null)
    {
        return ChangeIPv4MembershipAsync(multicastAddress, interfaceAddress, true);
    }

    public Task LeaveGroupAsync(IPAddress multicastAddress, IPAddress interfaceAddress = null)
    {
        return ChangeIPv4MembershipAsync(multicastAddress, interfaceAddress, false);
    }

    private Task ChangeIPv4MembershipAsync(IPAddress multicastAddress, IPAddress interfaceAddress, bool join)
    {
        return OnLoopAsync(() =>
        {
            ArgumentNullException.ThrowIfNull(multicastAddress);
            if (multicastAddress.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("Use the IPv6 interface-index or network-interface overload.");

            NetworkInterface networkInterface = interfaceAddress == null || interfaceAddress.Equals(IPAddress.Any)
                ? ((DefaultDatagramChannelConfig)Config).GetNetworkInterface0() : FindInterface(interfaceAddress);
            var local = (LocalAddress as IPEndPoint)?.Address;
            var address = networkInterface != null && (interfaceAddress == null || interfaceAddress.Equals(IPAddress.Any))
                ? InterfaceAddress(networkInterface, AddressFamily.InterNetwork)
                : interfaceAddress ?? (local?.AddressFamily == AddressFamily.InterNetwork ? local : IPAddress.Any);
            address ??= IPAddress.Any;
            networkInterface ??= FindInterface(address);
            ChangeMembership(multicastAddress, networkInterface?.GetIPProperties().GetIPv4Properties()?.Index ?? 0, address, join);
        });
    }

    // CLR: the single-address forms of DatagramChannel; this class adds an optional interface address.
    Task IDatagramChannel.JoinGroupAsync(IPAddress multicastAddress)
    {
        return JoinGroupAsync(multicastAddress, null);
    }

    Task IDatagramChannel.LeaveGroupAsync(IPAddress multicastAddress)
    {
        return LeaveGroupAsync(multicastAddress, null);
    }

    public Task JoinGroupAsync(IPAddress multicastAddress, long interfaceIndex)
    {
        return OnLoopAsync(() => ChangeMembership(multicastAddress, interfaceIndex, null, true));
    }

    public Task LeaveGroupAsync(IPAddress multicastAddress, long interfaceIndex)
    {
        return OnLoopAsync(() => ChangeMembership(multicastAddress, interfaceIndex, null, false));
    }

    public Task JoinGroupAsync(IPEndPoint multicastAddress, NetworkInterface networkInterface)
    {
        ArgumentNullException.ThrowIfNull(multicastAddress);

        return ChangeMembershipAsync(multicastAddress.Address, networkInterface, true);
    }

    public Task LeaveGroupAsync(IPEndPoint multicastAddress, NetworkInterface networkInterface)
    {
        ArgumentNullException.ThrowIfNull(multicastAddress);

        return ChangeMembershipAsync(multicastAddress.Address, networkInterface, false);
    }

    private Task ChangeMembershipAsync(IPAddress address, NetworkInterface networkInterface, bool join)
    {
        ArgumentNullException.ThrowIfNull(networkInterface);

        return OnLoopAsync(() =>
        {
            var family = address.AddressFamily;
            if (!SupportsGroupFamily(family))
                throw new ArgumentException("Multicast group and socket families differ.", nameof(address));

            ChangeMembership(address, InterfaceIndex(networkInterface, family),
                family == AddressFamily.InterNetworkV6 ? null : InterfaceAddress(networkInterface, family), join);
        });
    }

    private bool SupportsGroupFamily(AddressFamily family)
    {
        return family == JavaChannel().AddressFamily ||
            (_linuxDualMode && family == AddressFamily.InterNetwork);
    }

    private void ChangeMembership(IPAddress group, long index, IPAddress address, bool join)
    {
        ArgumentNullException.ThrowIfNull(group);

        group = MembershipAddress(group);
        if (!SupportsGroupFamily(group.AddressFamily))
            throw new ArgumentException("Multicast group and socket families differ.", nameof(group));
        if (group.AddressFamily == AddressFamily.InterNetwork && address == null)
            throw new ArgumentException("IPv4 membership requires an interface address; the numeric-index overload is for IPv6.", nameof(address));
        if (!(group.IsIPv6Multicast || group.AddressFamily == AddressFamily.InterNetwork && group.GetAddressBytes()[0] is >= 224 and <= 239))
            throw new ArgumentException("A multicast address is required.", nameof(group));
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, uint.MaxValue);

        var key = (group, index, (IPAddress)null);
        if (join && _memberships.Keys.Any(k => k.Group.Equals(group) && k.Index == index && k.Source != null))
            throw new InvalidOperationException("Cannot join all sources while a source-specific membership exists.");
        if (join ? _memberships.ContainsKey(key) : !_memberships.ContainsKey(key))
            return;

        if (!join)
            address = _memberships[key].Address;

        var option = join ? SocketOptionName.AddMembership : SocketOptionName.DropMembership;

        if (group.AddressFamily == AddressFamily.InterNetworkV6)
            JavaChannel().SetSocketOption(SocketOptionLevel.IPv6, option, new IPv6MulticastOption(group, index));
        else
            JavaChannel().SetSocketOption(SocketOptionLevel.IP, option, new MulticastOption(group, address ?? IPAddress.Any));

        if (join)
            _memberships.Add(key, new Membership(address));
        else
            _memberships.Remove(key);
    }

    public Task JoinGroupAsync(IPAddress multicastAddress, NetworkInterface networkInterface, IPAddress source)
    {
        return ChangeSourceMembershipAsync(multicastAddress, networkInterface, source, true);
    }

    public Task LeaveGroupAsync(IPAddress multicastAddress, NetworkInterface networkInterface, IPAddress source)
    {
        return ChangeSourceMembershipAsync(multicastAddress, networkInterface, source, false);
    }

    private Task ChangeSourceMembershipAsync(IPAddress group, NetworkInterface networkInterface, IPAddress source, bool join)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(networkInterface);
        if (source == null)
        {
            return ChangeMembershipAsync(group, networkInterface, join);
        }

        return OnLoopAsync(() =>
        {
            ValidateSource(group, source);
            group = MembershipAddress(group);
            source = MembershipAddress(source);
            int index = InterfaceIndex(networkInterface, group.AddressFamily);
            var key = (group, (long)index, source);
            if (join && _memberships.ContainsKey((group, index, null)))
                throw new InvalidOperationException("Cannot join a source while an all-sources membership exists.");
            if (join ? _memberships.ContainsKey(key) : !_memberships.ContainsKey(key))
            {
                return;
            }

            var address = group.AddressFamily == AddressFamily.InterNetwork
                ? (join ? InterfaceAddress(networkInterface, group.AddressFamily) : _memberships[key].Address) : null;
            SetSourceOption(join ? SocketOptionName.AddSourceMembership : SocketOptionName.DropSourceMembership, group, index, address, source);

            if (join)
            {
                _memberships.Add(key, new Membership(address));
            }
            else
            {
                _memberships.Remove(key);
            }
        });
    }

    /**
     * Block the given sourceToBlock address for the given multicastAddress on the given networkInterface
     */
    public Task BlockAsync(IPAddress multicastAddress, NetworkInterface networkInterface, IPAddress sourceToBlock)
    {
        ArgumentNullException.ThrowIfNull(multicastAddress);
        ArgumentNullException.ThrowIfNull(networkInterface);
        ArgumentNullException.ThrowIfNull(sourceToBlock);

        return OnLoopAsync(() =>
        {
            ValidateSource(multicastAddress, sourceToBlock);
            multicastAddress = MembershipAddress(multicastAddress);
            sourceToBlock = MembershipAddress(sourceToBlock);
            int index = InterfaceIndex(networkInterface, multicastAddress.AddressFamily);
            if (_memberships.Keys.Any(k => k.Group.Equals(multicastAddress) && k.Index == index && k.Source != null))
                throw new InvalidOperationException("A source-specific membership cannot block sources.");

            if (_memberships.TryGetValue((multicastAddress, index, null), out var membership) && !membership.Blocked.Contains(sourceToBlock))
            {
                SetSourceOption(SocketOptionName.BlockSource, multicastAddress, index, membership.Address, sourceToBlock);
                membership.Blocked.Add(sourceToBlock);
            }
        });
    }

    /**
     * Block the given sourceToBlock address for the given multicastAddress
     *
     */
    public Task BlockAsync(IPAddress multicastAddress, IPAddress sourceToBlock)
    {
        var networkInterface = ((DefaultDatagramChannelConfig)Config).GetNetworkInterface0()
            ?? FindInterface(((IPEndPoint)LocalAddress)?.Address ?? IPAddress.Any)
            ?? throw new InvalidOperationException("No local multicast interface is selected.");
        return BlockAsync(multicastAddress, networkInterface, sourceToBlock);
    }

    private void ValidateSource(IPAddress group, IPAddress source)
    {
        if (!SupportsGroupFamily(group.AddressFamily) || source.AddressFamily != group.AddressFamily)
            throw new NotSupportedException("Source filtering requires matching group and source families supported by the socket.");

        if (group.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (!group.IsIPv6Multicast)
                throw new ArgumentException("A multicast address is required.", nameof(group));
            if (MembershipAddress(source).Equals(IPAddress.IPv6Any) || source.IsIPv6Multicast || source.IsIPv4MappedToIPv6)
                throw new ArgumentException("An IPv6 unicast source address is required.", nameof(source));

            return;
        }

        if (group.GetAddressBytes()[0] is < 224 or > 239)
            throw new ArgumentException("A multicast address is required.", nameof(group));
        if (source.Equals(IPAddress.Any) || source.Equals(IPAddress.Broadcast) || source.GetAddressBytes()[0] is >= 224 and <= 239)
            throw new ArgumentException("A unicast source address is required.", nameof(source));
    }

    // The membership key already identifies the interface. Scoped and unscoped
    // forms of the same IPv6 address identify the same membership on that interface.
    private static IPAddress MembershipAddress(IPAddress address)
    {
        return address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0
            ? new IPAddress(address.GetAddressBytes()) : address;
    }

    private void SetSourceOption(SocketOptionName option, IPAddress group, int index, IPAddress address, IPAddress source)
    {
        if (group.AddressFamily == AddressFamily.InterNetworkV6)
        {
            SetIPv6SourceOption(option, group, index, source);
            return;
        }

        // IP_MREQ_SOURCE is group/source/interface on Windows, group/interface/source on Linux.
        // Keep addresses in network byte order; SocketOptionName supplies the platform option number.
        // https://learn.microsoft.com/windows/win32/api/ws2ipdef/ns-ws2ipdef-ip_mreq_source
        // https://github.com/torvalds/linux/blob/master/include/uapi/linux/in.h
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Source membership layout has not been reviewed for this platform.");

        var request = new byte[12];
        group.GetAddressBytes().CopyTo(request, 0);
        (OperatingSystem.IsLinux() ? address : source).GetAddressBytes().CopyTo(request, 4);
        (OperatingSystem.IsLinux() ? source : address).GetAddressBytes().CopyTo(request, 8);
        JavaChannel().SetSocketOption(SocketOptionLevel.IP, option, request);
    }

    private void SetIPv6SourceOption(SocketOptionName option, IPAddress group, int index, IPAddress source)
    {
        bool windows = OperatingSystem.IsWindows();
        if (!windows && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("IPv6 source filtering is supported on Windows and Linux.");

        // GROUP_SOURCE_REQ has a native-endian uint followed by two 128-byte
        // sockaddr_storage values. Winsock aligns them to 8 and Linux to pointer size.
        // https://learn.microsoft.com/windows/win32/api/ws2ipdef/ns-ws2ipdef-group_source_req
        // https://github.com/torvalds/linux/blob/master/include/uapi/linux/in.h
        const int storageSize = 128;
        int offset = windows ? 8 : IntPtr.Size;
        Span<byte> request = stackalloc byte[offset + 2 * storageSize];
        request.Clear();
        BitConverter.TryWriteBytes(request, index);
        WriteAddress(group, request.Slice(offset, storageSize));
        WriteAddress(source, request.Slice(offset + storageSize, storageSize));

        int name = option switch
        {
            SocketOptionName.AddSourceMembership => windows ? 45 : 46,
            SocketOptionName.DropSourceMembership => windows ? 46 : 47,
            SocketOptionName.BlockSource => 43,
            _ => throw new ArgumentOutOfRangeException(nameof(option))
        };
        JavaChannel().SetRawSocketOption((int)SocketOptionLevel.IPv6, name, request);

        void WriteAddress(IPAddress address, Span<byte> destination)
        {
            if (address.IsIPv6LinkLocal || (address.IsIPv6Multicast && (address.GetAddressBytes()[1] & 15) <= 2))
                address = new IPAddress(address.GetAddressBytes(), index);

            SocketAddress serialized = new IPEndPoint(address, 0).Serialize();
            serialized.Buffer.Span[..serialized.Size].CopyTo(destination);
        }
    }

    internal static NetworkInterface FindInterface(IPAddress address)
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)));
    }

    internal static IPAddress InterfaceAddress(NetworkInterface networkInterface, AddressFamily family)
    {
        return networkInterface.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == family)?.Address
            ?? throw new ArgumentException("The interface has no address for the socket family.", nameof(networkInterface));
    }

    internal static int InterfaceIndex(NetworkInterface networkInterface, AddressFamily family)
    {
        return family == AddressFamily.InterNetworkV6 ? networkInterface.GetIPProperties().GetIPv6Properties()?.Index
            ?? throw new ArgumentException("The interface does not support IPv6.", nameof(networkInterface))
            : networkInterface.GetIPProperties().GetIPv4Properties()?.Index
            ?? throw new ArgumentException("The interface does not support IPv4.", nameof(networkInterface));
    }
}
