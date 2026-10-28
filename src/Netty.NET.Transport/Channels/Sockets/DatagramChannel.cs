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
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;

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
    private bool _reading;
    private bool _readRequested;
    public DatagramChannel() : this(AddressFamily.InterNetwork) { }
    public DatagramChannel(AddressFamily family) : base(null, new ChannelMetadata(true))
    { _socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp); }
    public override EndPoint LocalAddress => IsOpen ? TcpSocketChannel.Normalize(_socket.LocalEndPoint) : null;
    public override EndPoint RemoteAddress => IsOpen && _socket.Connected ? TcpSocketChannel.Normalize(_socket.RemoteEndPoint) : null;
    protected override IChannelConfig NewConfig() => new DatagramConfig(this, _socket);
    protected override Task DoBindAsync(EndPoint address, CancellationToken token)
    { token.ThrowIfCancellationRequested(); _socket.Bind(address); SetActive(true); return Task.CompletedTask; }
    protected override async Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken token)
    {
        if (local != null)
            _socket.Bind(local);
        await _socket.ConnectAsync(remote, token).ConfigureAwait(false);
        await OnLoopAsync(() => { EnsureOpen(); SetActive(true); }).ConfigureAwait(false);
    }
    protected override Task DoDisconnectAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); _socket.Disconnect(true); SetActive(_socket.IsBound); return Task.CompletedTask; }
    protected override void DoClose() => _socket.Dispose();
    protected override object FilterOutboundMessage(object message)
    {
        if (message is DatagramPacket or ByteBuf or IAddressedEnvelope<ByteBuf, IPEndPoint>)
            return message;
        throw new NotSupportedException("UDP writes require a ByteBuf or an addressed ByteBuf envelope.");
    }
    protected override async Task DoWriteAsync(object message)
    {
        var envelope = message as IAddressedEnvelope<ByteBuf, IPEndPoint>;
        ByteBuf data = envelope?.Content ?? (ByteBuf)message;
        int length = data.ReadableBytes;
        var sequence = data.ReadableSequence;
        byte[] staging = null;
        try
        {
            ReadOnlyMemory<byte> memory = sequence.First;
            if (!sequence.IsSingleSegment)
            {
                // A datagram must be sent atomically. As in the original composite-buffer copy path,
                // gather a segmented payload into a contiguous borrow rather than sending several datagrams.
                staging = ArrayPool<byte>.Shared.Rent(length);
                sequence.CopyTo(staging);
                memory = staging.AsMemory(0, length);
            }
            int sent = envelope?.Recipient is { } target
                ? await _socket.SendToAsync(memory, SocketFlags.None, target).ConfigureAwait(false)
                : await _socket.SendAsync(memory, SocketFlags.None).ConfigureAwait(false);
            if (sent != length)
                throw new IOException("A UDP datagram was only partially accepted.");
            await OnLoopAsync(() => OutboundBuffer.Progress(sent)).ConfigureAwait(false);
        }
        finally { if (staging != null) ArrayPool<byte>.Shared.Return(staging); }
    }
    protected override void DoBeginRead()
    {
        _readRequested = true;
        if (_reading)
            return;
        _reading = true;
        _ = ReadLoopAsync();
    }
    private async Task ReadLoopAsync()
    {
        try
        {
            bool again = true;
            while (again && IsOpen)
            {
                ByteBuf data = null;
                try
                {
                    await OnLoopAsync(() =>
                    {
                        if (!IsOpen)
                            return;
                        ReceiveHandle.Reset(Config);
                        data = ReceiveHandle.Allocate(Allocator);
                        ReceiveHandle.AttemptedBytesRead(data.WritableBytes);
                        _readRequested = false;
                    }).ConfigureAwait(false);
                    if (data == null)
                        return;
                    EndPoint any = _socket.AddressFamily == AddressFamily.InterNetworkV6 ? new IPEndPoint(IPAddress.IPv6Any, 0) : new IPEndPoint(IPAddress.Any, 0);
                    var result = await ReceiveDatagramAsync(data.AsMemory(data.WriterIndex, data.WritableBytes), any).ConfigureAwait(false);
                    await OnLoopAsync(() =>
                    {
                        if (!IsOpen)
                            return;
                        ReceiveHandle.LastBytesRead(result.ReceivedBytes);
                        ReceiveHandle.IncMessagesRead(1);
                        data.WriterIndex += result.ReceivedBytes;
                        var packet = new DatagramPacket(data, (IPEndPoint)LocalAddress, (IPEndPoint)TcpSocketChannel.Normalize(result.RemoteEndPoint));
                        data = null;
                        Pipeline.FireChannelRead(packet);
                        ReceiveHandle.ReadComplete();
                        Pipeline.FireChannelReadComplete();
                        again = IsOpen && (Config.IsAutoRead() || _readRequested);
                    }).ConfigureAwait(false);
                }
                finally { data?.Release(); }
            }
        }
        catch (Exception error)
        {
            try
            { await OnLoopAsync(() => { if (IsOpen) Pipeline.FireExceptionCaught(error); }).ConfigureAwait(false); }
            catch { }
        }
        finally
        {
            try
            { await OnLoopAsync(() => { _reading = false; if (IsOpen && _readRequested) DoBeginRead(); }).ConfigureAwait(false); }
            catch { }
        }
    }
    private async Task<SocketReceiveFromResult> ReceiveDatagramAsync(Memory<byte> memory, EndPoint remote)
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
    public Task JoinGroupAsync(IPAddress multicastAddress, IPAddress interfaceAddress = null) => OnLoopAsync(() =>
    {
        ArgumentNullException.ThrowIfNull(multicastAddress);
        if (multicastAddress.AddressFamily != AddressFamily.InterNetwork)
            throw new NotSupportedException("Use the IPv6 interface-index overload.");
        _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(multicastAddress, interfaceAddress ?? IPAddress.Any));
    });
    public Task LeaveGroupAsync(IPAddress multicastAddress, IPAddress interfaceAddress = null) => OnLoopAsync(() =>
    { _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership, new MulticastOption(multicastAddress, interfaceAddress ?? IPAddress.Any)); });
    public Task JoinGroupAsync(IPAddress multicastAddress, long interfaceIndex) => OnLoopAsync(() =>
    { _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(multicastAddress, interfaceIndex)); });
    public Task LeaveGroupAsync(IPAddress multicastAddress, long interfaceIndex) => OnLoopAsync(() =>
    { _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.DropMembership, new IPv6MulticastOption(multicastAddress, interfaceIndex)); });
    private sealed class DatagramConfig : DefaultChannelConfig
    {
        private readonly Socket _socket;
        internal DatagramConfig(IChannel channel, Socket socket) : base(channel, new FixedRecvByteBufAllocator(2048)) { _socket = socket; }
        protected override bool TryGetValue(ChannelOption option, out object value)
        {
            if (ReferenceEquals(option, ChannelOption.SO_BROADCAST))
                value = _socket.EnableBroadcast;
            else if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
                value = _socket.ReceiveBufferSize;
            else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
                value = _socket.SendBufferSize;
            else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
                value = (int)_socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) != 0;
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_TTL))
                value = (int)_socket.Ttl;
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_LOOP_DISABLED))
                value = !_socket.MulticastLoopback;
            else
                return base.TryGetValue(option, out value);
            return true;
        }
        protected override bool SetValue(ChannelOption option, object value)
        {
            if (ReferenceEquals(option, ChannelOption.SO_BROADCAST))
                _socket.EnableBroadcast = (bool)value;
            else if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
                _socket.ReceiveBufferSize = (int)value;
            else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
                _socket.SendBufferSize = (int)value;
            else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, (bool)value);
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_TTL))
            { ArgumentOutOfRangeException.ThrowIfNegative((int)value); _socket.Ttl = checked((short)(int)value); }
            else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_LOOP_DISABLED))
                _socket.MulticastLoopback = !(bool)value;
            else
                return base.SetValue(option, value);
            return true;
        }
    }
}
