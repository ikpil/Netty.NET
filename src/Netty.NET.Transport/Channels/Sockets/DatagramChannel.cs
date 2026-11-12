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

namespace Netty.NET.Transport.Channels.Sockets;

/// <summary>Datagram boundaries and endpoint metadata preserved over CLR asynchronous UDP sockets.</summary>
/**
 * An NIO datagram {@link Channel} that sends and receives an
 * {@link AddressedEnvelope AddressedEnvelope<ByteBuf, SocketAddress>}.
 *
 * @see AddressedEnvelope
 * @see DatagramPacket
 */
/**
 * A UDP/IP {@link Channel}.
 */
public class DatagramChannel : AbstractChannel
{
    private readonly Socket _socket;
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
    private bool _reading;
    private bool _readRequested;
    private bool _udpGro;
    private int _udpSlotSize;
    private bool _receiveOriginalDestination;
    private bool _offloadConfigurationClosed;
    public DatagramChannel() : this(AddressFamily.InterNetwork) { }
    public DatagramChannel(AddressFamily family) : this(family, false) { }

    /// <summary>Creates a UDP channel with optional IPv4-mapped traffic on an IPv6 socket.</summary>
    public DatagramChannel(AddressFamily family, bool dualMode) : base(null, new ChannelMetadata(true, 16))
    {
        if (dualMode && family != AddressFamily.InterNetworkV6)
            throw new ArgumentException("Dual mode requires an IPv6 socket.", nameof(family));
        _socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        _linuxDualMode = OperatingSystem.IsLinux() && dualMode;
        try
        {
            if (dualMode) _socket.DualMode = true;
            if (OperatingSystem.IsLinux())
            {
                if (Socket.OSSupportsIPv4 && (family == AddressFamily.InterNetwork || dualMode))
                    DisableMulticastAll(SocketOptionLevel.IP, 49); // IP_MULTICAST_ALL
                if (family == AddressFamily.InterNetworkV6)
                    DisableMulticastAll(SocketOptionLevel.IPv6, 29); // IPV6_MULTICAST_ALL
            }
        }
        catch { _socket.Dispose(); throw; }
    }

