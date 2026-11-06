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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;

/// <summary>A handler's position in the pipeline. Inbound events travel forward; outbound operations travel backward.</summary>
/**
 * Enables a {@link ChannelHandler} to interact with its {@link ChannelPipeline}
 * and other handlers. Among other things a handler can notify the next {@link ChannelHandler} in the
 * {@link ChannelPipeline} as well as modify the {@link ChannelPipeline} it belongs to dynamically.
 *
 * <h3>Notify</h3>
 *
 * You can notify the closest handler in the same {@link ChannelPipeline} by calling one of the various methods
 * provided here.
 *
 * Please refer to {@link ChannelPipeline} to understand how an event flows.
 *
 * <h3>Modifying a pipeline</h3>
 *
 * You can get the {@link ChannelPipeline} your handler belongs to by calling
 * {@link #pipeline()}.  A non-trivial application could insert, remove, or
 * replace handlers in the pipeline dynamically at runtime.
 *
 * <h3>Retrieving for later use</h3>
 *
 * You can keep the {@link ChannelHandlerContext} for later use, such as
 * triggering an event outside the handler methods, even from a different thread.
 * <pre>
 * public class MyHandler extends {@link ChannelDuplexHandler} {
 *
 *     <b>private {@link ChannelHandlerContext} ctx;</b>
 *
 *     public void beforeAdd({@link ChannelHandlerContext} ctx) {
 *         <b>this.ctx = ctx;</b>
 *     }
 *
 *     public void login(String username, password) {
 *         ctx.write(new LoginMessage(username, password));
 *     }
 *     ...
 * }
 * </pre>
 *
 * <h3>Storing stateful information</h3>
 *
 * {@link #attr(AttributeKey)} allow you to
 * store and access stateful information that is related with a {@link ChannelHandler} / {@link Channel} and its
 * context. Please refer to {@link ChannelHandler} to learn various recommended
 * ways to manage stateful information.
 *
 * <h3>A handler can have more than one {@link ChannelHandlerContext}</h3>
 *
 * Please note that a {@link ChannelHandler} instance can be added to more than
 * one {@link ChannelPipeline}.  It means a single {@link ChannelHandler}
 * instance can have more than one {@link ChannelHandlerContext} and therefore
 * the single instance can be invoked with different
 * {@link ChannelHandlerContext}s if it is added to one or more {@link ChannelPipeline}s more than once.
 * Also note that a {@link ChannelHandler} that is supposed to be added to multiple {@link ChannelPipeline}s should
 * be marked as {@link io.netty.channel.ChannelHandler.Sharable}.
 *
 * <h3>Additional resources worth reading</h3>
 * <p>
 * Please refer to the {@link ChannelHandler}, and
 * {@link ChannelPipeline} to find out more about inbound and outbound operations,
 * what fundamental differences they have, how they flow in a  pipeline,  and how to handle
 * the operation in your application.
 */
