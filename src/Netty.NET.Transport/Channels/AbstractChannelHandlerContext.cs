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
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels;

/// <summary>A handler's position in the pipeline. Inbound events travel forward; outbound operations travel backward.</summary>
// CLR dispatch: every invocation below keeps the source shape (head check, then the handler interface). The source's
// extra instanceof ChannelInboundHandlerAdapter/ChannelDuplexHandler/ChannelOutboundHandlerAdapter branches only
// work around HotSpot's secondary-supers cache (JDK-8180450). A C# call through the adapter class would also bypass a
// handler that re-implements the interface explicitly (C# interface re-implementation,
// https://learn.microsoft.com/dotnet/csharp/language-reference/language-specification/interfaces), so the CLR
// dispatches through the interface. Outbound operations return the Task that replaces their ChannelPromise
// (https://learn.microsoft.com/dotnet/api/system.threading.tasks.taskcompletionsource); a CancellationToken is the
// only producer-side input, so isNotValidPromise checks its cancellation.
internal abstract class AbstractChannelHandlerContext : IChannelHandlerContext, IResourceLeakHint
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(AbstractChannelHandlerContext));
    internal volatile AbstractChannelHandlerContext Next;
    internal volatile AbstractChannelHandlerContext Prev;

    // CLR: Interlocked.CompareExchange on _handlerState replaces the AtomicIntegerFieldUpdater HANDLER_STATE_UPDATER
    // (https://learn.microsoft.com/dotnet/api/system.threading.interlocked.compareexchange).

    /**
     * {@link ChannelHandler#handlerAdded(ChannelHandlerContext)} is about to be called.
     */
    private const int ADD_PENDING = 1;
    /**
     * {@link ChannelHandler#handlerAdded(ChannelHandlerContext)} was called.
     */
    private const int ADD_COMPLETE = 2;
    /**
     * {@link ChannelHandler#handlerRemoved(ChannelHandlerContext)} was called.
     */
    private const int REMOVE_COMPLETE = 3;
    /**
     * Neither {@link ChannelHandler#handlerAdded(ChannelHandlerContext)}
     * nor {@link ChannelHandler#handlerRemoved(ChannelHandlerContext)} was called.
     */
    private const int INIT = 0;

    private readonly DefaultChannelPipeline _pipeline;
    private readonly string _name;
    private readonly bool _ordered;
    private readonly int _executionMask;

    // Will be set to null if no child executor should be used, otherwise it will be set to the
    // child executor.
    internal readonly IEventExecutor childExecutor;
    // Cache the concrete value for the executor() method. This method is in the hot-path,
    // and it's a profitable optimisation to avoid as many dependent-loads as possible.
    // It does not need to be volatile, because it's always the same value for a given context,
    // within the lifetime of its registration with an event loop, and deregistering will clear it.
    internal IEventExecutor contextExecutor;

    // Lazily instantiated tasks used to trigger events to a handler with different executor.
    // There is no need to make this volatile as at worse it will just create a few more instances then needed.
    private Tasks _invokeTasks;

    private volatile int _handlerState = INIT;

    // CLR: the handler instance replaces Class<? extends ChannelHandler>; the mask binds the handler's interface
    // methods without invoking them. HeadContext and TailContext are their own handler and pass null.
    internal AbstractChannelHandlerContext(DefaultChannelPipeline pipeline, IEventExecutor executor, string name, IChannelHandler handler)
    {
        _name = ObjectUtil.CheckNotNull(name, "name");
        _pipeline = pipeline;
        childExecutor = executor;
        _executionMask = ChannelHandlerMask.Mask(handler ?? (IChannelHandler)this);
        // Its ordered if its driven by the EventLoop or the given Executor is an instanceof OrderedEventExecutor.
        _ordered = executor == null || executor is IOrderedEventExecutor;
    }

    public IChannel Channel => _pipeline.Channel;

    public IChannelPipeline Pipeline => _pipeline;

    public IByteBufAllocator Alloc()
    {
        return Channel.Config.GetAllocator();
    }

    public IEventExecutor Executor
    {
        get
        {
            IEventExecutor ex = contextExecutor;
            if (ex == null)
            {
                contextExecutor = ex = childExecutor ?? Channel.EventLoop;
            }

            return ex;
        }
    }

    public string Name => _name;

    public abstract IChannelHandler Handler { get; }

    public IChannelInboundInvoker FireChannelRegistered()
    {
        AbstractChannelHandlerContext next = FindContextInbound(ChannelHandlerMask.MaskChannelRegistered);
        if (next.Executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        headContext.ChannelRegistered(next);
                    }
                    else
                    {
                        ((IChannelInboundHandler)handler).ChannelRegistered(next);
                    }
                }
                catch (Exception t)
                {
                    next.InvokeExceptionCaught(t);
                }
            }
            else
            {
                next.FireChannelRegistered();
            }
        }
        else
        {
            next.Executor.Execute(() => FireChannelRegistered());
        }

        return this;
    }

    public IChannelInboundInvoker FireChannelUnregistered()
    {
        AbstractChannelHandlerContext next = FindContextInbound(ChannelHandlerMask.MaskChannelUnregistered);
        if (next.Executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        headContext.ChannelUnregistered(next);
                    }
                    else
                    {
                        ((IChannelInboundHandler)handler).ChannelUnregistered(next);
                    }
                }
                catch (Exception t)
                {
                    next.InvokeExceptionCaught(t);
                }
            }
            else
            {
                next.FireChannelUnregistered();
            }
        }
        else
        {
            next.Executor.Execute(() => FireChannelUnregistered());
        }

        return this;
    }

    public IChannelInboundInvoker FireChannelActive()
    {
        AbstractChannelHandlerContext next = FindContextInbound(ChannelHandlerMask.MaskChannelActive);
        if (next.Executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        headContext.ChannelActive(next);
                    }
                    else
                    {
                        ((IChannelInboundHandler)handler).ChannelActive(next);
                    }
                }
                catch (Exception t)
                {
                    next.InvokeExceptionCaught(t);
                }
            }
            else
            {
                next.FireChannelActive();
            }
        }
        else
        {
            next.Executor.Execute(() => FireChannelActive());
        }

        return this;
    }

    public IChannelInboundInvoker FireChannelInactive()
    {
        AbstractChannelHandlerContext next = FindContextInbound(ChannelHandlerMask.MaskChannelInactive);
        if (next.Executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        headContext.ChannelInactive(next);
                    }
                    else
                    {
                        ((IChannelInboundHandler)handler).ChannelInactive(next);
                    }
                }
                catch (Exception t)
                {
                    next.InvokeExceptionCaught(t);
                }
            }
            else
            {
                next.FireChannelInactive();
            }
        }
        else
        {
            next.Executor.Execute(() => FireChannelInactive());
        }

        return this;
    }

    public IChannelInboundInvoker FireExceptionCaught(Exception cause)
    {
        AbstractChannelHandlerContext next = FindContextInbound(ChannelHandlerMask.MaskExceptionCaught);
        ObjectUtil.CheckNotNull(cause, "cause");

        if (next.Executor.InEventLoop())
        {
            next.InvokeExceptionCaught(cause);
        }
        else
        {
            try
            {
                next.Executor.Execute(() => next.InvokeExceptionCaught(cause));
            }
            catch (Exception t)
            {
                if (logger.IsWarnEnabled())
                {
                    logger.Warn("Failed to submit an exceptionCaught() event.", t);
                    logger.Warn("The exceptionCaught() event that was failed to submit was:", cause);
                }
            }
        }

        return this;
    }

    private void InvokeExceptionCaught(Exception cause)
    {
        if (InvokeHandler())
        {
            try
            {
                Handler.ExceptionCaught(this, cause);
            }
            catch (Exception error)
            {
                if (logger.IsDebugEnabled())
                {
                    logger.Debug(
                        "An exception " +
                        "was thrown by a user handler's exceptionCaught() " +
                        "method while handling the following exception:", cause);
                }
                else if (logger.IsWarnEnabled())
                {
                    logger.Warn(
                        "An exception '{}' [enable DEBUG level for full stacktrace] " +
                        "was thrown by a user handler's exceptionCaught() " +
                        "method while handling the following exception:", error, cause);
                }
            }
        }
        else
        {
            FireExceptionCaught(cause);
        }
    }

    public IChannelInboundInvoker FireUserEventTriggered(object @event)
    {
        ObjectUtil.CheckNotNull(@event, "event");

        AbstractChannelHandlerContext next = FindContextInbound(ChannelHandlerMask.MaskUserEventTriggered);
        if (next.Executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        headContext.UserEventTriggered(next, @event);
                    }
                    else
                    {
                        ((IChannelInboundHandler)handler).UserEventTriggered(next, @event);
                    }
                }
                catch (Exception t)
                {
                    next.InvokeExceptionCaught(t);
                }
            }
            else
            {
                next.FireUserEventTriggered(@event);
            }
        }
        else
        {
            next.Executor.Execute(() => FireUserEventTriggered(@event));
        }

        return this;
    }

    public IChannelInboundInvoker FireChannelRead(object msg)
    {
        AbstractChannelHandlerContext next = FindContextInbound(ChannelHandlerMask.MaskChannelRead);
        if (next.Executor.InEventLoop())
        {
            object m = _pipeline.Touch(msg, next);

            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        headContext.ChannelRead(next, m);
                    }
                    else
                    {
                        ((IChannelInboundHandler)handler).ChannelRead(next, m);
                    }
                }
                catch (Exception t)
                {
                    next.InvokeExceptionCaught(t);
                }
            }
            else
            {
                next.FireChannelRead(m);
            }
        }
        else
        {
            next.Executor.Execute(() => FireChannelRead(msg));
        }

        return this;
    }

    public IChannelInboundInvoker FireChannelReadComplete()
    {
        AbstractChannelHandlerContext next = FindContextInbound(ChannelHandlerMask.MaskChannelReadComplete);
        if (next.Executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        headContext.ChannelReadComplete(next);
                    }
                    else
                    {
                        ((IChannelInboundHandler)handler).ChannelReadComplete(next);
                    }
                }
                catch (Exception t)
                {
                    next.InvokeExceptionCaught(t);
                }
            }
            else
            {
                next.FireChannelReadComplete();
            }
        }
        else
        {
            next.Executor.Execute(GetInvokeTasks().fireChannelReadCompleteTask);
        }

        return this;
    }

    public IChannelInboundInvoker FireChannelWritabilityChanged()
    {
        AbstractChannelHandlerContext next = FindContextInbound(ChannelHandlerMask.MaskChannelWritabilityChanged);
        if (next.Executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        headContext.ChannelWritabilityChanged(next);
                    }
                    else
                    {
                        ((IChannelInboundHandler)handler).ChannelWritabilityChanged(next);
                    }
                }
                catch (Exception t)
                {
                    next.InvokeExceptionCaught(t);
                }
            }
            else
            {
                next.FireChannelWritabilityChanged();
            }
        }
        else
        {
            next.Executor.Execute(GetInvokeTasks().fireChannelWritabilityChangedTask);
        }

        return this;
    }

    // CLR: bind(localAddress) and bind(localAddress, promise) are one Task-returning operation; the returned Task is
    // the promise. Across executors the operation is re-invoked from this context, as the source runnable does, and
    // its Task completes the promise returned here.
    public Task BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default)
    {
        ObjectUtil.CheckNotNull(localAddress, "localAddress");
        if (IsNotValidPromise(cancellationToken))
        {
            // cancelled
            return Task.FromCanceled(cancellationToken);
        }

        AbstractChannelHandlerContext next = FindContextOutbound(ChannelHandlerMask.MaskBind);
        IEventExecutor executor = next.Executor;
        if (executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        return headContext.BindAsync(next, localAddress, cancellationToken);
                    }

                    return ((IChannelOutboundHandler)handler).BindAsync(next, localAddress, cancellationToken);
                }
                catch (Exception t)
                {
                    return NotifyOutboundHandlerException(t);
                }
            }

            return next.BindAsync(localAddress, cancellationToken);
        }

        TaskCompletionSource p = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SafeExecute(executor, () => Cascade(BindAsync(localAddress, cancellationToken), p), p, null, false);
        return p.Task;
    }

    public Task ConnectAsync(EndPoint remoteAddress, EndPoint localAddress = null, CancellationToken cancellationToken = default)
    {
        ObjectUtil.CheckNotNull(remoteAddress, "remoteAddress");

        if (IsNotValidPromise(cancellationToken))
        {
            // cancelled
            return Task.FromCanceled(cancellationToken);
        }

        AbstractChannelHandlerContext next = FindContextOutbound(ChannelHandlerMask.MaskConnect);
        IEventExecutor executor = next.Executor;
        if (executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        return headContext.ConnectAsync(next, remoteAddress, localAddress, cancellationToken);
                    }

                    return ((IChannelOutboundHandler)handler).ConnectAsync(next, remoteAddress, localAddress, cancellationToken);
                }
                catch (Exception t)
                {
                    return NotifyOutboundHandlerException(t);
                }
            }

            return next.ConnectAsync(remoteAddress, localAddress, cancellationToken);
        }

        TaskCompletionSource p = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SafeExecute(executor, () => Cascade(ConnectAsync(remoteAddress, localAddress, cancellationToken), p), p, null, false);
        return p.Task;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!Channel.Metadata.HasDisconnect())
        {
            // Translate disconnect to close if the channel has no notion of disconnect-reconnect.
            // So far, UDP/IP is the only transport that has such behavior.
            return CloseAsync(cancellationToken);
        }
        if (IsNotValidPromise(cancellationToken))
        {
            // cancelled
            return Task.FromCanceled(cancellationToken);
        }

        AbstractChannelHandlerContext next = FindContextOutbound(ChannelHandlerMask.MaskDisconnect);
        IEventExecutor executor = next.Executor;
        if (executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        return headContext.DisconnectAsync(next, cancellationToken);
                    }

                    return ((IChannelOutboundHandler)handler).DisconnectAsync(next, cancellationToken);
                }
                catch (Exception t)
                {
                    return NotifyOutboundHandlerException(t);
                }
            }

            return next.DisconnectAsync(cancellationToken);
        }

        TaskCompletionSource p = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SafeExecute(executor, () => Cascade(DisconnectAsync(cancellationToken), p), p, null, false);
        return p.Task;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (IsNotValidPromise(cancellationToken))
        {
            // cancelled
            return Task.FromCanceled(cancellationToken);
        }

        AbstractChannelHandlerContext next = FindContextOutbound(ChannelHandlerMask.MaskClose);
        IEventExecutor executor = next.Executor;
        if (executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        return headContext.CloseAsync(next, cancellationToken);
                    }

                    return ((IChannelOutboundHandler)handler).CloseAsync(next, cancellationToken);
                }
                catch (Exception t)
                {
                    return NotifyOutboundHandlerException(t);
                }
            }

            return next.CloseAsync(cancellationToken);
        }

        TaskCompletionSource p = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SafeExecute(executor, () => Cascade(CloseAsync(cancellationToken), p), p, null, false);
        return p.Task;
    }

    public Task DeregisterAsync(CancellationToken cancellationToken = default)
    {
        if (IsNotValidPromise(cancellationToken))
        {
            // cancelled
            return Task.FromCanceled(cancellationToken);
        }

        AbstractChannelHandlerContext next = FindContextOutbound(ChannelHandlerMask.MaskDeregister);
        IEventExecutor executor = next.Executor;
        if (executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        return headContext.DeregisterAsync(next, cancellationToken);
                    }

                    return ((IChannelOutboundHandler)handler).DeregisterAsync(next, cancellationToken);
                }
                catch (Exception t)
                {
                    return NotifyOutboundHandlerException(t);
                }
            }

            // CLR: forwards from next as the other operations do. The pinned source calls deregister(promise) on this
            // context here, which would select the same not-yet-invokable next context again.
            return next.DeregisterAsync(cancellationToken);
        }

        TaskCompletionSource p = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SafeExecute(executor, () => Cascade(DeregisterAsync(cancellationToken), p), p, null, false);
        return p.Task;
    }

    public IChannelOutboundInvoker Read()
    {
        AbstractChannelHandlerContext next = FindContextOutbound(ChannelHandlerMask.MaskRead);
        if (next.Executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        headContext.Read(next);
                    }
                    else
                    {
                        ((IChannelOutboundHandler)handler).Read(next);
                    }
                }
                catch (Exception t)
                {
                    next.InvokeExceptionCaught(t);
                }
            }
            else
            {
                next.Read();
            }
        }
        else
        {
            next.Executor.Execute(GetInvokeTasks().readTask);
        }

        return this;
    }

    // CLR: write(msg) and write(msg, promise) are one Task-returning operation.
    public Task WriteAsync(object msg, CancellationToken cancellationToken = default)
    {
        return Write(msg, false, cancellationToken);
    }

    public IChannelOutboundInvoker Flush()
    {
        AbstractChannelHandlerContext next = FindContextOutbound(ChannelHandlerMask.MaskFlush);
        IEventExecutor executor = next.Executor;
        if (executor.InEventLoop())
        {
            if (next.InvokeHandler())
            {
                try
                {
                    // DON'T CHANGE
                    // Duplex handlers implements both out/in interfaces causing a scalability issue
                    // see https://bugs.openjdk.org/browse/JDK-8180450
                    // CLR: interface dispatch after the head check (see the class comment).
                    IChannelHandler handler = next.Handler;
                    DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                    if (handler == headContext)
                    {
                        headContext.Flush(next);
                    }
                    else
                    {
                        ((IChannelOutboundHandler)handler).Flush(next);
                    }
                }
                catch (Exception t)
                {
                    next.InvokeExceptionCaught(t);
                }
            }
            else
            {
                next.Flush();
            }
        }
        else
        {
            // CLR: a null promise is channel().voidPromise() (see SafeExecute).
            SafeExecute(executor, GetInvokeTasks().flushTask, null, null, false);
        }

        return this;
    }

    // CLR: writeAndFlush(msg) and writeAndFlush(msg, promise) are one Task-returning operation.
    public Task WriteAndFlushAsync(object msg, CancellationToken cancellationToken = default)
    {
        return Write(msg, true, cancellationToken);
    }

    internal Task Write(object msg, bool flush, CancellationToken cancellationToken)
    {
        if (ValidateWrite(msg, cancellationToken))
        {
            AbstractChannelHandlerContext next = FindContextOutbound(flush ?
                ChannelHandlerMask.MaskWrite | ChannelHandlerMask.MaskFlush : ChannelHandlerMask.MaskWrite);
            object m = _pipeline.Touch(msg, next);
            IEventExecutor executor = next.Executor;
            if (executor.InEventLoop())
            {
                if (next.InvokeHandler())
                {
                    Task promise;

                    try
                    {
                        // DON'T CHANGE
                        // Duplex handlers implements both out/in interfaces causing a scalability issue
                        // see https://bugs.openjdk.org/browse/JDK-8180450
                        // CLR: interface dispatch after the head check (see the class comment).
                        IChannelHandler handler = next.Handler;
                        DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                        if (handler == headContext)
                        {
                            promise = headContext.WriteAsync(next, msg, cancellationToken);
                        }
                        else
                        {
                            promise = ((IChannelOutboundHandler)handler).WriteAsync(next, msg, cancellationToken);
                        }
                    }
                    catch (Exception t)
                    {
                        promise = NotifyOutboundHandlerException(t);
                    }

                    if (flush)
                    {
                        try
                        {
                            // DON'T CHANGE
                            // Duplex handlers implements both out/in interfaces causing a scalability issue
                            // see https://bugs.openjdk.org/browse/JDK-8180450
                            // CLR: interface dispatch after the head check (see the class comment).
                            IChannelHandler handler = next.Handler;
                            DefaultChannelPipeline.HeadContext headContext = _pipeline.Head;
                            if (handler == headContext)
                            {
                                headContext.Flush(next);
                            }
                            else
                            {
                                ((IChannelOutboundHandler)handler).Flush(next);
                            }
                        }
                        catch (Exception t)
                        {
                            next.InvokeExceptionCaught(t);
                        }
                    }

                    return promise;
                }

                return next.Write(msg, flush, cancellationToken);
            }

            TaskCompletionSource p = new(TaskCreationOptions.RunContinuationsAsynchronously);
            WriteTask task = WriteTask.NewInstance(this, m, p, flush, cancellationToken);
            if (!SafeExecute(executor, task.run, p, m, !flush))
            {
                // We failed to submit the WriteTask. We need to cancel it so we decrement the pending bytes
                // and put it back in the Recycler for re-use later.
                //
                // See https://github.com/netty/netty/issues/8343.
                task.Cancel();
            }

            return p.Task;
        }

        return Task.FromCanceled(cancellationToken);
    }

    private static bool ValidateWrite(object msg, CancellationToken cancellationToken)
    {
        ObjectUtil.CheckNotNull(msg, "msg");

        // CLR: IsNotValidPromise only reads the CancellationToken and cannot throw, so the source's
        // release-and-rethrow guard for its RuntimeException has nothing to guard.
        if (IsNotValidPromise(cancellationToken))
        {
            ReferenceCountUtil.Release(msg);
            return false; // cancelled
        }

        return true;
    }

    // CLR: tryFailure(promise, cause, logger) becomes the faulted Task of the operation. Every CLR operation returns an
    // observable Task, so there is no VoidChannelPromise whose failure is only logged.
    private static Task NotifyOutboundHandlerException(Exception cause)
    {
        return Task.FromException(cause);
    }

    // CLR: the ChannelPromise checks (already done, foreign channel, VoidChannelPromise, CloseFuture) guard producer
    // arguments that the Task-returning operations do not take; the CancellationToken carries the cancellation.
    private static bool IsNotValidPromise(CancellationToken cancellationToken)
    {
        // Check if the promise was cancelled and if so signal that the processing of the operation
        // should not be performed.
        //
        // See https://github.com/netty/netty/issues/2349
        return cancellationToken.IsCancellationRequested;
    }

    private AbstractChannelHandlerContext FindContextInbound(int mask)
    {
        AbstractChannelHandlerContext ctx = this;
        IEventExecutor currentExecutor = Executor;

        do
        {
            ctx = ctx.Next;
        } while (SkipContext(ctx, currentExecutor, mask, ChannelHandlerMask.MaskOnlyInbound));

        return ctx;
    }

    private AbstractChannelHandlerContext FindContextOutbound(int mask)
    {
        AbstractChannelHandlerContext ctx = this;
        IEventExecutor currentExecutor = Executor;

        do
        {
            ctx = ctx.Prev;
        } while (SkipContext(ctx, currentExecutor, mask, ChannelHandlerMask.MaskOnlyOutbound));

        return ctx;
    }

    private static bool SkipContext(
        AbstractChannelHandlerContext ctx, IEventExecutor currentExecutor, int mask, int onlyMask)
    {
        // Ensure we correctly handle MASK_EXCEPTION_CAUGHT which is not included in the MASK_EXCEPTION_CAUGHT
        return (ctx._executionMask & (onlyMask | mask)) == 0 ||
            // We can only skip if the EventExecutor is the same as otherwise we need to ensure we offload
            // everything to preserve ordering.
            //
            // See https://github.com/netty/netty/issues/10067
            (ctx.Executor == currentExecutor && (ctx._executionMask & mask) == 0);
    }

    internal void SetRemoved()
    {
        _handlerState = REMOVE_COMPLETE;
    }

    // CLR: CS0420 does not apply to Interlocked, which performs its own full-fence access
    // (https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-messages/cs0420).
