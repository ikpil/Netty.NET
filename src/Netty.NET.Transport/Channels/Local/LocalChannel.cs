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
    private readonly object _inboundGate = new();
    private bool _inboundClosed;
    // The source's package-visible inboundBuffer is observed by its original close-ownership test.
    internal bool IsInboundBufferEmpty => _inbound.IsEmpty;
    private LocalChannel _peer;
    private LocalAddress _local;
    private LocalAddress _remote;
    private readonly TaskCompletionSource _connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _readPending;
    private bool _reading;
    private bool _wrote;
    private volatile bool _writeInProgress;
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
                throw new SocketException((int)SocketError.ConnectionRefused);
            // Set our state to connected before the peer's channelActive is fired, otherwise a write operation that
            // is triggered by the peer's channelActive will raise a NotYetConnectedException.
            SetActive(true);
            peer.EventLoop.Execute(() =>
            {
                if (!peer.IsOpen || !IsOpen)
                { peer.CloseFromPeer(); return; }
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
        return FinishConnectAsync(cancellationToken);
    }
    private async Task FinishConnectAsync(CancellationToken cancellationToken)
    {
        try
        { await _connection.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException canceled)
        {
            // CLR cancellation aborts this connect, whereas Java makes its started connect promise uncancellable.
            // Close on the owner so a queued accepted peer cannot reactivate a client whose Task was cancelled.
            await OnLoopAsync(() =>
            {
                _connection.TrySetCanceled(canceled.CancellationToken);
                _ = CloseTransportAsync();
            }).ConfigureAwait(false);
            throw;
        }
    }
    protected override void DoClose()
    {
        // CLR: concurrent queue operations are individually safe, but accepting a retained reference and
        // sealing/draining the receiver must form a single ownership boundary. User retain/release callbacks
        // run outside this short gate.
        lock (_inboundGate)
            _inboundClosed = true;
        try
        {
            if (Parent == null)
                LocalChannelRegistry.Unregister(_local);
            _local = null;
            var peer = _peer;
            // Preserve order of event and force a read operation now before the close operation is processed.
            if (_writeInProgress && peer != null)
                FinishPeerRead(peer);
            if (_connecting)
            {
                // Use tryFailure() instead of setFailure() to avoid the race against cancel().
                // CLR: tolerate a connection producer already cancelled on its owner as well as a close race.
                _connection.TrySetException(new IOException("Channel is closed."));
            }
            _peer = null;
            // The accepted channel did not complete the connection. Don't touch it: it may not have an event
            // loop yet and is owned by the server channel or its own event loop. When it is registered it sees
            // that this channel is closed and closes itself. If it was closed already, its close has notified
            // this channel or will do so.
            if (peer != null && (Parent != null || _connection.Task.IsCompletedSuccessfully))
            {
                try
                {
                    // Always call peer.eventLoop().execute() even if peer.eventLoop().inEventLoop() is true.
                    // This ensures that if both channels are on the same event loop, the peer's channelInActive
                    // event is triggered *after* this peer's channelInActive event
                    peer.EventLoop.Execute(peer.CloseFromPeer);
                }
                catch (InvalidOperationException) { peer.CloseForcibly(); }
            }
        }
        finally
        {
            // We need to release all the buffers that may be put into our inbound queue since we closed the Channel
            // to ensure we not leak any memory. This is fine as it basically gives the same guarantees as TCP which
            // means even if the promise was notified before its not really guaranteed that the "remote peer" will
            // see the buffer at all.
            _readPending = false;
            while (_inbound.TryDequeue(out var message))
                ReferenceCountUtil.Release(message);
        }
    }
    private void CloseFromPeer()
    {
        if (_connecting && !_connection.Task.IsCompleted)
        {
            // The accepted channel was closed before the connection was established, for example because the server
            // channel was closed. It is closed already, so don't notify it again. Fail the connect like a refused
            // connection and close this channel.
            _connection.TrySetException(new SocketException((int)SocketError.ConnectionRefused));
            _peer = null;
        }
        if (IsOpen)
            _ = CloseTransportAsync();
        else
            while (_inbound.TryDequeue(out var message))
                ReferenceCountUtil.Release(message);
    }
    protected override Task DoWriteAsync(object message)
    {
        _writeInProgress = true;
        var peer = _peer;
        if (peer == null || !peer.IsOpen)
            return Task.FromException(new IOException("Peer is closed."));
        ReferenceCountUtil.Retain(message);
        bool accepted;
        lock (peer._inboundGate)
        {
            accepted = IsOpen && peer.IsOpen && !peer._inboundClosed;
            if (accepted)
                peer._inbound.Enqueue(message);
        }
        if (!accepted)
        {
            ReferenceCountUtil.Release(message);
            return Task.FromException(new IOException("Peer is closed."));
        }
        _wrote = true;
        return Task.CompletedTask;
    }
    protected override void DoWriteComplete()
    {
        _writeInProgress = false;
        var peer = _peer;
        if (!_wrote || peer == null)
            return;
        _wrote = false;
        try
        { FinishPeerRead(peer); }
        catch { _ = CloseTransportAsync(); peer.CloseForcibly(); throw; }
    }
    private void FinishPeerRead(LocalChannel peer)
    {
        // If the peer is also writing, then we must schedule the event on the event loop to preserve read order.
        if (ReferenceEquals(peer.EventLoop, EventLoop) && !peer._writeInProgress)
            peer.FinishPeerRead();
        else
            peer.EventLoop.Execute(peer.FinishPeerRead);
    }
    private void FinishPeerRead()
    {
        if (!IsOpen)
        { while (_inbound.TryDequeue(out var message)) ReferenceCountUtil.Release(message); return; }
        // We should only set readInProgress to false if there is any data that was read as otherwise we may miss to
        // forward data later on.
        if (_readPending && IsActive && IsRegistered && !_inbound.IsEmpty)
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
                        // CLR: indexed transfer handles segmented storage and preserves the source reader index.
                        output.WriteBytes(buffer, buffer.ReaderIndex, buffer.ReadableBytes);
                        buffer.Release();
                        while (_inbound.TryPeek(out next) && next is ByteBuf more && more.ReadableBytes < output.WritableBytes)
                        {
                            _inbound.TryDequeue(out _);
                            output.WriteBytes(more, more.ReaderIndex, more.ReadableBytes);
                            more.Release();
                        }
                        handle.LastBytesRead(output.ReadableBytes);
                        received = output; // Send the coalesced buffer down the pipeline.
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