public class ChannelHandlerContext : IChannelOutboundInvoker, IAttributeMap
{
    internal volatile ChannelHandlerContext Previous;
    internal volatile ChannelHandlerContext Next;
    private int _state;
    internal int State { get => Volatile.Read(ref _state); set => Volatile.Write(ref _state, value); } // 0 initial, 1 added, 2 removed, 3 add pending.
    internal bool Linked = true;
    private readonly IEventExecutor _executor;
    private readonly bool _ordered;
    private readonly int _executionMask;
    internal IEventExecutor AssignedExecutor => _executor;
    internal ChannelHandlerContext(DefaultChannelPipeline pipeline, string name, IChannelHandler handler, IEventExecutor executor = null)
    {
        Pipeline = pipeline;
        Name = name;
        Handler = handler;
        _executor = executor;
        _executionMask = ChannelHandlerMask.Mask(handler.GetType());
        // Its ordered if its driven by the EventLoop or the given Executor is an instanceof OrderedEventExecutor.
        _ordered = executor == null || executor is IOrderedEventExecutor;
        State = 3;
    }
    internal bool TrySetAdded()
    {
        while (true)
        {
            int state = State;
            if (state == 1 || state == 2)
                return false;
            if (Interlocked.CompareExchange(ref _state, 1, state) == state)
                return true;
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
    private bool CanInvoke
    {
        get
        {
            // Store in local variable to reduce volatile reads.
            int state = State;
            return state == 1 || (!_ordered && state == 3);
        }
    }
    /**
     * Return the assigned {@link ChannelPipeline}
     */
    public DefaultChannelPipeline Pipeline { get; }
    /**
     * Return the {@link Channel} which is bound to the {@link ChannelHandlerContext}.
     */
    public IChannel Channel => Pipeline.Channel;
    /**
     * Returns the {@link EventExecutor} which is used to execute an arbitrary task.
     */
    public IEventExecutor Executor => _executor ?? Channel.EventLoop;
    /**
     * Return the assigned {@link ByteBufAllocator} which will be used to allocate {@link ByteBuf}s.
     */
    public IByteBufAllocator Allocator => Channel.Config.GetAllocator();
    /**
     * The unique name of the {@link ChannelHandlerContext}.The name was used when then {@link ChannelHandler}
     * was added to the {@link ChannelPipeline}. This name can also be used to access the registered
     * {@link ChannelHandler} from the {@link ChannelPipeline}.
     */
    public string Name { get; }
    /**
     * The {@link ChannelHandler} that is bound this {@link ChannelHandlerContext}.
     */
    public IChannelHandler Handler { get; }
    /**
     * Return {@code true} if the {@link ChannelHandler} which belongs to this context was removed
     * from the {@link ChannelPipeline}. Note that this method is only meant to be called from with in the
     * {@link EventLoop}.
     */
    public virtual bool IsRemoved => State == 2;
    /**
     * @deprecated Use {@link Channel#attr(AttributeKey)}
     */
    public IAttribute<T> Attr<T>(AttributeKey<T> key) where T : class => Channel.Attr(key);
    /**
     * @deprecated Use {@link Channel#hasAttr(AttributeKey)}
     */
    public bool HasAttr<T>(AttributeKey<T> key) where T : class => Channel.HasAttr(key);

    private ChannelHandlerContext FindInbound(int mask)
    {
        // Like source findContextInbound/fireChannelRegistered, select pending handlers before dispatch.
        // Their owner must run queued HandlerAdded callbacks before deciding whether to invoke or forward.
        var context = Next;
        var executor = Executor;
        while (context != null && (context.State == 2 || SkipContext(context, executor, mask, ChannelHandlerMask.OnlyInbound)))
            context = context.Next;
        return context;
    }
    private ChannelHandlerContext FindOutbound(int mask)
    {
        var context = Previous;
        var executor = Executor;
        while (context != null && (context.State == 2 || SkipContext(context, executor, mask, ChannelHandlerMask.OnlyOutbound)))
            context = context.Previous;
        return context;
    }
    private static bool SkipContext(ChannelHandlerContext context, IEventExecutor executor, int mask, int direction)
    {
        return (context._executionMask & (direction | mask)) == 0 ||
            // We can only skip if the EventExecutor is the same as otherwise we need to ensure we offload
            // everything to preserve ordering.
            //
            // See https://github.com/netty/netty/issues/10067
            (ReferenceEquals(context.Executor, executor) && (context._executionMask & mask) == 0);
    }
    private ChannelHandlerContext Fire(Action<IChannelInboundHandler, ChannelHandlerContext> callback, int mask, object ownedMessage = null)
    {
        var next = FindInbound(mask);
        if (next == null)
        { ReferenceCountUtil.Release(ownedMessage); return this; }
        void Invoke()
        {
            if (!next.CanInvoke)
            { next.Fire(callback, mask, ownedMessage); return; }
            try
            { callback((IChannelInboundHandler)next.Handler, next); }
            catch (Exception error) { next.NotifyException(error); }
        }
        try
        { if (next.Executor.InEventLoop()) Invoke(); else next.Executor.Execute(Invoke); }
        catch { ReferenceCountUtil.Release(ownedMessage); throw; }
        return this;
    }
    public virtual ChannelHandlerContext FireChannelRegistered() => Fire((h, c) => h.ChannelRegistered(c), ChannelHandlerMask.ChannelRegistered);
    public virtual ChannelHandlerContext FireChannelUnregistered() => Fire((h, c) => h.ChannelUnregistered(c), ChannelHandlerMask.ChannelUnregistered);
    public virtual ChannelHandlerContext FireChannelActive() => Fire((h, c) => h.ChannelActive(c), ChannelHandlerMask.ChannelActive);
    public virtual ChannelHandlerContext FireChannelInactive() => Fire((h, c) => h.ChannelInactive(c), ChannelHandlerMask.ChannelInactive);
    public virtual ChannelHandlerContext FireChannelRead(object message)
    { ArgumentNullException.ThrowIfNull(message); return Fire((h, c) => h.ChannelRead(c, message), ChannelHandlerMask.ChannelRead, message); }
    public virtual ChannelHandlerContext FireChannelReadComplete() => Fire((h, c) => h.ChannelReadComplete(c), ChannelHandlerMask.ChannelReadComplete);
    public virtual ChannelHandlerContext FireUserEventTriggered(object evt)
    { ArgumentNullException.ThrowIfNull(evt); return Fire((h, c) => h.UserEventTriggered(c, evt), ChannelHandlerMask.UserEventTriggered, evt); }
    public virtual ChannelHandlerContext FireChannelWritabilityChanged() => Fire((h, c) => h.ChannelWritabilityChanged(c), ChannelHandlerMask.ChannelWritabilityChanged);
    public virtual ChannelHandlerContext FireExceptionCaught(Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        var next = FindInbound(ChannelHandlerMask.ExceptionCaught);
        if (next == null)
        { Pipeline.Owner.OnUnhandledException(cause); return this; }
        void Invoke()
        {
            if (!next.CanInvoke)
            { next.FireExceptionCaught(cause); return; }
            next.NotifyException(cause);
        }
        if (next.Executor.InEventLoop())
            Invoke();
        else
            next.Executor.Execute(Invoke);
        return this;
    }
    internal void NotifyException(Exception cause)
    {
        if (!CanInvoke)
        { FireExceptionCaught(cause); return; }
        try
        { Handler.ExceptionCaught(this, cause); }
        catch (Exception error) { Pipeline.LogExceptionHandlerFailure(error, cause); }
    }

    private static readonly bool EstimateWriteTaskSize = SystemPropertyUtil.GetBoolean("io.netty.transport.estimateSizeOnSubmit", true);
    // Assuming compressed oops, 12 bytes obj header, 4 ref fields and one int field
    // CLR retains the source default as an accounting estimate for a queued write.
    private static readonly int WriteTaskOverhead = Math.Max(0, SystemPropertyUtil.GetInt("io.netty.transport.writeTaskSizeOverhead", 32));

    private Task Outbound(Func<IChannelOutboundHandler, ChannelHandlerContext, Task> operation,
        CancellationToken cancellationToken, int mask, object message = null)
    {
        if (message != null && cancellationToken.IsCancellationRequested)
        {
            ReferenceCountUtil.Release(message);
            return Task.FromCanceled(cancellationToken);
        }
        var previous = FindOutbound(mask);
        if (previous == null)
        { ReferenceCountUtil.Release(message); return Task.FromException(new InvalidOperationException("No outbound handler.")); }
        Task Invoke()
        {
            if (cancellationToken.IsCancellationRequested)
            { ReferenceCountUtil.Release(message); return Task.FromCanceled(cancellationToken); }
            if (!previous.CanInvoke)
                return previous.Outbound(operation, cancellationToken, mask, message);
            try
            { return operation((IChannelOutboundHandler)previous.Handler, previous); }
            catch (Exception error) { return Task.FromException(error); }
        }
        if (previous.Executor.InEventLoop())
            return Invoke();
        return QueueAsync();
        async Task QueueAsync()
        {
            var buffer = Pipeline.Owner.OutboundBuffer;
            long size = 0;
            bool started = false;
            try
            {
                if (message != null && EstimateWriteTaskSize && !buffer.IsClosed)
                {
                    size = (long)Math.Max(0, Pipeline.EstimatorHandle.Size(message)) + WriteTaskOverhead;
                    buffer.IncrementPendingOutboundBytes(size, notifyLater: true);
                }
                await previous.Executor.SubmitAsync(() =>
                {
                    started = true;
                    buffer.DecrementPendingOutboundBytes(size, notifyLater: true);
                    return Invoke();
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                if (!started)
                {
                    buffer.DecrementPendingOutboundBytes(size, notifyLater: true);
                    ReferenceCountUtil.Release(message);
                }
                throw;
            }
        }
    }
    public virtual Task BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(localAddress); return Outbound((h, c) => h.BindAsync(c, localAddress, cancellationToken), cancellationToken, ChannelHandlerMask.Bind); }
    public virtual Task ConnectAsync(EndPoint remoteAddress, EndPoint localAddress = null, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(remoteAddress); return Outbound((h, c) => h.ConnectAsync(c, remoteAddress, localAddress, cancellationToken), cancellationToken, ChannelHandlerMask.Connect); }
    public virtual Task DisconnectAsync(CancellationToken cancellationToken = default) => Channel.Metadata.HasDisconnect()
        ? Outbound((h, c) => h.DisconnectAsync(c, cancellationToken), cancellationToken, ChannelHandlerMask.Disconnect) : CloseAsync(cancellationToken);
    public virtual Task CloseAsync(CancellationToken cancellationToken = default) => Outbound((h, c) => h.CloseAsync(c, cancellationToken), cancellationToken, ChannelHandlerMask.Close);
    public virtual Task DeregisterAsync(CancellationToken cancellationToken = default) => Outbound((h, c) => h.DeregisterAsync(c, cancellationToken), cancellationToken, ChannelHandlerMask.Deregister);
    public virtual Task WriteAsync(object message, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(message); return Outbound((h, c) => h.WriteAsync(c, message, cancellationToken), cancellationToken, ChannelHandlerMask.Write, message); }
    public virtual Task WriteAndFlushAsync(object message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Outbound((handler, context) =>
        {
            Task write;
            try { write = handler.WriteAsync(context, message, cancellationToken); }
            catch (Exception error) { write = Task.FromException(error); }
            try { handler.Flush(context); }
            catch (Exception error) { context.NotifyException(error); }
            return write;
        }, cancellationToken, ChannelHandlerMask.Write | ChannelHandlerMask.Flush, message);
    }
    private void OutboundVoid(Action<IChannelOutboundHandler, ChannelHandlerContext> action, int mask)
    {
        var previous = FindOutbound(mask);
        if (previous == null)
            return;
        void Invoke()
        {
            if (!previous.CanInvoke)
            { previous.OutboundVoid(action, mask); return; }
            try
            { action((IChannelOutboundHandler)previous.Handler, previous); }
            catch (Exception error) { previous.NotifyException(error); }
        }
        if (previous.Executor.InEventLoop())
            Invoke();
        else
            previous.Executor.Execute(Invoke);
    }
    public virtual IChannelOutboundInvoker Read() { OutboundVoid((h, c) => h.Read(c), ChannelHandlerMask.Read); return this; }
    public virtual IChannelOutboundInvoker Flush() { OutboundVoid((h, c) => h.Flush(c), ChannelHandlerMask.Flush); return this; }
}