#pragma warning disable CS0420
    internal bool SetAddComplete()
    {
        for (; ; )
        {
            int oldState = _handlerState;
            if (oldState == REMOVE_COMPLETE)
            {
                return false;
            }
            // Ensure we never update when the handlerState is REMOVE_COMPLETE already.
            // oldState is usually ADD_PENDING but can also be REMOVE_COMPLETE when an EventExecutor is used that is not
            // exposing ordering guarantees.
            if (Interlocked.CompareExchange(ref _handlerState, ADD_COMPLETE, oldState) == oldState)
            {
                return true;
            }
        }
    }

    internal void SetAddPending()
    {
        bool updated = Interlocked.CompareExchange(ref _handlerState, ADD_PENDING, INIT) == INIT;
        Debug.Assert(updated); // This should always be true as it MUST be called before setAddComplete() or setRemoved().
    }
#pragma warning restore CS0420

    internal void CallHandlerAdded()
    {
        // We must call setAddComplete before calling handlerAdded. Otherwise if the handlerAdded method generates
        // any pipeline events ctx.handler() will miss them because the state will not allow it.
        if (SetAddComplete())
        {
            Handler.HandlerAdded(this);
        }
    }

    internal void CallHandlerRemoved()
    {
        try
        {
            // Only call handlerRemoved(...) if we called handlerAdded(...) before.
            if (_handlerState == ADD_COMPLETE)
            {
                Handler.HandlerRemoved(this);
            }
        }
        finally
        {
            // Mark the handler as removed in any case.
            SetRemoved();
        }
    }

    /**
     * Makes best possible effort to detect if {@link ChannelHandler#handlerAdded(ChannelHandlerContext)} was called
     * yet. If not return {@code false} and if called or could not detect return {@code true}.
     *
     * If this method returns {@code false} we will not invoke the {@link ChannelHandler} but just forward the event.
     * This is needed as {@link DefaultChannelPipeline} may already put the {@link ChannelHandler} in the linked-list
     * but not called {@link ChannelHandler#handlerAdded(ChannelHandlerContext)}.
     */
    internal bool InvokeHandler()
    {
        // Store in local variable to reduce volatile reads.
        int handlerState = _handlerState;
        return handlerState == ADD_COMPLETE || (!_ordered && handlerState == ADD_PENDING);
    }

    public bool IsRemoved => _handlerState == REMOVE_COMPLETE;

    public IAttribute<T> Attr<T>(AttributeKey<T> key) where T : class
    {
        return Channel.Attr(key);
    }

    public bool HasAttr<T>(AttributeKey<T> key) where T : class
    {
        return Channel.HasAttr(key);
    }

    // CLR: an instance member, because a null promise stands for channel().voidPromise(), whose setFailure fires
    // exceptionCaught through the pipeline of a registered channel (VoidChannelPromise(channel, true)).
    private bool SafeExecute(IEventExecutor executor, Action runnable,
        TaskCompletionSource promise, object msg, bool lazy)
    {
        try
        {
            if (lazy && executor is AbstractEventExecutor eventExecutor)
            {
                eventExecutor.LazyExecute(runnable);
            }
            else
            {
                executor.Execute(runnable);
            }

            return true;
        }
        catch (Exception cause)
        {
            try
            {
                if (msg != null)
                {
                    ReferenceCountUtil.Release(msg);
                }
            }
            finally
            {
                if (promise != null)
                {
                    promise.TrySetException(cause);
                }
                else if (Channel.IsRegistered)
                {
                    Channel.Pipeline.FireExceptionCaught(cause);
                }
            }

            return false;
        }
    }

    // CLR: PromiseNotifier.cascade for a Task. The operation re-invoked on the next executor returns its own Task,
    // whose outcome completes the promise returned before the executor hop.
    private static void Cascade(Task future, TaskCompletionSource promise)
    {
        if (future.IsCompleted)
        {
            SetOutcome(future, promise);
            return;
        }

        future.ContinueWith(static (completed, state) => SetOutcome(completed, (TaskCompletionSource)state), promise,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static void SetOutcome(Task future, TaskCompletionSource promise)
    {
        if (future.IsCompletedSuccessfully)
        {
            promise.TrySetResult();
        }
        else if (future.IsCanceled)
        {
            try
            {
                future.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException e)
            {
                promise.TrySetCanceled(e.CancellationToken);
            }
        }
        else
        {
            promise.TrySetException(future.Exception!.InnerExceptions);
        }
    }

    public string ToHintString()
    {
        return '\'' + _name + "' will handle the message from this point.";
    }

    // CLR: Java prints simpleClassName(ChannelHandlerContext.class); the C# interface carries the I prefix, so the
    // Java interface name is kept as text.
    public override string ToString()
    {
        return "ChannelHandlerContext" + '(' + _name + ", " + Channel + ')';
    }

    internal Tasks GetInvokeTasks()
    {
        Tasks tasks = _invokeTasks;
        if (tasks == null)
        {
            _invokeTasks = tasks = new Tasks(this);
        }

        return tasks;
    }

    // CLR: the pooled WriteTask uses the Common Recycler (Recycler.Create), and its Runnable is the cached run delegate.
    internal sealed class WriteTask
    {
        private static readonly Recycler<WriteTask> RECYCLER = Recycler.Create<WriteTask>(handle => new WriteTask(handle));

        internal static WriteTask NewInstance(AbstractChannelHandlerContext ctx,
            object msg, TaskCompletionSource promise, bool flush, CancellationToken cancellationToken)
        {
            WriteTask task = RECYCLER.Get();
            Init(task, ctx, msg, promise, flush, cancellationToken);
            return task;
        }

        private static readonly bool ESTIMATE_TASK_SIZE_ON_SUBMIT =
            SystemPropertyUtil.GetBoolean("io.netty.transport.estimateSizeOnSubmit", true);

        // Assuming compressed oops, 12 bytes obj header, 4 ref fields and one int field
        private static readonly int WRITE_TASK_OVERHEAD =
            SystemPropertyUtil.GetInt("io.netty.transport.writeTaskSizeOverhead", 32);

        private readonly IRecyclerHandle<WriteTask> handle;
        internal readonly Action run;
        private AbstractChannelHandlerContext ctx;
        private object msg;
        private TaskCompletionSource promise;
        // CLR: the operation's CancellationToken travels with the write, as its ChannelPromise does.
        private CancellationToken cancellationToken;
        private int size; // sign bit controls flush

        private WriteTask(IRecyclerHandle<WriteTask> handle)
        {
            this.handle = handle;
            run = Run;
        }

        internal static void Init(WriteTask task, AbstractChannelHandlerContext ctx,
            object msg, TaskCompletionSource promise, bool flush, CancellationToken cancellationToken)
        {
            task.ctx = ctx;
            task.msg = msg;
            task.promise = promise;
            task.cancellationToken = cancellationToken;

            if (ESTIMATE_TASK_SIZE_ON_SUBMIT)
            {
                task.size = ctx._pipeline.EstimatorHandle().Size(msg) + WRITE_TASK_OVERHEAD;
                ctx._pipeline.IncrementPendingOutboundBytes(task.size);
            }
            else
            {
                task.size = 0;
            }

            if (flush)
            {
                task.size |= int.MinValue;
            }
        }

        public void Run()
        {
            try
            {
                DecrementPendingOutboundBytes();
                Cascade(ctx.Write(msg, size < 0, cancellationToken), promise);
            }
            finally
            {
                Recycle();
            }
        }

        internal void Cancel()
        {
            try
            {
                DecrementPendingOutboundBytes();
            }
            finally
            {
                Recycle();
            }
        }

        private void DecrementPendingOutboundBytes()
        {
            if (ESTIMATE_TASK_SIZE_ON_SUBMIT)
            {
                ctx._pipeline.DecrementPendingOutboundBytes(size & int.MaxValue);
            }
        }

        private void Recycle()
        {
            // Set to null so the GC can collect them directly
            ctx = null;
            msg = null;
            promise = null;
            cancellationToken = default;
            handle.Recycle(this);
        }
    }

    // CLR: the cached Runnables are Action delegates.
    internal sealed class Tasks
    {
        internal readonly Action fireChannelReadCompleteTask;
        internal readonly Action readTask;
        internal readonly Action fireChannelWritabilityChangedTask;
        internal readonly Action flushTask;

        internal Tasks(AbstractChannelHandlerContext ctx)
        {
            fireChannelReadCompleteTask = () => ctx.FireChannelReadComplete();
            readTask = () => ctx.Read();
            fireChannelWritabilityChangedTask = () => ctx.FireChannelWritabilityChanged();
            flushTask = () => ctx.Flush();
        }
    }
}
