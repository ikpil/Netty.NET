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
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;

namespace Netty.NET.Transport.Channels.Local;

/// <summary>In-process peer transport with separate event-loop ownership and queued message reference counts.</summary>
/**
 * A {@link Channel} for the local transport.
 */
public class LocalChannel : AbstractChannel
{
    private readonly ConcurrentQueue<object> _inbound = new();
    private LocalChannel _peer;
    private LocalAddress _local;
    private LocalAddress _remote;
    private readonly TaskCompletionSource _connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _readPending;
    private bool _reading;
    private bool _wrote;
    private bool _connecting;
    public LocalChannel() : base(null, new ChannelMetadata(false)) { }
    protected internal LocalChannel(LocalServerChannel parent, LocalChannel peer) : base(parent, new ChannelMetadata(false))
    { _peer = peer; _local = (LocalAddress)parent.LocalAddress; _remote = (LocalAddress)peer.LocalAddress; }
    protected override IChannelConfig NewConfig()
    { var config = new DefaultChannelConfig(this); config.SetAllocator(new PreferHeapByteBufAllocator(config.GetAllocator())); return config; }
    public override EndPoint LocalAddress => _local;
    public override EndPoint RemoteAddress => _remote;
    protected override void DoRegister()
    {
        if (Parent != null)
        {
            var peer = _peer;
            if (peer == null || !peer.IsOpen)
                throw new IOException("Connection refused: client closed.");
            SetActive(true);
            peer.EventLoop.Execute(() =>
            {
                if (!peer.IsOpen || !IsOpen)
                { peer._connection.TrySetException(new IOException("Connection refused.")); return; }
                peer.SetActive(true);
                peer._connection.TrySetResult();
            });
        }
    }
    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); _local = LocalChannelRegistry.Register(this, _local, address); return Task.CompletedTask; }
    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        if (IsActive)
            return Task.FromException(new InvalidOperationException("Already connected."));
        if (_peer != null)
            return Task.FromException(new InvalidOperationException("Connection pending."));
        cancellationToken.ThrowIfCancellationRequested();
        _connecting = true;
        if (_local == null)
            _local = LocalChannelRegistry.Register(this, null, local ?? global::Netty.NET.Transport.Channels.Local.LocalAddress.ANY);
        if (LocalChannelRegistry.Get(remote) is not LocalServerChannel server || !server.IsActive)
            return Task.FromException(new SocketException((int)SocketError.ConnectionRefused));
        _remote = (LocalAddress)remote;
        _peer = server.Serve(this);
        return _connection.Task.WaitAsync(cancellationToken);
    }
    protected override void DoClose()
    {
        if (Parent == null)
            LocalChannelRegistry.Unregister(_local);
        _local = null;
        if (_connecting)
            _connection.TrySetException(new IOException("Channel is closed."));
        var peer = _peer;
        _peer = null;
        if (peer != null)
        {
            try
            {
                // Always queue, even on a shared loop: this channel's inactive event must precede its peer's.
                peer.EventLoop.Execute(() => { peer._connection.TrySetException(new IOException("Peer is closed.")); _ = peer.CloseTransportAsync(); });
            }
            catch (InvalidOperationException) { peer.CloseForcibly(); }
        }
        while (_inbound.TryDequeue(out var message))
            ReferenceCountUtil.Release(message);
    }
    protected override Task DoWriteAsync(object message)
    {
        var peer = _peer;
        if (peer == null || !peer.IsOpen)
            return Task.FromException(new IOException("Peer is closed."));
        ReferenceCountUtil.Retain(message);
        peer._inbound.Enqueue(message);
        _wrote = true;
        return Task.CompletedTask;
    }
    protected override void DoWriteComplete()
    {
        var peer = _peer;
        if (!_wrote || peer == null)
            return;
        _wrote = false;
        try
        { peer.EventLoop.Execute(peer.FinishPeerRead); }
        catch { _ = CloseTransportAsync(); peer.CloseForcibly(); throw; }
    }
    private void FinishPeerRead()
    {
        if (!IsOpen)
        { while (_inbound.TryDequeue(out var message)) ReferenceCountUtil.Release(message); return; }
        if (_readPending && IsActive && IsRegistered)
            ReadInbound();
    }
    protected override void DoBeginRead()
    {
        _readPending = true;
        if (_reading || _inbound.IsEmpty)
            return;
        ReadInbound();
    }
    private void ReadInbound()
    {
        if (_reading || !IsOpen)
            return;
        _reading = true;
        _readPending = false;
        try
        {
            var handle = ReceiveHandle;
            handle.Reset(Config);
            while (_inbound.TryDequeue(out var received))
            {
                if (received is ByteBuf buffer && _inbound.TryPeek(out var next) && next is ByteBuf)
                {
                    ByteBuf output = handle.Allocate(Allocator);
                    if (buffer.ReadableBytes < output.WritableBytes)
                    {
                        // We have an opportunity to coalesce buffers.
                        output.WriteBytes(buffer.ReadableMemory.Span);
                        buffer.Release();
                        while (_inbound.TryPeek(out next) && next is ByteBuf more && more.ReadableBytes < output.WritableBytes)
                        {
                            _inbound.TryDequeue(out _);
                            output.WriteBytes(more.ReadableMemory.Span);
                            more.Release();
                        }
                        handle.LastBytesRead(output.ReadableBytes);
                        received = output;
                    }
                    else
                    {
                        // It won't be profitable to coalesce buffers this time around.
                        handle.LastBytesRead(output.Capacity);
                        output.Release();
                    }
                }
                handle.IncMessagesRead(1);
                Pipeline.FireChannelRead(received);
                if (!handle.ContinueReading())
                    break;
            }
            handle.ReadComplete();
            Pipeline.FireChannelReadComplete();
        }
        finally { _reading = false; }
        if (IsOpen && IsActive && !_inbound.IsEmpty && (Config.IsAutoRead() || _readPending))
            EventLoop.Execute(DoBeginRead); // Bound reader recursion instead of consuming a peer's stack.
        else if (IsOpen && Config.IsAutoRead())
            _readPending = true;
    }
}
