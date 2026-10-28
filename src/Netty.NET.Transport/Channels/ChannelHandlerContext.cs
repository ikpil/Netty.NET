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
public sealed class ChannelHandlerContext : IChannelOutboundInvoker, IAttributeMap
{
    internal ChannelHandlerContext Previous;
    internal ChannelHandlerContext Next;
    internal int State; // 0 pending, 1 added, 2 removed.
    internal bool Linked = true;
    private readonly IEventExecutor _executor;
    internal IEventExecutor AssignedExecutor => _executor;
    internal ChannelHandlerContext(DefaultChannelPipeline pipeline, string name, IChannelHandler handler, IEventExecutor executor = null)
    { Pipeline = pipeline; Name = name; Handler = handler; _executor = executor; }
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
    public bool IsRemoved => State == 2;
    /**
     * @deprecated Use {@link Channel#attr(AttributeKey)}
     */
    public IAttribute<T> Attr<T>(AttributeKey<T> key) where T : class => Channel.Attr(key);
    /**
     * @deprecated Use {@link Channel#hasAttr(AttributeKey)}
     */
    public bool HasAttr<T>(AttributeKey<T> key) where T : class => Channel.HasAttr(key);

    private ChannelHandlerContext FindInbound(bool exception = false)
    {
        var context = Next;
        while (context != null && (context.State != 1 || (!exception && context.Handler is not IChannelInboundHandler)))
            context = context.Next;
        return context;
    }
    private ChannelHandlerContext FindOutbound()
    {
        var context = Previous;
        while (context != null && (context.State != 1 || context.Handler is not IChannelOutboundHandler))
            context = context.Previous;
        return context;
    }
    private ChannelHandlerContext Fire(Action<IChannelInboundHandler, ChannelHandlerContext> callback, object ownedMessage = null)
    {
        var next = FindInbound();
        if (next == null)
        { ReferenceCountUtil.Release(ownedMessage); return this; }
        void Invoke()
        {
            if (next.State != 1)
            { next.Fire(callback, ownedMessage); return; }
            try
            { callback((IChannelInboundHandler)next.Handler, next); }
            catch (Exception error) { next.NotifyException(error); }
        }
        try
        { if (next.Executor.InEventLoop()) Invoke(); else next.Executor.Execute(Invoke); }
        catch { ReferenceCountUtil.Release(ownedMessage); throw; }
        return this;
    }
    public ChannelHandlerContext FireChannelRegistered() => Fire((h, c) => h.ChannelRegistered(c));
    public ChannelHandlerContext FireChannelUnregistered() => Fire((h, c) => h.ChannelUnregistered(c));
    public ChannelHandlerContext FireChannelActive() => Fire((h, c) => h.ChannelActive(c));
    public ChannelHandlerContext FireChannelInactive() => Fire((h, c) => h.ChannelInactive(c));
    public ChannelHandlerContext FireChannelRead(object message)
    { ArgumentNullException.ThrowIfNull(message); return Fire((h, c) => h.ChannelRead(c, message), message); }
    public ChannelHandlerContext FireChannelReadComplete() => Fire((h, c) => h.ChannelReadComplete(c));
    public ChannelHandlerContext FireUserEventTriggered(object evt)
    { ArgumentNullException.ThrowIfNull(evt); return Fire((h, c) => h.UserEventTriggered(c, evt), evt); }
    public ChannelHandlerContext FireChannelWritabilityChanged() => Fire((h, c) => h.ChannelWritabilityChanged(c));
    public ChannelHandlerContext FireExceptionCaught(Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        var next = FindInbound(true);
        if (next == null)
        { Pipeline.Owner.OnUnhandledException(cause); return this; }
        void Invoke() => next.NotifyException(cause);
        if (next.Executor.InEventLoop())
            Invoke();
        else
            next.Executor.Execute(Invoke);
        return this;
    }
    internal void NotifyException(Exception cause)
    {
        try
        { Handler.ExceptionCaught(this, cause); }
        catch (Exception error) { Pipeline.LogExceptionHandlerFailure(error, cause); }
    }

    private Task Outbound(Func<IChannelOutboundHandler, ChannelHandlerContext, Task> operation,
        CancellationToken cancellationToken, object message = null)
    {
        var previous = FindOutbound();
        if (previous == null)
        { ReferenceCountUtil.Release(message); return Task.FromException(new InvalidOperationException("No outbound handler.")); }
        Task Invoke()
        {
            if (cancellationToken.IsCancellationRequested)
            { ReferenceCountUtil.Release(message); return Task.FromCanceled(cancellationToken); }
            if (previous.State != 1)
                return previous.Outbound(operation, cancellationToken, message);
            try
            { return operation((IChannelOutboundHandler)previous.Handler, previous); }
            catch (Exception error) { return Task.FromException(error); }
        }
        if (previous.Executor.InEventLoop())
            return Invoke();
        return QueueAsync();
        async Task QueueAsync()
        {
            int started = 0;
            try
            {
                await previous.Executor.SubmitAsync(() =>
                {
                    started = 1;
                    return Invoke();
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch { if (started == 0) ReferenceCountUtil.Release(message); throw; }
        }
    }
    public Task BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(localAddress); return Outbound((h, c) => h.BindAsync(c, localAddress, cancellationToken), cancellationToken); }
    public Task ConnectAsync(EndPoint remoteAddress, EndPoint localAddress = null, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(remoteAddress); return Outbound((h, c) => h.ConnectAsync(c, remoteAddress, localAddress, cancellationToken), cancellationToken); }
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Channel.Metadata.HasDisconnect()
        ? Outbound((h, c) => h.DisconnectAsync(c, cancellationToken), cancellationToken) : CloseAsync(cancellationToken);
    public Task CloseAsync(CancellationToken cancellationToken = default) => Outbound((h, c) => h.CloseAsync(c, cancellationToken), cancellationToken);
    public Task DeregisterAsync(CancellationToken cancellationToken = default) => Outbound((h, c) => h.DeregisterAsync(c, cancellationToken), cancellationToken);
    public Task WriteAsync(object message, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(message); return Outbound((h, c) => h.WriteAsync(c, message, cancellationToken), cancellationToken, message); }
    public Task WriteAndFlushAsync(object message, CancellationToken cancellationToken = default)
    { var result = WriteAsync(message, cancellationToken); Flush(); return result; }
    private void OutboundVoid(Action<IChannelOutboundHandler, ChannelHandlerContext> action)
    {
        var previous = FindOutbound();
        if (previous == null)
            return;
        void Invoke()
        {
            if (previous.State != 1)
            { previous.OutboundVoid(action); return; }
            try
            { action((IChannelOutboundHandler)previous.Handler, previous); }
            catch (Exception error) { previous.NotifyException(error); }
        }
        if (previous.Executor.InEventLoop())
            Invoke();
        else
            previous.Executor.Execute(Invoke);
    }
    public IChannelOutboundInvoker Read() { OutboundVoid((h, c) => h.Read(c)); return this; }
    public IChannelOutboundInvoker Flush() { OutboundVoid((h, c) => h.Flush(c)); return this; }
}
