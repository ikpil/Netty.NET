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
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels;

/**
 *  Combines a {@link ChannelInboundHandler} and a {@link ChannelOutboundHandler} into one {@link ChannelHandler}.
 */
public class CombinedChannelDuplexHandler<TInbound, TOutbound> : ChannelDuplexHandler
    where TInbound : class, IChannelInboundHandler
    where TOutbound : class, IChannelOutboundHandler
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(CombinedChannelDuplexHandler<TInbound, TOutbound>));
    private DelegatingChannelHandlerContext _inboundCtx;
    private DelegatingChannelHandlerContext _outboundCtx;
    private volatile bool _handlerAdded;
    private TInbound _inboundHandler;
    private TOutbound _outboundHandler;

    /**
     * Creates a new uninitialized instance. A class that extends this handler must invoke
     * {@link #init(ChannelInboundHandler, ChannelOutboundHandler)} before adding this handler into a
     * {@link ChannelPipeline}.
     */
    protected CombinedChannelDuplexHandler()
    {
        EnsureNotSharable();
    }

    /**
     * Creates a new instance that combines the specified two handlers into one.
     */
    public CombinedChannelDuplexHandler(TInbound inboundHandler, TOutbound outboundHandler)
    {
        EnsureNotSharable();
        Init(inboundHandler, outboundHandler);
    }

    /**
     * Initialized this handler with the specified handlers.
     *
     * @throws IllegalStateException if this handler was not constructed via the default constructor or
     *                               if this handler does not implement all required handler interfaces
     * @throws IllegalArgumentException if the specified handlers cannot be combined into one due to a conflict
     *                                  in the type hierarchy
     */
    protected void Init(TInbound inboundHandler, TOutbound outboundHandler)
    {
        if (_inboundHandler != null)
            throw new InvalidOperationException("Init() cannot be invoked more than once.");
        ArgumentNullException.ThrowIfNull(inboundHandler);
        ArgumentNullException.ThrowIfNull(outboundHandler);
        if (inboundHandler is IChannelOutboundHandler)
            throw new ArgumentException("The inbound handler must not implement IChannelOutboundHandler.", nameof(inboundHandler));
        if (outboundHandler is IChannelInboundHandler)
            throw new ArgumentException("The outbound handler must not implement IChannelInboundHandler.", nameof(outboundHandler));
        _inboundHandler = inboundHandler;
        _outboundHandler = outboundHandler;
    }

    protected TInbound InboundHandler => _inboundHandler;
    protected TOutbound OutboundHandler => _outboundHandler;

    private void CheckAdded()
    {
        if (!_handlerAdded)
            throw new InvalidOperationException("Handler not added to pipeline yet.");
    }

    /**
     * Removes the {@link ChannelInboundHandler} that was combined in this {@link CombinedChannelDuplexHandler}.
     */
    public void RemoveInboundHandler()
    {
        CheckAdded();
        _inboundCtx.Remove();
    }

    /**
     * Removes the {@link ChannelOutboundHandler} that was combined in this {@link CombinedChannelDuplexHandler}.
     */
    public void RemoveOutboundHandler()
    {
        CheckAdded();
        _outboundCtx.Remove();
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        if (_inboundHandler == null)
            throw new InvalidOperationException("Init() must be invoked before being added to a ChannelPipeline.");
        _outboundCtx = new DelegatingChannelHandlerContext(ctx, _outboundHandler);
        _inboundCtx = new InboundContext(ctx, _inboundHandler, this);
        // The inboundCtx and outboundCtx were created and set now it's safe to call removeInboundHandler() and
        // removeOutboundHandler().
        _handlerAdded = true;
        try { _inboundHandler.HandlerAdded(_inboundCtx); }
        finally { _outboundHandler.HandlerAdded(_outboundCtx); }
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        try { _inboundCtx.Remove(); }
        finally { _outboundCtx.Remove(); }
    }

    private void ForwardInboundException(Exception cause)
    {
        if (!_outboundCtx.Removed)
        {
            try
            {
                // We directly delegate to the ChannelOutboundHandler as this may override exceptionCaught(...)
                // as well
                _outboundHandler.ExceptionCaught(_outboundCtx, cause);
            }
            catch (Exception error)
            {
                if (Logger.IsDebugEnabled())
                    Logger.Debug("An exception was thrown by a user handler's ExceptionCaught() while handling the following exception:", cause);
                else if (Logger.IsWarnEnabled())
                    Logger.Warn("An exception '{}' [enable DEBUG level for full stacktrace] was thrown by a user handler's ExceptionCaught() while handling the following exception:", error, cause);
            }
        }
        else
            _outboundCtx.Ctx.FireExceptionCaught(cause);
    }

    public override void ChannelRegistered(IChannelHandlerContext ctx)
    {
        Debug.Assert(ReferenceEquals(ctx, _inboundCtx.Ctx));
        if (!_inboundCtx.Removed)
            _inboundHandler.ChannelRegistered(_inboundCtx);
        else
            _inboundCtx.FireChannelRegistered();
    }

    public override void ChannelUnregistered(IChannelHandlerContext ctx)
    {
        Debug.Assert(ReferenceEquals(ctx, _inboundCtx.Ctx));
        if (!_inboundCtx.Removed)
            _inboundHandler.ChannelUnregistered(_inboundCtx);
        else
            _inboundCtx.FireChannelUnregistered();
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        Debug.Assert(ReferenceEquals(ctx, _inboundCtx.Ctx));
        if (!_inboundCtx.Removed)
            _inboundHandler.ChannelActive(_inboundCtx);
        else
            _inboundCtx.FireChannelActive();
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        Debug.Assert(ReferenceEquals(ctx, _inboundCtx.Ctx));
        if (!_inboundCtx.Removed)
            _inboundHandler.ChannelInactive(_inboundCtx);
        else
            _inboundCtx.FireChannelInactive();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Debug.Assert(ReferenceEquals(ctx, _inboundCtx.Ctx));
        if (!_inboundCtx.Removed)
            _inboundHandler.ExceptionCaught(_inboundCtx, cause);
        else
            _inboundCtx.FireExceptionCaught(cause);
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        Debug.Assert(ReferenceEquals(ctx, _inboundCtx.Ctx));
        if (!_inboundCtx.Removed)
            _inboundHandler.UserEventTriggered(_inboundCtx, evt);
        else
            _inboundCtx.FireUserEventTriggered(evt);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        Debug.Assert(ReferenceEquals(ctx, _inboundCtx.Ctx));
        if (!_inboundCtx.Removed)
            _inboundHandler.ChannelRead(_inboundCtx, msg);
        else
            _inboundCtx.FireChannelRead(msg);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        Debug.Assert(ReferenceEquals(ctx, _inboundCtx.Ctx));
        if (!_inboundCtx.Removed)
            _inboundHandler.ChannelReadComplete(_inboundCtx);
        else
            _inboundCtx.FireChannelReadComplete();
    }

    public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
    {
        Debug.Assert(ReferenceEquals(ctx, _inboundCtx.Ctx));
        if (!_inboundCtx.Removed)
            _inboundHandler.ChannelWritabilityChanged(_inboundCtx);
        else
            _inboundCtx.FireChannelWritabilityChanged();
    }

    public override Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(ctx, _outboundCtx.Ctx));
        return !_outboundCtx.Removed
            ? _outboundHandler.BindAsync(_outboundCtx, localAddress, cancellationToken)
            : _outboundCtx.BindAsync(localAddress, cancellationToken);
    }

    public override Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(ctx, _outboundCtx.Ctx));
        return !_outboundCtx.Removed
            ? _outboundHandler.ConnectAsync(_outboundCtx, remoteAddress, localAddress, cancellationToken)
            : _outboundCtx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    public override Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(ctx, _outboundCtx.Ctx));
        return !_outboundCtx.Removed
            ? _outboundHandler.DisconnectAsync(_outboundCtx, cancellationToken)
            : _outboundCtx.DisconnectAsync(cancellationToken);
    }

    public override Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(ctx, _outboundCtx.Ctx));
        return !_outboundCtx.Removed
            ? _outboundHandler.CloseAsync(_outboundCtx, cancellationToken)
            : _outboundCtx.CloseAsync(cancellationToken);
    }

    public override Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(ctx, _outboundCtx.Ctx));
        return !_outboundCtx.Removed
            ? _outboundHandler.DeregisterAsync(_outboundCtx, cancellationToken)
            : _outboundCtx.DeregisterAsync(cancellationToken);
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(ctx, _outboundCtx.Ctx));
        return !_outboundCtx.Removed
            ? _outboundHandler.WriteAsync(_outboundCtx, msg, cancellationToken)
            : _outboundCtx.WriteAsync(msg, cancellationToken);
    }

    public override void Read(IChannelHandlerContext ctx)
    {
        Debug.Assert(ReferenceEquals(ctx, _outboundCtx.Ctx));
        if (!_outboundCtx.Removed)
            _outboundHandler.Read(_outboundCtx);
        else
            _outboundCtx.Read();
    }

    public override void Flush(IChannelHandlerContext ctx)
    {
        Debug.Assert(ReferenceEquals(ctx, _outboundCtx.Ctx));
        if (!_outboundCtx.Removed)
            _outboundHandler.Flush(_outboundCtx);
        else
            _outboundCtx.Flush();
    }

    private sealed class InboundContext : DelegatingChannelHandlerContext
    {
        private readonly CombinedChannelDuplexHandler<TInbound, TOutbound> _owner;
        internal InboundContext(IChannelHandlerContext ctx, IChannelHandler handler,
            CombinedChannelDuplexHandler<TInbound, TOutbound> owner) : base(ctx, handler)
        {
            _owner = owner;
        }

        public override IChannelInboundInvoker FireExceptionCaught(Exception cause)
        {
            _owner.ForwardInboundException(cause);
            return this;
        }
    }

    private class DelegatingChannelHandlerContext : IChannelHandlerContext
    {
        internal readonly IChannelHandlerContext Ctx;
        private readonly IChannelHandler _handler;
        internal bool Removed { get; private set; }
        internal DelegatingChannelHandlerContext(IChannelHandlerContext ctx, IChannelHandler handler)
        {
            Ctx = ctx;
            _handler = handler;
        }
        public IChannel Channel => Ctx.Channel;
        public IEventExecutor Executor => Ctx.Executor;
        public string Name => Ctx.Name;
        public IChannelHandler Handler => Ctx.Handler;
        public bool IsRemoved => Removed || Ctx.IsRemoved;
        public IChannelPipeline Pipeline => Ctx.Pipeline;
        public IByteBufAllocator Alloc()
        {
            return Ctx.Alloc();
        }

        public IAttribute<T> Attr<T>(AttributeKey<T> key) where T : class
        {
            return Ctx.Channel.Attr(key);
        }

        public bool HasAttr<T>(AttributeKey<T> key) where T : class
        {
            return Ctx.Channel.HasAttr(key);
        }

        public IChannelInboundInvoker FireChannelRegistered()
        {
            Ctx.FireChannelRegistered();
            return this;
        }

        public IChannelInboundInvoker FireChannelUnregistered()
        {
            Ctx.FireChannelUnregistered();
            return this;
        }

        public IChannelInboundInvoker FireChannelActive()
        {
            Ctx.FireChannelActive();
            return this;
        }

        public IChannelInboundInvoker FireChannelInactive()
        {
            Ctx.FireChannelInactive();
            return this;
        }

        public virtual IChannelInboundInvoker FireExceptionCaught(Exception cause)
        {
            Ctx.FireExceptionCaught(cause);
            return this;
        }

        public IChannelInboundInvoker FireUserEventTriggered(object @event)
        {
            Ctx.FireUserEventTriggered(@event);
            return this;
        }

        public IChannelInboundInvoker FireChannelRead(object msg)
        {
            Ctx.FireChannelRead(msg);
            return this;
        }

        public IChannelInboundInvoker FireChannelReadComplete()
        {
            Ctx.FireChannelReadComplete();
            return this;
        }

        public IChannelInboundInvoker FireChannelWritabilityChanged()
        {
            Ctx.FireChannelWritabilityChanged();
            return this;
        }

        public Task BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default)
        {
            return Ctx.BindAsync(localAddress, cancellationToken);
        }

        public Task ConnectAsync(EndPoint remoteAddress, EndPoint localAddress = null, CancellationToken cancellationToken = default)
        {
            return Ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return Ctx.DisconnectAsync(cancellationToken);
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            return Ctx.CloseAsync(cancellationToken);
        }

        public Task DeregisterAsync(CancellationToken cancellationToken = default)
        {
            return Ctx.DeregisterAsync(cancellationToken);
        }

        public Task WriteAsync(object msg, CancellationToken cancellationToken = default)
        {
            return Ctx.WriteAsync(msg, cancellationToken);
        }

        public Task WriteAndFlushAsync(object msg, CancellationToken cancellationToken = default)
        {
            return Ctx.WriteAndFlushAsync(msg, cancellationToken);
        }

        public IChannelOutboundInvoker Read()
        {
            Ctx.Read();
            return this;
        }

        public IChannelOutboundInvoker Flush()
        {
            Ctx.Flush();
            return this;
        }

        // CLR: keeps the text of the delegated context, as this context formerly inherited its toString().
        public override string ToString()
        {
            return Ctx.ToString();
        }

        internal void Remove()
        {
            if (Executor.InEventLoop())
                RemoveOnExecutor();
            else
                Executor.Execute(RemoveOnExecutor);
        }

        private void RemoveOnExecutor()
        {
            if (Removed)
                return;
            Removed = true;
            try { _handler.HandlerRemoved(this); }
            catch (Exception cause)
            {
                FireExceptionCaught(new ChannelPipelineException(_handler.GetType().FullName + ".HandlerRemoved() has thrown an exception.", cause));
            }
        }
    }
}