    private void DisableMulticastAll(SocketOptionLevel level, int option)
    {
        // As in OpenJDK, receive only memberships held by this socket. Older kernels may lack the option.
        // https://github.com/openjdk/jdk/blob/master/src/java.base/unix/native/libnio/ch/Net.c
        Span<byte> disabled = stackalloc byte[sizeof(int)];
        disabled.Clear();
        try { _socket.SetRawSocketOption((int)level, option, disabled); }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.ProtocolOption) { }
    }
    public override EndPoint LocalAddress => IsOpen ? TcpSocketChannel.Normalize(Volatile.Read(ref _linuxLocalAddress) ?? _socket.LocalEndPoint) : null;
    public override EndPoint RemoteAddress => IsOpen ? Volatile.Read(ref _connectedAddress) : null;
    protected override IChannelConfig NewConfig() => new DatagramConfig(this, _socket);
    protected override Task DoBindAsync(EndPoint address, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_socket.AddressFamily == AddressFamily.InterNetworkV6 && address is IPEndPoint ip && ip.Address.Equals(IPAddress.Any))
            address = new IPEndPoint(IPAddress.IPv6Any, ip.Port);
        _socket.Bind(address);
        SetActive(true);
        return Task.CompletedTask;
    }
    internal override bool UsesSocketCompletions => true;
    protected override async Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken token)
    {
        if (local != null)
            _socket.Bind(local);
        var bound = OperatingSystem.IsLinux() ? _linuxBoundAddress ?? (IPEndPoint)_socket.LocalEndPoint : null;
        try { await _socket.ConnectAsync(remote, token).ConfigureAwait(false); }
        catch (SocketException)
        {
            // NioDatagramChannel closes when the native connect fails. The socket may own multicast
            // state that cannot be copied to a replacement descriptor by Socket.ConnectAsync.
            try { await OnSocketLoopAsync(() => { _ = Close(); }).ConfigureAwait(false); }
            catch (RejectedExecutionException) { } // The event loop's shutdown hook owns cleanup.
            throw;
        }
        await OnSocketLoopAsync(() =>
        {
            EnsureOpen();
            if (OperatingSystem.IsLinux())
            {
                var connected = LinuxSocketOptions.GetLocalEndpoint(_socket);
                _linuxBoundAddress = bound ?? new IPEndPoint(AnyAddress, connected.Port);
                Volatile.Write(ref _linuxLocalAddress, connected);
            }
            Volatile.Write(ref _connectedAddress, TcpSocketChannel.Normalize(remote));
            SetActive(true);
        }).ConfigureAwait(false);
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
                _socket.Connect(new IPEndPoint(AnyAddress, 0));
            else if (OperatingSystem.IsLinux())
                DisconnectLinux();
            else
                _socket.Disconnect(true);
            Volatile.Write(ref _connectedAddress, null);
            SetActive(_socket.IsBound || ActiveOnRegistration);
        }
        return Task.CompletedTask;
    }

    private IPAddress AnyAddress => _socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;

    private void DisconnectLinux()
    {
        // AF_UNSPEC detaches the UDP peer while keeping the descriptor, its socket options and pending receives.
        // Linux may release an ephemeral port. Restore the original binding just as the JDK channel does.
        // https://github.com/openjdk/jdk/blob/master/src/java.base/share/classes/sun/nio/ch/DatagramChannelImpl.java
        byte[] unspecified = new byte[16]; // struct sockaddr with sa_family = AF_UNSPEC (0)
        if (ConnectNative(_socket.SafeHandle, unspecified, (uint)unspecified.Length) != 0)
            throw new SocketException();

        try
        {
            var local = LinuxSocketOptions.GetLocalEndpoint(_socket);
            if (local.Port == 0)
            {
                _socket.Bind(_linuxBoundAddress);
                local = LinuxSocketOptions.GetLocalEndpoint(_socket);
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

    /**
     * Return {@code true} if the {@link DatagramChannel} is connected to the remote peer.
     */
    public bool IsConnected => RemoteAddress != null;
#pragma warning disable CS0612, CS0618 // Preserve the original optional active-on-registration setting.
    private bool ActiveOnRegistration =>
        Config.TryGetOption(ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, out bool active) && active;
#pragma warning restore CS0612, CS0618
    protected override void DoRegister()
    {
        lock (_socket)
            _offloadConfigurationClosed = true;
        SetActive(_socket.IsBound || ActiveOnRegistration);
    }

    private void SetUdpGro(bool enabled)
    {
        lock (_socket)
        {
            if (_udpGro == enabled) return;
            EnsureOffloadConfigurationOpen();
            LinuxUdpOffload.SetGro(_socket, enabled);
            Volatile.Write(ref _udpGro, enabled);
        }
    }

    private void SetUdpSlotSize(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        lock (_socket)
        {
            if (_udpSlotSize == size) return;
            EnsureOffloadConfigurationOpen();
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("UDP scattering requires Linux.");
            Volatile.Write(ref _udpSlotSize, size);
        }
    }

    /**
     * If {@code true} is used <a href="https://man7.org/linux/man-pages/man7/ip.7.html">IP_RECVORIGDSTADDR</a> is
     * enabled, {@code false} for disable it. Default is disabled.
     */
    private void SetReceiveOriginalDestination(bool enabled)
    {
        lock (_socket)
        {
            if (_receiveOriginalDestination == enabled) return;
            EnsureOffloadConfigurationOpen();
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("IP_RECVORIGDSTADDR requires Linux.");
            LinuxSocketOptions.SetFlag(_socket, (int)SocketOptionLevel.IP, LinuxUdpOffload.IpOriginalDestination, enabled);
            Volatile.Write(ref _receiveOriginalDestination, enabled);
        }
    }

    private void EnsureOffloadConfigurationOpen()
    {
        if (_offloadConfigurationClosed)
            throw new InvalidOperationException("Set UDP receive offload options before registering the channel.");
    }
    protected override void DoDeregister() => SetActive(_socket.IsBound);
    protected override void DoClose() { _memberships.Clear(); _socket.Dispose(); }
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
    protected override object FilterOutboundMessage(object message)
    {
        if (message is IAddressedEnvelope<ByteBuf, EndPoint> envelope && envelope.Recipient is DnsEndPoint)
            throw new SocketException((int)SocketError.HostNotFound);
        if (message is ByteBuf or IAddressedEnvelope<ByteBuf, EndPoint>)
            return message;
        throw new NotSupportedException("UDP writes require a ByteBuf or an addressed ByteBuf envelope.");
    }
    protected override int? MaxMessagesPerWrite() => Config is DefaultChannelConfig config
        ? config.GetMaxMessagesPerWrite()
        : Config.TryGetOption(ChannelOption.MAX_MESSAGES_PER_WRITE, out int value) ? value : int.MaxValue;

    protected override async Task DoWrite(object message)
    {
        var envelope = message as IAddressedEnvelope<ByteBuf, EndPoint>;
        ByteBuf data = envelope?.Content ?? (ByteBuf)message;
        int length = data.ReadableBytes;
        // Empty buffers, including cancelled outbound entries, complete without a native send.
        if (length == 0) return;
        if (envelope?.Recipient == null && !IsConnected)
            throw new SocketException((int)SocketError.NotConnected);
        var sequence = data.ReadableSequence;
        var target = envelope?.Recipient;
        if (message is SegmentedDatagramPacket segmented && length > segmented.SegmentSize)
        {
            int segmentSize = segmented.SegmentSize;
            bool sentWithOffload = LinuxUdpOffload.CanSend(_socket, length, segmentSize) &&
                TrySendSegmented(sequence, length, segmentSize, segmented.Recipient);
            if (!sentWithOffload)
            {
                for (int offset = 0; offset < length;)
                {
                    int count = Math.Min(segmentSize, length - offset);
                    await SendDatagramAsync(sequence.Slice(offset, count), target).ConfigureAwait(false);
                    offset += count;
                }
            }
        }
        else
            await SendDatagramAsync(sequence, target).ConfigureAwait(false);

        await OnSocketLoopAsync(() =>
        {
            OutboundBuffer.Progress(length);
            // CLR receive cannot start on an unbound socket. A first send can implicitly bind it.
            if (!_reading && _readRequested && IsOpen)
                DoBeginRead();
        }).ConfigureAwait(false);
    }


    private bool TrySendSegmented(ReadOnlySequence<byte> sequence, int length, int segmentSize, IPEndPoint recipient)
    {
        if (sequence.IsSingleSegment)
            return LinuxUdpOffload.TrySend(_socket, sequence.First, segmentSize, recipient);
        byte[] staging = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            sequence.CopyTo(staging);
            return LinuxUdpOffload.TrySend(_socket, staging.AsMemory(0, length), segmentSize, recipient);
        }
        finally { ArrayPool<byte>.Shared.Return(staging); }
    }

    private async Task SendDatagramAsync(ReadOnlySequence<byte> sequence, EndPoint target)
    {
        byte[] staging = null;
        try
        {
            ReadOnlyMemory<byte> memory = sequence.First;
            if (!sequence.IsSingleSegment)
            {
                // A datagram must be sent atomically. As in the original composite-buffer copy path,
                // gather a segmented payload into a contiguous borrow rather than sending several datagrams.
                int length = (int)sequence.Length;
                staging = ArrayPool<byte>.Shared.Rent(length);
                sequence.CopyTo(staging);
                memory = staging.AsMemory(0, length);
            }
            int sent = target != null
                ? await _socket.SendToAsync(memory, SocketFlags.None, target).ConfigureAwait(false)
                : await _socket.SendAsync(memory, SocketFlags.None).ConfigureAwait(false);
            if (sent != sequence.Length)
                throw new IOException("A UDP datagram was only partially accepted.");
        }
        finally { if (staging != null) ArrayPool<byte>.Shared.Return(staging); }
    }
    protected override void DoBeginRead()
    {
        _readRequested = true;
        ResumeRead();
        if (!_socket.IsBound)
            return;
        if (_reading)
            return;
        _reading = true;
        StartSocketWork(() => _ = ReadLoopAsync());
    }
    protected override void DoClearReadPending() => _readRequested = false;
    private readonly record struct PendingDatagram(ByteBuf Buffer, Task<LinuxUdpOffload.ReadResult> Completion);

    private async Task ReadLoopAsync()
    {
        IPEndPoint any = _socket.AddressFamily == AddressFamily.InterNetworkV6
            ? new IPEndPoint(IPAddress.IPv6Any, 0) : new IPEndPoint(IPAddress.Any, 0);
        var batch = new List<DatagramPacket>();
        PendingDatagram? pending = null;
        try
        {
            bool again = true;
            while ((again || pending != null) && IsOpen)
            {
                Exception failure = null;
                IRecvByteBufAllocator.IHandle handle = null;
                try
                {
                    await OnSocketLoopAsync(() =>
                    {
                        if (!IsOpen || (pending == null && (!IsRegistered || (!Config.IsAutoRead() && !_readRequested))))
                            return;

                        handle = ReceiveHandle;
                        handle.Reset(Config);
                        pending ??= StartReceive(handle, any);
                    }).ConfigureAwait(false);
                    if (handle == null) break;

                    // Await readiness without consuming the outcome. The owner prepares and delivers
                    // the entire ready batch before returning to the ordinary task queue.
                    await ((Task)pending.Value.Completion).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
                catch (Exception error) { failure = error; }

                again = false;
                await DispatchReadResultAsync(() => _readRequested, CompleteBatch).ConfigureAwait(false);
                ReleaseBatch();

                if (again || pending != null)
                    await QueueSocketWorkAsync(() => { }).ConfigureAwait(false);

                bool CompleteBatch()
                {
                    if (failure == null)
                    {
                        try
                        {
                            do
                            {
                                PendingDatagram receive = pending.Value;
                                pending = null;
                                ByteBuf data = receive.Buffer;
                                try
                                {
                                    var result = receive.Completion.GetAwaiter().GetResult();
                                    handle.AttemptedBytesRead(data.WritableBytes);
                                    handle.LastBytesRead(result.BytesRead);
                                    var recipient = (IPEndPoint)LocalAddress;
                                    int start = data.WriterIndex;
                                    if (result.Batch is { } packets)
                                    {
                                        var last = packets[^1];
                                        data.WriterIndex = start + last.Offset + last.Length;
                                        foreach (var packet in packets)
                                            AddDatagramSlices(data, start, packet, recipient, handle, batch);
                                    }
                                    else
                                    {
                                        var packet = result.Single;
                                        data.WriterIndex += packet.Length;
                                        if (packet.SegmentSize > 0 && packet.SegmentSize < packet.Length)
                                            AddDatagramSlices(data, start, packet, recipient, handle, batch);
                                        else
                                        {
                                            handle.IncMessagesRead(1);
                                            batch.Add(new DatagramPacket(data, packet.Recipient ?? recipient,
                                                (IPEndPoint)TcpSocketChannel.Normalize(packet.Sender)));
                                            data = null;
                                        }
                                    }
                                }
                                finally { data?.Release(); }

                                if (IsRegistered && ContinueReading(handle) && _socket.Poll(0, SelectMode.SelectRead))
                                    pending = StartReceive(handle, any);
                                // A readiness hint can race with a socket change. Deliver the current batch
                                // before waiting for a receive that did not finish synchronously.
                            } while (pending?.Completion.IsCompleted == true);
                        }
                        catch (Exception error) { failure = error; }
                    }

                    for (int i = 0; i < batch.Count && IsOpen; i++)
                    {
                        _readRequested = false;
                        var packet = batch[i];
                        batch[i] = null;
                        Pipeline.FireChannelRead(packet);
                    }
                    handle?.ReadComplete();
                    Pipeline.FireChannelReadComplete();
                    if (failure != null)
                    {
                        // We do not want to close on SocketException when using DatagramChannel as we usually can continue receiving.
                        // See https://github.com/netty/netty/issues/5893
                        Pipeline.FireExceptionCaught(failure);
                        if (failure is not SocketException && IsOpen) _ = Close();
                    }
                    // Check if there is a readPending which was not processed yet.
                    // This could be for two reasons:
                    // * The user called Channel.read() or ChannelHandlerContext.read() in channelRead(...) method
                    // * The user called Channel.read() or ChannelHandlerContext.read() in channelReadComplete(...) method
                    //
                    // See https://github.com/netty/netty/issues/2254
                    again = IsOpen && (Config.IsAutoRead() || _readRequested);
                    return batch.Count != 0;
                }
            }
        }
        catch (Exception error)
        {
            try
            {
                await OnSocketLoopAsync(() =>
                {
                    if (!IsOpen) return;
                    Pipeline.FireExceptionCaught(error);
                    _ = Close();
                }).ConfigureAwait(false);
            }
            catch { }
        }
        finally
        {
            ReleaseBatch();
            if (pending is { } receive)
            {
                try { await receive.Completion.ConfigureAwait(false); }
                catch { }
                finally { receive.Buffer.Release(); }
            }
            try
            { await OnSocketLoopAsync(() => { _reading = false; if (IsOpen && IsRegistered && _readRequested) DoBeginRead(); }).ConfigureAwait(false); }
            catch { }
        }

        void ReleaseBatch()
        {
            foreach (var packet in batch) packet?.Release();
            batch.Clear();
        }
    }

    private static void AddDatagramSlices(ByteBuf data, int start, LinuxUdpOffload.Received packet, IPEndPoint recipient,
        IRecvByteBufAllocator.IHandle handle, List<DatagramPacket> batch)
    {
        var sender = (IPEndPoint)TcpSocketChannel.Normalize(packet.Sender);
        recipient = packet.Recipient ?? recipient;
        int segmentSize = packet.SegmentSize > 0 ? packet.SegmentSize : packet.Length;
        int offset = 0;
        // Retain the payload of each delivered datagram independently of the aggregate.
        do
        {
            int count = Math.Min(segmentSize, packet.Length - offset);
            batch.Add(new DatagramPacket(data.RetainedSlice(start + packet.Offset + offset, count), recipient, sender));
            handle.IncMessagesRead(1);
            offset += count;
        } while (offset < packet.Length);
    }

    private PendingDatagram StartReceive(IRecvByteBufAllocator.IHandle handle, IPEndPoint any)
    {
        var data = handle.Allocate(Allocator);
        try
        {
            handle.AttemptedBytesRead(data.WritableBytes);
            Memory<byte> memory = data.AsMemory(data.WriterIndex, data.WritableBytes);
            return new PendingDatagram(data, _udpGro || _udpSlotSize != 0 || _receiveOriginalDestination
                ? LinuxUdpOffload.ReceiveAsync(_socket, memory, any, _udpSlotSize) : ReceivePacketAsync(memory, any));
        }
        catch { data.Release(); throw; }
    }

    private static bool ContinueReading(IRecvByteBufAllocator.IHandle handle) =>
        // We use the TRUE_SUPPLIER as it is also ok to read less then what we did try to read (as long
        // as we read anything).
        handle is IRecvByteBufAllocator.IExtendedHandle extended
            ? extended.ContinueReading(static () => true) : handle.ContinueReading();

    private async Task<LinuxUdpOffload.ReadResult> ReceivePacketAsync(Memory<byte> memory, EndPoint remote)
    {
        var result = await ReceiveDatagramAsync(memory, remote).ConfigureAwait(false);
        return new LinuxUdpOffload.ReadResult(new(result.ReceivedBytes, result.RemoteEndPoint, 0), BytesRead: result.ReceivedBytes);
    }

    protected virtual async Task<SocketReceiveFromResult> ReceiveDatagramAsync(Memory<byte> memory, EndPoint remote)
    {
        // .NET's Windows failure path does not materialize the sender on WSAEMSGSIZE. Receive one
        // complete UDP datagram into bounded pooled staging, then apply the configured Java-style
        // truncation policy. The staging remains owned until native receive completes, including close.
        if (OperatingSystem.IsWindows() && memory.Length < 65536)
        {
            byte[] staging = ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                var packet = await _socket.ReceiveFromAsync(staging.AsMemory(0, 65536), SocketFlags.None, remote).ConfigureAwait(false);
                int count = Math.Min(packet.ReceivedBytes, memory.Length);
                staging.AsMemory(0, count).CopyTo(memory);
                return new SocketReceiveFromResult { ReceivedBytes = count, RemoteEndPoint = packet.RemoteEndPoint };
            }
            finally { ArrayPool<byte>.Shared.Return(staging); }
        }
        return await _socket.ReceiveFromAsync(memory, SocketFlags.None, remote).ConfigureAwait(false);
    }
    /**
     * Joins a multicast group and notifies the {@link ChannelFuture} once the operation completes.
     */
    public Task JoinGroupAsync(IPAddress multicastAddress, IPAddress interfaceAddress = null)
        => ChangeIPv4MembershipAsync(multicastAddress, interfaceAddress, true);
    /**
     * Leaves a multicast group and notifies the {@link ChannelFuture} once the operation completes.
     */
    public Task LeaveGroupAsync(IPAddress multicastAddress, IPAddress interfaceAddress = null)
        => ChangeIPv4MembershipAsync(multicastAddress, interfaceAddress, false);
    private Task ChangeIPv4MembershipAsync(IPAddress multicastAddress, IPAddress interfaceAddress, bool join) => OnLoopAsync(() =>
    {
        ArgumentNullException.ThrowIfNull(multicastAddress);
        if (multicastAddress.AddressFamily != AddressFamily.InterNetwork)
            throw new NotSupportedException("Use the IPv6 interface-index or network-interface overload.");
        NetworkInterface networkInterface = interfaceAddress == null || interfaceAddress.Equals(IPAddress.Any)
            ? ((DatagramConfig)Config).GetNetworkInterface() : FindInterface(interfaceAddress);
        var local = (LocalAddress as IPEndPoint)?.Address;
        var address = networkInterface != null && (interfaceAddress == null || interfaceAddress.Equals(IPAddress.Any))
            ? InterfaceAddress(networkInterface, AddressFamily.InterNetwork)
            : interfaceAddress ?? (local?.AddressFamily == AddressFamily.InterNetwork ? local : IPAddress.Any);
        address ??= IPAddress.Any;
        networkInterface ??= FindInterface(address);
        ChangeMembership(multicastAddress, networkInterface?.GetIPProperties().GetIPv4Properties()?.Index ?? 0, address, join);
    });
    public Task JoinGroupAsync(IPAddress multicastAddress, long interfaceIndex)
        => OnLoopAsync(() => ChangeMembership(multicastAddress, interfaceIndex, null, true));
    public Task LeaveGroupAsync(IPAddress multicastAddress, long interfaceIndex)
        => OnLoopAsync(() => ChangeMembership(multicastAddress, interfaceIndex, null, false));
    /**
     * Joins the specified multicast group at the specified interface and notifies the {@link ChannelFuture}
     * once the operation completes.
     */
    public Task JoinGroupAsync(IPEndPoint multicastAddress, NetworkInterface networkInterface)
    {
        ArgumentNullException.ThrowIfNull(multicastAddress);
        return ChangeMembershipAsync(multicastAddress.Address, networkInterface, true);
    }
    /**
     * Leaves a multicast group on a specified local interface and notifies the {@link ChannelFuture} once the
     * operation completes.
     */
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

    private bool SupportsGroupFamily(AddressFamily family) => family == _socket.AddressFamily ||
        (_linuxDualMode && family == AddressFamily.InterNetwork);

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
            _socket.SetSocketOption(SocketOptionLevel.IPv6, option, new IPv6MulticastOption(group, index));
        else
            _socket.SetSocketOption(SocketOptionLevel.IP, option, new MulticastOption(group, address ?? IPAddress.Any));
        if (join)
            _memberships.Add(key, new Membership(address));
        else
            _memberships.Remove(key);
    }
    /**
     * Joins the specified multicast group at the specified interface and notifies the {@link ChannelFuture}
     * once the operation completes.
     */
    public Task JoinGroupAsync(IPAddress multicastAddress, NetworkInterface networkInterface, IPAddress source)
        => ChangeSourceMembershipAsync(multicastAddress, networkInterface, source, true);
    /**
     * Leave the specified multicast group at the specified interface using the specified source and notifies
     * the {@link ChannelFuture} once the operation completes.
     *
     */
    public Task LeaveGroupAsync(IPAddress multicastAddress, NetworkInterface networkInterface, IPAddress source)
        => ChangeSourceMembershipAsync(multicastAddress, networkInterface, source, false);
    private Task ChangeSourceMembershipAsync(IPAddress group, NetworkInterface networkInterface, IPAddress source, bool join)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(networkInterface);
        if (source == null) return ChangeMembershipAsync(group, networkInterface, join);
        return OnLoopAsync(() =>
        {
            ValidateSource(group, source);
            group = MembershipAddress(group);
            source = MembershipAddress(source);
            int index = InterfaceIndex(networkInterface, group.AddressFamily);
            var key = (group, (long)index, source);
            if (join && _memberships.ContainsKey((group, index, null)))
                throw new InvalidOperationException("Cannot join a source while an all-sources membership exists.");
            if (join ? _memberships.ContainsKey(key) : !_memberships.ContainsKey(key)) return;
            var address = group.AddressFamily == AddressFamily.InterNetwork
                ? (join ? InterfaceAddress(networkInterface, group.AddressFamily) : _memberships[key].Address) : null;
            SetSourceOption(join ? SocketOptionName.AddSourceMembership : SocketOptionName.DropSourceMembership, group, index, address, source);
            if (join) _memberships.Add(key, new Membership(address)); else _memberships.Remove(key);
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
        var networkInterface = ((DatagramConfig)Config).GetNetworkInterface()
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
    private static IPAddress MembershipAddress(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0
            ? new IPAddress(address.GetAddressBytes()) : address;

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
        _socket.SetSocketOption(SocketOptionLevel.IP, option, request);
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
        _socket.SetRawSocketOption((int)SocketOptionLevel.IPv6, name, request);

        void WriteAddress(IPAddress address, Span<byte> destination)
        {
            if (address.IsIPv6LinkLocal || (address.IsIPv6Multicast && (address.GetAddressBytes()[1] & 15) <= 2))
                address = new IPAddress(address.GetAddressBytes(), index);
            SocketAddress serialized = new IPEndPoint(address, 0).Serialize();
            serialized.Buffer.Span[..serialized.Size].CopyTo(destination);
        }
    }
    private static NetworkInterface FindInterface(IPAddress address) => NetworkInterface.GetAllNetworkInterfaces()
        .FirstOrDefault(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)));
    private static IPAddress InterfaceAddress(NetworkInterface networkInterface, AddressFamily family)
        => networkInterface.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == family)?.Address
            ?? throw new ArgumentException("The interface has no address for the socket family.", nameof(networkInterface));
    private static int InterfaceIndex(NetworkInterface networkInterface, AddressFamily family)
        => family == AddressFamily.InterNetworkV6 ? networkInterface.GetIPProperties().GetIPv6Properties()?.Index
            ?? throw new ArgumentException("The interface does not support IPv6.", nameof(networkInterface))
            : networkInterface.GetIPProperties().GetIPv4Properties()?.Index
            ?? throw new ArgumentException("The interface does not support IPv4.", nameof(networkInterface));
    #pragma warning disable CS0612, CS0618 // The source config supports the deprecated active-on-registration option.
    /**
 * A {@link ChannelConfig} for a {@link DatagramChannel}.
 *
 * <h3>Available options</h3>
 *
 * In addition to the options provided by {@link ChannelConfig},
 * {@link DatagramChannelConfig} allows the following options in the option map:
 *
 * <table border="1" cellspacing="0" cellpadding="6">
 * <tr>
 * <th>Name</th><th>Associated setter method</th>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_BROADCAST}</td><td>{@link #setBroadcast(boolean)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#IP_MULTICAST_ADDR}</td><td>{@link #setInterface(InetAddress)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#IP_MULTICAST_LOOP_DISABLED}</td>
 * <td>{@link #setLoopbackModeDisabled(boolean)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#IP_MULTICAST_IF}</td>
 * <td>{@link #setNetworkInterface(NetworkInterface)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_REUSEADDR}</td><td>{@link #setReuseAddress(boolean)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_RCVBUF}</td><td>{@link #setReceiveBufferSize(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_SNDBUF}</td><td>{@link #setSendBufferSize(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#IP_MULTICAST_TTL}</td><td>{@link #setTimeToLive(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#IP_TOS}</td><td>{@link #setTrafficClass(int)}</td>
 * </tr>
 * </table>
 */
    private sealed class DatagramConfig : DefaultChannelConfig
    {
        internal bool ActiveOnRegistration { get; private set; }
        public override IReadOnlyDictionary<ChannelOption, object> GetOptions()
        {
            var options = new Dictionary<ChannelOption, object>(base.GetOptions());
            foreach (var option in new ChannelOption[] { ChannelOption.SO_BROADCAST, ChannelOption.SO_RCVBUF,
                ChannelOption.SO_SNDBUF, ChannelOption.SO_REUSEADDR, ChannelOption.IP_MULTICAST_LOOP_DISABLED,
                ChannelOption.IP_MULTICAST_ADDR, ChannelOption.IP_MULTICAST_IF, ChannelOption.IP_MULTICAST_TTL,
                ChannelOption.IP_TOS, ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, ChannelOption.UDP_GRO,
                ChannelOption.UDP_MAX_DATAGRAM_PAYLOAD_SIZE, ChannelOption.IP_RECVORIGDSTADDR, ChannelOption.IP_FREEBIND,
                ChannelOption.SO_REUSEPORT })
                if (TryGetValue(option, out var value)) options[option] = value;
            return options;
        }
        /**
     * Gets the {@link StandardSocketOptions#IP_MULTICAST_IF} option.
     */
        internal NetworkInterface GetNetworkInterface()
        {
            if (_socket.AddressFamily == AddressFamily.InterNetwork)
            {
                var address = new IPAddress(_socket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, 4));
                return address.Equals(IPAddress.Any) ? null : FindInterface(address);
            }
            int index = (int)_socket.GetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface);
            return index == 0 ? null : NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Supports(NetworkInterfaceComponent.IPv6) && n.GetIPProperties().GetIPv6Properties()?.Index == index);
        }
        /**
     * Sets the {@link StandardSocketOptions#IP_MULTICAST_IF} option.
     */
        private void SetNetworkInterface(NetworkInterface networkInterface)
        {
            ArgumentNullException.ThrowIfNull(networkInterface);
            if (_socket.AddressFamily == AddressFamily.InterNetwork)
                _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                    InterfaceAddress(networkInterface, AddressFamily.InterNetwork).GetAddressBytes());
            else
            {
                _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface,
                    InterfaceIndex(networkInterface, AddressFamily.InterNetworkV6));
                if (_linuxDualMode && networkInterface.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(info => info.Address.AddressFamily == AddressFamily.InterNetwork) is { } ipv4)
                    _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, ipv4.Address.GetAddressBytes());
            }
        }
        protected override void AutoReadCleared() => ((AbstractChannel)Channel).ClearReadPending();
        private readonly Socket _socket;
        private readonly bool _linuxDualMode;
        internal DatagramConfig(DatagramChannel channel, Socket socket) : base(channel, new FixedRecvByteBufAllocator(2048))
        {
            _socket = socket;
            _linuxDualMode = channel._linuxDualMode && Socket.OSSupportsIPv4;
        }
        protected override bool TryGetValue(ChannelOption option, out object value)
        {
            /**
     * Gets the {@link StandardSocketOptions#SO_BROADCAST} option.
     */
            if (ReferenceEquals(option, ChannelOption.SO_BROADCAST))
                value = _socket.EnableBroadcast;
            /**
     * Gets the {@link StandardSocketOptions#SO_RCVBUF} option.
     */
            else if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
                value = _socket.ReceiveBufferSize;
            /**
     * Gets the {@link StandardSocketOptions#SO_SNDBUF} option.
     */
            else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
                value = _socket.SendBufferSize;
            /**
     * Gets the {@link StandardSocketOptions#SO_REUSEADDR} option.
     */
            else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
                value = (int)_socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) != 0;
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_IF))
                value = GetNetworkInterface();
            /**
     * Gets the address of the network interface used for multicast packets.
     */
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_ADDR))
            {
                var networkInterface = GetNetworkInterface();
                value = networkInterface == null ? null : InterfaceAddress(networkInterface, _socket.AddressFamily);
            }
            /**
     * Gets the {@link StandardSocketOptions#IP_TOS} option.
     */
            else if (ReferenceEquals(option, ChannelOption.IP_TOS))
                value = GetTrafficClass();
            else if (ReferenceEquals(option, ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION))
                value = ActiveOnRegistration;
            else if (ReferenceEquals(option, ChannelOption.UDP_GRO))
                value = Volatile.Read(ref ((DatagramChannel)Channel)._udpGro);
            else if (ReferenceEquals(option, ChannelOption.UDP_MAX_DATAGRAM_PAYLOAD_SIZE))
                value = Volatile.Read(ref ((DatagramChannel)Channel)._udpSlotSize);
            /**
     * Returns {@code true} if <a href="https://man7.org/linux/man-pages/man7/ip.7.html">IP_RECVORIGDSTADDR</a> is
     * enabled, {@code false} otherwise.
     */
            else if (ReferenceEquals(option, ChannelOption.IP_RECVORIGDSTADDR))
                value = Volatile.Read(ref ((DatagramChannel)Channel)._receiveOriginalDestination);
            /**
     * Returns {@code true} if <a href="https://man7.org/linux/man-pages/man7/ip.7.html">IP_FREEBIND</a> is enabled,
     * {@code false} otherwise.
     */
            else if (ReferenceEquals(option, ChannelOption.IP_FREEBIND) && OperatingSystem.IsLinux())
                value = LinuxSocketOptions.GetFlag(_socket, (int)SocketOptionLevel.IP, LinuxSocketOptions.IpFreeBind);
            else if (ReferenceEquals(option, ChannelOption.SO_REUSEPORT) && OperatingSystem.IsLinux())
                value = LinuxSocketOptions.GetFlag(_socket, LinuxSocketOptions.SolSocket, LinuxSocketOptions.SoReusePort);
            /**
     * Gets the {@link StandardSocketOptions#IP_MULTICAST_TTL} option.
     */
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_TTL))
                value = (int)_socket.GetSocketOption(MulticastOptionLevel, SocketOptionName.MulticastTimeToLive);
            /**
     * Gets the {@link StandardSocketOptions#IP_MULTICAST_LOOP} option.
     *
     * @return {@code true} if and only if the loopback mode has been disabled
     */
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_LOOP_DISABLED))
                value = !_socket.MulticastLoopback;
            else
                return base.TryGetValue(option, out value);
            return true;
        }
        protected override bool SetValue(ChannelOption option, object value)
        {
            /**
     * Sets the {@link StandardSocketOptions#SO_BROADCAST} option.
     */
            if (ReferenceEquals(option, ChannelOption.SO_BROADCAST))
                _socket.EnableBroadcast = (bool)value;
            /**
     * Sets the {@link StandardSocketOptions#SO_RCVBUF} option.
     */
            else if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
                _socket.ReceiveBufferSize = (int)value;
            /**
     * Sets the {@link StandardSocketOptions#SO_SNDBUF} option.
     */
            else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
                _socket.SendBufferSize = (int)value;
            /**
     * Gets the {@link StandardSocketOptions#SO_REUSEADDR} option.
     */
            else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, (bool)value);
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_IF))
                SetNetworkInterface((NetworkInterface)value);
            /**
     * Sets the address of the network interface used for multicast packets.
     */
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_ADDR))
                SetNetworkInterface(FindInterface((IPAddress)value) ?? throw new ArgumentException("Address does not belong to a local network interface.", nameof(value)));
            /**
     * Sets the {@link StandardSocketOptions#IP_TOS} option.
     */
            else if (ReferenceEquals(option, ChannelOption.IP_TOS))
            {
                int trafficClass = (int)value;
                ArgumentOutOfRangeException.ThrowIfNegative(trafficClass);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(trafficClass, 255);
                SetTrafficClass(trafficClass);
            }
            else if (ReferenceEquals(option, ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION))
            {
                if (Channel.IsRegistered)
                    throw new InvalidOperationException("Can only change before channel is registered.");
                ActiveOnRegistration = (bool)value;
            }
            else if (ReferenceEquals(option, ChannelOption.UDP_GRO))
                ((DatagramChannel)Channel).SetUdpGro((bool)value);
            else if (ReferenceEquals(option, ChannelOption.UDP_MAX_DATAGRAM_PAYLOAD_SIZE))
                ((DatagramChannel)Channel).SetUdpSlotSize((int)value);
            else if (ReferenceEquals(option, ChannelOption.IP_RECVORIGDSTADDR))
                ((DatagramChannel)Channel).SetReceiveOriginalDestination((bool)value);
            /**
     * If {@code true} is used <a href="https://man7.org/linux/man-pages/man7/ip.7.html">IP_FREEBIND</a> is enabled,
     * {@code false} for disable it. Default is disabled.
     */
            else if (ReferenceEquals(option, ChannelOption.IP_FREEBIND) && OperatingSystem.IsLinux())
                LinuxSocketOptions.SetFlag(_socket, (int)SocketOptionLevel.IP, LinuxSocketOptions.IpFreeBind, (bool)value);
            else if (ReferenceEquals(option, ChannelOption.SO_REUSEPORT) && OperatingSystem.IsLinux())
                LinuxSocketOptions.SetFlag(_socket, LinuxSocketOptions.SolSocket, LinuxSocketOptions.SoReusePort, (bool)value);
            /**
     * Sets the {@link StandardSocketOptions#IP_MULTICAST_TTL} option.
     */
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_TTL))
            {
                int ttl = (int)value;
                ArgumentOutOfRangeException.ThrowIfNegative(ttl);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(ttl, 255);
                _socket.SetSocketOption(MulticastOptionLevel, SocketOptionName.MulticastTimeToLive, ttl);
                SetMappedIPv4Option(SocketOptionName.MulticastTimeToLive, ttl);
            }
            /**
     * Sets the {@link StandardSocketOptions#IP_MULTICAST_LOOP} option.
     *
     * @param loopbackModeDisabled
     *        {@code true} if and only if the loopback mode has been disabled
     */
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_LOOP_DISABLED))
            {
                bool enabled = !(bool)value;
                _socket.MulticastLoopback = enabled;
                SetMappedIPv4Option(SocketOptionName.MulticastLoopback, enabled ? 1 : 0);
            }
            else
                return base.SetValue(option, value);
            return true;
        }
        // Linux's IPv6 traffic class is IPV6_TCLASS, which has no SocketOptionName mapping.
        // https://github.com/torvalds/linux/blob/master/include/uapi/linux/in6.h
        private const int LinuxIPv6TrafficClass = 67;

        private int GetTrafficClass()
        {
            if (OperatingSystem.IsLinux() && _socket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Span<byte> value = stackalloc byte[sizeof(int)];
                int written = _socket.GetRawSocketOption((int)SocketOptionLevel.IPv6, LinuxIPv6TrafficClass, value);
                return BitConverter.ToInt32(value[..written]);
            }
            return (int)_socket.GetSocketOption(MulticastOptionLevel, SocketOptionName.TypeOfService);
        }

        private void SetTrafficClass(int trafficClass)
        {
            if (OperatingSystem.IsLinux() && _socket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Span<byte> value = stackalloc byte[sizeof(int)];
                BitConverter.TryWriteBytes(value, trafficClass);
                _socket.SetRawSocketOption((int)SocketOptionLevel.IPv6, LinuxIPv6TrafficClass, value);
                SetMappedIPv4Option(SocketOptionName.TypeOfService, trafficClass);
            }
            else
                _socket.SetSocketOption(MulticastOptionLevel, SocketOptionName.TypeOfService, trafficClass);
        }

        private void SetMappedIPv4Option(SocketOptionName option, int value)
        {
            if (_linuxDualMode) _socket.SetSocketOption(SocketOptionLevel.IP, option, value);
        }

        private SocketOptionLevel MulticastOptionLevel => _socket.AddressFamily == AddressFamily.InterNetworkV6
            ? SocketOptionLevel.IPv6 : SocketOptionLevel.IP;
    }
}
