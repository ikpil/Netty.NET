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
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels.Local;

/**
 * A {@link ServerChannel} for the local transport which allows in VM communication.
 */
public class LocalServerChannel : AbstractServerChannel
{
    private readonly Queue<LocalChannel> _inbound = new();
    private volatile int state; // 0 - open, 1 - active, 2 - closed
    private volatile LocalAddress _local;
    private bool _acceptPending;
    private IIoRegistration _registration;
    private readonly LocalServerUnsafe _unsafe;
    public LocalServerChannel()
    {
        _unsafe = new LocalServerUnsafe(this);
    }

    protected override bool IsCompatible(IEventLoop loop)
    {
        return loop is SingleThreadEventLoop ||
                (loop is IIoEventLoop ioEventLoop && ioEventLoop.IsCompatible(typeof(LocalServerUnsafe)));
    }

    // CLR: doRegister(ChannelPromise) runs on the event loop, where IoEventLoop.register(IoHandle) completes
    // synchronously; the completed Task replaces the future listener.
    protected override void DoRegister()
    {
        IEventLoop loop = EventLoop;
        if (loop is IIoEventLoop ioEventLoop)
        {
            System.Diagnostics.Debug.Assert(_registration == null);
            _registration = ioEventLoop.RegisterAsync(_unsafe).GetAwaiter().GetResult();
        }
        else
        {
            _unsafe.Registered();
        }
    }

    protected override void DoDeregister()
    {
        IEventLoop loop = EventLoop;
        if (loop is IIoEventLoop)
        {
            IIoRegistration registration = _registration;
            if (registration != null)
            {
                _registration = null;
                registration.Cancel();
            }
        }
        else
        {
            _unsafe.Unregistered();
        }
    }

    // CLR: AbstractUnsafe is folded into AbstractChannel; LocalServerUnsafe is the registered LocalIoHandle.
    private sealed class LocalServerUnsafe : ILocalIoHandle
    {
        private readonly LocalServerChannel _channel;
        private readonly Action _shutdownHook;

        internal LocalServerUnsafe(LocalServerChannel channel)
        {
            _channel = channel;
            _shutdownHook = CloseNow;
        }

        public void Close()
        {
            _ = _channel.Close();
        }

        public void Handle(IIoRegistration registration, IIoEvent @event)
        {
            // NOOP
        }

        public void Registered()
        {
            IEventLoop loop = _channel.EventLoop;
            if (!(loop is IIoEventLoop) && loop is SingleThreadEventExecutor executor)
            {
                executor.AddShutdownHook(_shutdownHook);
            }
        }

        public void Unregistered()
        {
            IEventLoop loop = _channel.EventLoop;
            if (!(loop is IIoEventLoop) && loop is SingleThreadEventExecutor executor)
            {
                executor.RemoveShutdownHook(_shutdownHook);
            }
        }

        public void CloseNow()
        {
            _ = _channel.Close();
        }
    }
    protected override IChannelConfig NewConfig()
    {
        return new ServerConfig(this);
    }

    // CLR: AbstractChannel keeps its own lifecycle flags for the shared close/registration paths; the source state
    // field is combined with them.
    public override bool IsOpen => state < 2 && base.IsOpen;

    public override bool IsActive => state == 1 && base.IsActive;
    protected override EndPoint LocalAddress0()
    {
        return _local;
    }

    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _local = LocalChannelRegistry.Register(this, _local, address);
        state = 1;
        SetActive(true);
        return Task.CompletedTask;
    }

    protected override void DoClose()
    {
        if (state <= 1)
        {
            // Update all internal state before the closeFuture is notified.
            if (_local != null)
            {
                LocalChannelRegistry.Unregister(_local);
                _local = null;
            }
            state = 2;
        }
        // Close connections that were queued but never accepted, so their connect attempt fails instead of
        // never completing.
        while (_inbound.TryDequeue(out var child))
            child.CloseForcibly();
    }

    /**
     * A factory method for {@link LocalChannel}s. Users may override it
     * to create custom instances of {@link LocalChannel}s.
     */
    protected virtual LocalChannel NewLocalChannel(LocalChannel peer)
    {
        return new(this, peer);
    }

    internal LocalChannel Serve(LocalChannel peer)
    {
        LocalChannel child = NewLocalChannel(peer);
        if (EventLoop.InEventLoop())
        {
            Serve0(child);
        }
        else
        {
            EventLoop.Execute(() => Serve0(child));
        }
        return child;
    }

    protected override void DoBeginRead()
    {
        if (_acceptPending)
        {
            return;
        }

        Queue<LocalChannel> inboundBuffer = _inbound;
        if (inboundBuffer.Count == 0)
        {
            _acceptPending = true;
            return;
        }

        ReadInbound();
    }

    private void ReadInbound()
    {
        IRecvByteBufAllocator.IHandle handle = RecvBufAllocHandle();
        handle.Reset(Config);
        IChannelPipeline pipeline = Pipeline;
        do
        {
            if (!_inbound.TryDequeue(out LocalChannel m))
            {
                break;
            }
            handle.IncMessagesRead(1);
            pipeline.FireChannelRead(m);
        } while (handle.ContinueReading());
        handle.ReadComplete();
        pipeline.FireChannelReadComplete();
    }

    private void Serve0(LocalChannel child)
    {
        if (!IsOpen)
        {
            child.CloseForcibly();
            return;
        }
        _inbound.Enqueue(child);
        if (_acceptPending)
        {
            _acceptPending = false;
            ReadInbound();
        }
    }

    private sealed class ServerConfig : DefaultChannelConfig
    {
        internal ServerConfig(IChannel channel) : base(channel, new ServerChannelRecvByteBufAllocator())
        {
            SetAllocator(new PreferHeapByteBufAllocator(GetAllocator()));
        }
    }
}
