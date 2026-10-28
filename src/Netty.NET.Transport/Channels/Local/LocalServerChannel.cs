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
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels.Local;

/**
 * A {@link ServerChannel} for the local transport which allows in VM communication.
 */
public class LocalServerChannel : AbstractChannel, IServerChannel
{
    private readonly Queue<LocalChannel> _inbound = new();
    private LocalAddress _local;
    private bool _acceptPending;
    public LocalServerChannel() : base(null, new ChannelMetadata(false, 16)) { }
    protected override IChannelConfig NewConfig() => new ServerConfig(this);
    public override EndPoint LocalAddress => _local;
    public override EndPoint RemoteAddress => null;
    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); _local = LocalChannelRegistry.Register(this, _local, address); SetActive(true); return Task.CompletedTask; }
    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken token) => Task.FromException(new NotSupportedException("Server channels cannot connect."));
    protected override Task DoWriteAsync(object message) => Task.FromException(new NotSupportedException("Server channels cannot write."));
    protected override object FilterOutboundMessage(object message) => throw new NotSupportedException("Server channels cannot write.");
    protected override void DoClose()
    {
        LocalChannelRegistry.Unregister(_local);
        _local = null;
        // Close connections that were queued but never accepted, so their connect attempt fails instead of
        // never completing.
        while (_inbound.TryDequeue(out var child))
            child.CloseForcibly();
    }
    /**
     * A factory method for {@link LocalChannel}s. Users may override it
     * to create custom instances of {@link LocalChannel}s.
     */
    protected virtual LocalChannel NewLocalChannel(LocalChannel peer) => new(this, peer);
    internal LocalChannel Serve(LocalChannel peer)
    {
        var child = NewLocalChannel(peer);
        EventLoop.Execute(() =>
        {
            if (!IsOpen)
            { child.CloseForcibly(); return; }
            _inbound.Enqueue(child);
            if (_acceptPending)
                DoBeginRead();
        });
        return child;
    }
    protected override void DoBeginRead()
    {
        if (_inbound.Count == 0)
        { _acceptPending = true; return; }
        _acceptPending = false;
        ReceiveHandle.Reset(Config);
        while (_inbound.TryDequeue(out var child))
        {
            ReceiveHandle.IncMessagesRead(1);
            Pipeline.FireChannelRead(child);
            if (!ReceiveHandle.ContinueReading())
                break;
        }
        ReceiveHandle.ReadComplete();
        Pipeline.FireChannelReadComplete();
        if (IsOpen && Config.IsAutoRead())
        { if (_inbound.Count != 0) EventLoop.Execute(DoBeginRead); else _acceptPending = true; }
    }
    private sealed class ServerConfig : DefaultChannelConfig
    {
        internal ServerConfig(IChannel channel) : base(channel, new ServerChannelRecvByteBufAllocator())
        { SetAllocator(new PreferHeapByteBufAllocator(GetAllocator())); }
    }
}
