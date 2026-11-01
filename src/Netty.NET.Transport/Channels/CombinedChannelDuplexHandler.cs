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
    private DelegatingContext _inboundContext;
    private DelegatingContext _outboundContext;
    private volatile bool _handlerAdded;
    private TInbound _inboundHandler;
    private TOutbound _outboundHandler;

    /**
     * Creates a new uninitialized instance. A class that extends this handler must invoke
     * {@link #init(ChannelInboundHandler, ChannelOutboundHandler)} before adding this handler into a
     * {@link ChannelPipeline}.
     */
    protected CombinedChannelDuplexHandler() => EnsureNotSharable();

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
    public void RemoveInboundHandler() { CheckAdded(); _inboundContext.Remove(); }

    /**
     * Removes the {@link ChannelOutboundHandler} that was combined in this {@link CombinedChannelDuplexHandler}.
     */
    public void RemoveOutboundHandler() { CheckAdded(); _outboundContext.Remove(); }

    public override void HandlerAdded(ChannelHandlerContext context)
    {
        if (_inboundHandler == null)
            throw new InvalidOperationException("Init() must be invoked before being added to a ChannelPipeline.");
        _outboundContext = new DelegatingContext(context, _outboundHandler);
        _inboundContext = new InboundContext(context, _inboundHandler, this);
        // The inboundCtx and outboundCtx were created and set now it's safe to call removeInboundHandler() and
        // removeOutboundHandler().
        _handlerAdded = true;
        try { _inboundHandler.HandlerAdded(_inboundContext); }
        finally { _outboundHandler.HandlerAdded(_outboundContext); }
    }

    public override void HandlerRemoved(ChannelHandlerContext context)
    {
        try { _inboundContext.Remove(); }
        finally { _outboundContext.Remove(); }
    }

    private void ForwardInboundException(Exception cause)
    {
        if (!_outboundContext.Removed)
        {
            try
            {
                // We directly delegate to the ChannelOutboundHandler as this may override exceptionCaught(...)
                // as well
                _outboundHandler.ExceptionCaught(_outboundContext, cause);
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
            _outboundContext.Parent.FireExceptionCaught(cause);
    }

    public override void ChannelRegistered(ChannelHandlerContext context)
    {
        Debug.Assert(ReferenceEquals(context, _inboundContext.Parent));
        if (!_inboundContext.Removed)
            _inboundHandler.ChannelRegistered(_inboundContext);
        else
            _inboundContext.FireChannelRegistered();
    }

    public override void ChannelUnregistered(ChannelHandlerContext context)
    {
        Debug.Assert(ReferenceEquals(context, _inboundContext.Parent));
        if (!_inboundContext.Removed)
            _inboundHandler.ChannelUnregistered(_inboundContext);
        else
            _inboundContext.FireChannelUnregistered();
    }

    public override void ChannelActive(ChannelHandlerContext context)
    {
        Debug.Assert(ReferenceEquals(context, _inboundContext.Parent));
        if (!_inboundContext.Removed)
            _inboundHandler.ChannelActive(_inboundContext);
        else
            _inboundContext.FireChannelActive();
    }

    public override void ChannelInactive(ChannelHandlerContext context)
    {
        Debug.Assert(ReferenceEquals(context, _inboundContext.Parent));
        if (!_inboundContext.Removed)
            _inboundHandler.ChannelInactive(_inboundContext);
        else
            _inboundContext.FireChannelInactive();
    }

    public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
    {
        Debug.Assert(ReferenceEquals(context, _inboundContext.Parent));
        if (!_inboundContext.Removed)
            _inboundHandler.ExceptionCaught(_inboundContext, cause);
        else
            _inboundContext.FireExceptionCaught(cause);
    }

    public override void UserEventTriggered(ChannelHandlerContext context, object evt)
    {
        Debug.Assert(ReferenceEquals(context, _inboundContext.Parent));
        if (!_inboundContext.Removed)
            _inboundHandler.UserEventTriggered(_inboundContext, evt);
        else
            _inboundContext.FireUserEventTriggered(evt);
    }

    public override void ChannelRead(ChannelHandlerContext context, object message)
    {
        Debug.Assert(ReferenceEquals(context, _inboundContext.Parent));
        if (!_inboundContext.Removed)
            _inboundHandler.ChannelRead(_inboundContext, message);
        else
            _inboundContext.FireChannelRead(message);
    }

    public override void ChannelReadComplete(ChannelHandlerContext context)
    {
        Debug.Assert(ReferenceEquals(context, _inboundContext.Parent));
        if (!_inboundContext.Removed)
            _inboundHandler.ChannelReadComplete(_inboundContext);
        else
            _inboundContext.FireChannelReadComplete();
    }

    public override void ChannelWritabilityChanged(ChannelHandlerContext context)
    {
        Debug.Assert(ReferenceEquals(context, _inboundContext.Parent));
        if (!_inboundContext.Removed)
            _inboundHandler.ChannelWritabilityChanged(_inboundContext);
        else
            _inboundContext.FireChannelWritabilityChanged();
    }

    public override Task BindAsync(ChannelHandlerContext context, EndPoint localAddress, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(context, _outboundContext.Parent));
        return !_outboundContext.Removed
            ? _outboundHandler.BindAsync(_outboundContext, localAddress, cancellationToken)
            : _outboundContext.BindAsync(localAddress, cancellationToken);
    }

    public override Task ConnectAsync(ChannelHandlerContext context, EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(context, _outboundContext.Parent));
        return !_outboundContext.Removed
            ? _outboundHandler.ConnectAsync(_outboundContext, remoteAddress, localAddress, cancellationToken)
            : _outboundContext.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    public override Task DisconnectAsync(ChannelHandlerContext context, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(context, _outboundContext.Parent));
        return !_outboundContext.Removed
            ? _outboundHandler.DisconnectAsync(_outboundContext, cancellationToken)
            : _outboundContext.DisconnectAsync(cancellationToken);
    }

    public override Task CloseAsync(ChannelHandlerContext context, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(context, _outboundContext.Parent));
        return !_outboundContext.Removed
            ? _outboundHandler.CloseAsync(_outboundContext, cancellationToken)
            : _outboundContext.CloseAsync(cancellationToken);
    }

    public override Task DeregisterAsync(ChannelHandlerContext context, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(context, _outboundContext.Parent));
        return !_outboundContext.Removed
            ? _outboundHandler.DeregisterAsync(_outboundContext, cancellationToken)
            : _outboundContext.DeregisterAsync(cancellationToken);
    }

    public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(context, _outboundContext.Parent));
        return !_outboundContext.Removed
            ? _outboundHandler.WriteAsync(_outboundContext, message, cancellationToken)
            : _outboundContext.WriteAsync(message, cancellationToken);
    }

    public override void Read(ChannelHandlerContext context)
    {
        Debug.Assert(ReferenceEquals(context, _outboundContext.Parent));
        if (!_outboundContext.Removed)
            _outboundHandler.Read(_outboundContext);
        else
            _outboundContext.Read();
    }

    public override void Flush(ChannelHandlerContext context)
    {
        Debug.Assert(ReferenceEquals(context, _outboundContext.Parent));
        if (!_outboundContext.Removed)
            _outboundHandler.Flush(_outboundContext);
        else
            _outboundContext.Flush();
    }

    private sealed class InboundContext : DelegatingContext
    {
        private readonly CombinedChannelDuplexHandler<TInbound, TOutbound> _owner;
        internal InboundContext(ChannelHandlerContext parent, IChannelHandler handler,
            CombinedChannelDuplexHandler<TInbound, TOutbound> owner) : base(parent, handler) => _owner = owner;
        public override ChannelHandlerContext FireExceptionCaught(Exception cause)
        {
            _owner.ForwardInboundException(cause);
            return this;
        }
    }

    private class DelegatingContext : ChannelHandlerContext
    {
        internal readonly ChannelHandlerContext Parent;
        private readonly IChannelHandler _handler;
        internal bool Removed { get; private set; }
        internal DelegatingContext(ChannelHandlerContext parent, IChannelHandler handler)
            : base(parent.Pipeline, parent.Name, parent.Handler, parent.AssignedExecutor)
        {
            Parent = parent;
            _handler = handler;
        }
        public override bool IsRemoved => Removed || Parent.IsRemoved;
        public override ChannelHandlerContext FireChannelRegistered() { Parent.FireChannelRegistered(); return this; }
        public override ChannelHandlerContext FireChannelUnregistered() { Parent.FireChannelUnregistered(); return this; }
        public override ChannelHandlerContext FireChannelActive() { Parent.FireChannelActive(); return this; }
        public override ChannelHandlerContext FireChannelInactive() { Parent.FireChannelInactive(); return this; }
        public override ChannelHandlerContext FireExceptionCaught(Exception cause) { Parent.FireExceptionCaught(cause); return this; }
        public override ChannelHandlerContext FireUserEventTriggered(object evt) { Parent.FireUserEventTriggered(evt); return this; }
        public override ChannelHandlerContext FireChannelRead(object message) { Parent.FireChannelRead(message); return this; }
        public override ChannelHandlerContext FireChannelReadComplete() { Parent.FireChannelReadComplete(); return this; }
        public override ChannelHandlerContext FireChannelWritabilityChanged() { Parent.FireChannelWritabilityChanged(); return this; }
        public override Task BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default) => Parent.BindAsync(localAddress, cancellationToken);
        public override Task ConnectAsync(EndPoint remoteAddress, EndPoint localAddress = null, CancellationToken cancellationToken = default) => Parent.ConnectAsync(remoteAddress, localAddress, cancellationToken);
        public override Task DisconnectAsync(CancellationToken cancellationToken = default) => Parent.DisconnectAsync(cancellationToken);
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Parent.CloseAsync(cancellationToken);
        public override Task DeregisterAsync(CancellationToken cancellationToken = default) => Parent.DeregisterAsync(cancellationToken);
        public override Task WriteAsync(object message, CancellationToken cancellationToken = default) => Parent.WriteAsync(message, cancellationToken);
        public override Task WriteAndFlushAsync(object message, CancellationToken cancellationToken = default) => Parent.WriteAndFlushAsync(message, cancellationToken);
        public override IChannelOutboundInvoker Read() { Parent.Read(); return this; }
        public override IChannelOutboundInvoker Flush() { Parent.Flush(); return this; }

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
