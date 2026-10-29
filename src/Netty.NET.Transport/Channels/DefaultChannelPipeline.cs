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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels;

/// <summary>A thread-safe doubly linked handler pipeline with executor-confined callbacks.</summary>
/**
 * The default {@link ChannelPipeline} implementation.  It is usually created
 * by a {@link Channel} implementation when the {@link Channel} is created.
 */
public class DefaultChannelPipeline : IChannelOutboundInvoker, IEnumerable<KeyValuePair<string, IChannelHandler>>
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(DefaultChannelPipeline));
    private readonly object _gate = new();
    private readonly Dictionary<IEventExecutorGroup, IEventExecutor> _executors = new(ReferenceEqualityComparer.Instance);
    private readonly List<(ChannelHandlerContext Context, bool Added)> _pendingCallbacks = new();
    internal readonly ChannelHandlerContext Head;
    internal readonly ChannelHandlerContext Tail;
    private bool _registered;
    private IMessageSizeEstimator.IHandle _estimatorHandle;
    internal IMessageSizeEstimator.IHandle EstimatorHandle
    {
        get
        {
            var handle = Volatile.Read(ref _estimatorHandle);
            if (handle != null)
                return handle;
            handle = Channel.Config.GetMessageSizeEstimator().NewHandle();
            return Interlocked.CompareExchange(ref _estimatorHandle, handle, null) ?? handle;
        }
    }
    internal AbstractChannel Owner { get; }
    /**
     * Returns the {@link Channel} that this pipeline is attached to.
     *
     * @return the channel. {@code null} if this pipeline is not attached yet.
     */
    public IChannel Channel => Owner;
    public DefaultChannelPipeline(AbstractChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        Owner = channel;
        Head = new(this, "DefaultChannelPipeline$HeadContext#0", new HeadHandler(channel)) { State = 1 };
        Tail = new(this, "DefaultChannelPipeline$TailContext#0", new TailHandler(this)) { State = 1 };
        Head.Next = Tail;
        Tail.Previous = Head;
    }
    private ChannelHandlerContext Create(string name, IChannelHandler handler, IEventExecutorGroup group)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (handler is ChannelHandlerAdapter adapter)
            adapter.CheckMultiplicity();
        if (name == null)
        {
            string prefix = handler.GetType().Name + '#';
            int suffix = 0;
            while (Find(prefix + suffix) != null)
                suffix++;
            name = prefix + suffix;
        }
        else if (Find(name) != null || name == Head.Name || name == Tail.Name)
            throw new ArgumentException("Duplicate handler name: " + name, nameof(name));
        IEventExecutor executor = null;
        if (group != null)
        {
            bool pin = !Channel.Config.TryGetOption(ChannelOption.SINGLE_EVENTEXECUTOR_PER_GROUP, out bool configured) || configured;
            if (!pin)
                executor = group.Next();
            else if (!_executors.TryGetValue(group, out executor))
                _executors.Add(group, executor = group.Next());
        }
        return new(this, name, handler, executor);
    }
    private ChannelHandlerContext Find(string name)
    {
        for (var node = Head.Next; node != Tail; node = node.Next)
            if (node.Name == name)
                return node;
        return null;
    }
    private static void Link(ChannelHandlerContext previous, ChannelHandlerContext context, ChannelHandlerContext next)
    { context.Previous = previous; context.Next = next; previous.Next = context; next.Previous = context; }
    private DefaultChannelPipeline Add(string anchor, string name, IChannelHandler handler, IEventExecutorGroup group, int position)
    {
        ChannelHandlerContext context;
        bool notify;
        lock (_gate)
        {
            var adjacent = anchor == null ? (position == 0 ? Head : Tail) : Find(anchor) ?? throw new KeyNotFoundException(anchor);
            context = Create(name, handler, group);
            if (position == 0 || position == 3)
                Link(adjacent, context, adjacent.Next);
            else
                Link(adjacent.Previous, context, adjacent);
            notify = _registered;
            if (!notify)
                _pendingCallbacks.Add((context, true));
        }
        if (notify)
            CallAdded(context);
        return this;
    }
    /**
     * Inserts a {@link ChannelHandler} at the first position of this pipeline.
     *
     * @param name     the name of the handler to insert first
     * @param handler  the handler to insert first
     *
     * @throws IllegalArgumentException
     *         if there's an entry with the same name already in the pipeline
     * @throws NullPointerException
     *         if the specified handler is {@code null}
     */
    public DefaultChannelPipeline AddFirst(string name, IChannelHandler handler) => Add(null, name, handler, null, 0);
    /**
     * Inserts a {@link ChannelHandler} at the first position of this pipeline.
     *
     * @param group    the {@link EventExecutorGroup} which will be used to execute the {@link ChannelHandler}
     *                 methods
     * @param name     the name of the handler to insert first
     * @param handler  the handler to insert first
     *
     * @throws IllegalArgumentException
     *         if there's an entry with the same name already in the pipeline
     * @throws NullPointerException
     *         if the specified handler is {@code null}
     * @deprecated use {@link #addFirst(String, ChannelHandler)}
     */
    public DefaultChannelPipeline AddFirst(IEventExecutorGroup group, string name, IChannelHandler handler) => Add(null, name, handler, group, 0);
    /**
     * Appends a {@link ChannelHandler} at the last position of this pipeline.
     *
     * @param name     the name of the handler to append
     * @param handler  the handler to append
     *
     * @throws IllegalArgumentException
     *         if there's an entry with the same name already in the pipeline
     * @throws NullPointerException
     *         if the specified handler is {@code null}
     */
    public DefaultChannelPipeline AddLast(string name, IChannelHandler handler) => Add(null, name, handler, null, 1);
    /**
     * Appends a {@link ChannelHandler} at the last position of this pipeline.
     *
     * @param group    the {@link EventExecutorGroup} which will be used to execute the {@link ChannelHandler}
     *                 methods
     * @param name     the name of the handler to append
     * @param handler  the handler to append
     *
     * @throws IllegalArgumentException
     *         if there's an entry with the same name already in the pipeline
     * @throws NullPointerException
     *         if the specified handler is {@code null}
     * @deprecated use {@link #addLast(String, ChannelHandler)}
     */
    public DefaultChannelPipeline AddLast(IEventExecutorGroup group, string name, IChannelHandler handler) => Add(null, name, handler, group, 1);
    /**
     * Inserts a {@link ChannelHandler} before an existing handler of this
     * pipeline.
     *
     * @param baseName  the name of the existing handler
     * @param name      the name of the handler to insert before
     * @param handler   the handler to insert before
     *
     * @throws NoSuchElementException
     *         if there's no such entry with the specified {@code baseName}
     * @throws IllegalArgumentException
     *         if there's an entry with the same name already in the pipeline
     * @throws NullPointerException
     *         if the specified baseName or handler is {@code null}
     */
    public DefaultChannelPipeline AddBefore(string anchor, string name, IChannelHandler handler) => Add(anchor, name, handler, null, 2);
    /**
     * Inserts a {@link ChannelHandler} after an existing handler of this
     * pipeline.
     *
     * @param baseName  the name of the existing handler
     * @param name      the name of the handler to insert after
     * @param handler   the handler to insert after
     *
     * @throws NoSuchElementException
     *         if there's no such entry with the specified {@code baseName}
     * @throws IllegalArgumentException
     *         if there's an entry with the same name already in the pipeline
     * @throws NullPointerException
     *         if the specified baseName or handler is {@code null}
     */
    public DefaultChannelPipeline AddAfter(string anchor, string name, IChannelHandler handler) => Add(anchor, name, handler, null, 3);
    /**
     * Inserts a {@link ChannelHandler} before an existing handler of this
     * pipeline.
     *
     * @param group     the {@link EventExecutorGroup} which will be used to execute the {@link ChannelHandler}
     *                  methods
     * @param baseName  the name of the existing handler
     * @param name      the name of the handler to insert before
     * @param handler   the handler to insert before
     *
     * @throws NoSuchElementException
     *         if there's no such entry with the specified {@code baseName}
     * @throws IllegalArgumentException
     *         if there's an entry with the same name already in the pipeline
     * @throws NullPointerException
     *         if the specified baseName or handler is {@code null}
     * @deprecated use {@link #addBefore(String, String, ChannelHandler)}
     */
    public DefaultChannelPipeline AddBefore(IEventExecutorGroup group, string anchor, string name, IChannelHandler handler) => Add(anchor, name, handler, group, 2);
    /**
     * Inserts a {@link ChannelHandler} after an existing handler of this
     * pipeline.
     *
     * @param group     the {@link EventExecutorGroup} which will be used to execute the {@link ChannelHandler}
     *                  methods
     * @param baseName  the name of the existing handler
     * @param name      the name of the handler to insert after
     * @param handler   the handler to insert after
     *
     * @throws NoSuchElementException
     *         if there's no such entry with the specified {@code baseName}
     * @throws IllegalArgumentException
     *         if there's an entry with the same name already in the pipeline
     * @throws NullPointerException
     *         if the specified baseName or handler is {@code null}
     * @deprecated use {@link #addAfter(String, String, ChannelHandler)}
     */
    public DefaultChannelPipeline AddAfter(IEventExecutorGroup group, string anchor, string name, IChannelHandler handler) => Add(anchor, name, handler, group, 3);
    /**
     * Inserts {@link ChannelHandler}s at the last position of this pipeline.
     *
     * @param handlers  the handlers to insert last
     *
     */
    public DefaultChannelPipeline AddLast(params IChannelHandler[] handlers)
    { ArgumentNullException.ThrowIfNull(handlers); foreach (var handler in handlers) { if (handler == null) break; AddLast((string)null, handler); } return this; }
    /**
     * Inserts {@link ChannelHandler}s at the first position of this pipeline.
     *
     * @param handlers  the handlers to insert first
     *
     */
    public DefaultChannelPipeline AddFirst(params IChannelHandler[] handlers)
    { ArgumentNullException.ThrowIfNull(handlers); int count = Array.IndexOf(handlers, null); if (count < 0) count = handlers.Length; for (int i = count - 1; i >= 0; i--) AddFirst((string)null, handlers[i]); return this; }
    /**
     * Returns the context object of the {@link ChannelHandler} with the
     * specified name in this pipeline.
     *
     * @return the context object of the handler with the specified name.
     *         {@code null} if there's no such handler in this pipeline.
     */
    /**
     * Returns the context object of the {@link ChannelHandler} of the
     * specified type in this pipeline.
     *
     * @return the context object of the handler of the specified type.
     *         {@code null} if there's no such handler in this pipeline.
     */
    public ChannelHandlerContext Context(string name) { lock (_gate) return Find(name); }
    /**
     * Returns the context object of the specified {@link ChannelHandler} in
     * this pipeline.
     *
     * @return the context object of the specified handler.
     *         {@code null} if there's no such handler in this pipeline.
     */
    public ChannelHandlerContext Context(IChannelHandler handler)
    { lock (_gate) { for (var c = Head.Next; c != Tail; c = c.Next) if (ReferenceEquals(c.Handler, handler)) return c; return null; } }
    public ChannelHandlerContext Context(Type type)
    { lock (_gate) { for (var c = Head.Next; c != Tail; c = c.Next) if (type.IsInstanceOfType(c.Handler)) return c; return null; } }
    /**
     * Returns the {@link ChannelHandler} with the specified name in this
     * pipeline.
     *
     * @return the handler with the specified name.
     *         {@code null} if there's no such handler in this pipeline.
     */
    /**
     * Returns the {@link ChannelHandler} of the specified type in this
     * pipeline.
     *
     * @return the handler of the specified handler type.
     *         {@code null} if there's no such handler in this pipeline.
     */
    public IChannelHandler Get(string name) => Context(name)?.Handler;
    public T Get<T>() where T : class, IChannelHandler => Context(typeof(T))?.Handler as T;
    /**
     * Returns the first {@link ChannelHandler} in this pipeline.
     *
     * @return the first handler.  {@code null} if this pipeline is empty.
     */
    public IChannelHandler First => FirstContext?.Handler;
    /**
     * Returns the last {@link ChannelHandler} in this pipeline.
     *
     * @return the last handler.  {@code null} if this pipeline is empty.
     */
    public IChannelHandler Last => LastContext?.Handler;
    /**
     * Returns the context of the first {@link ChannelHandler} in this pipeline.
     *
     * @return the context of the first handler.  {@code null} if this pipeline is empty.
     */
    public ChannelHandlerContext FirstContext { get { lock (_gate) return Head.Next == Tail ? null : Head.Next; } }
    /**
     * Returns the context of the last {@link ChannelHandler} in this pipeline.
     *
     * @return the context of the last handler.  {@code null} if this pipeline is empty.
     */
    public ChannelHandlerContext LastContext { get { lock (_gate) return Tail.Previous == Head ? null : Tail.Previous; } }
    /**
     * Removes the {@link ChannelHandler} with the specified name from this pipeline.
     *
     * @param  name             the name under which the {@link ChannelHandler} was stored.
     *
     * @return the removed handler
     *
     * @throws NoSuchElementException
     *         if there's no such handler with the specified name in this pipeline
     * @throws NullPointerException
     *         if the specified name is {@code null}
     */
    /**
     * Removes the {@link ChannelHandler} of the specified type from this pipeline.
     *
     * @param <T>           the type of the handler
     * @param handlerType   the type of the handler
     *
     * @return the removed handler
     *
     * @throws NoSuchElementException
     *         if there's no such handler of the specified type in this pipeline
     * @throws NullPointerException
     *         if the specified handler type is {@code null}
     */
    public IChannelHandler Remove(string name) => RemoveContext(Context(name) ?? throw new KeyNotFoundException(name));
    /**
     * Removes the specified {@link ChannelHandler} from this pipeline.
     *
     * @param  handler          the {@link ChannelHandler} to remove
     *
     * @throws NoSuchElementException
     *         if there's no such handler in this pipeline
     * @throws NullPointerException
     *         if the specified handler is {@code null}
     */
    public DefaultChannelPipeline Remove(IChannelHandler handler) { RemoveContext(Context(handler) ?? throw new KeyNotFoundException()); return this; }
    public T Remove<T>() where T : class, IChannelHandler => (T)RemoveContext(Context(typeof(T)) ?? throw new KeyNotFoundException());
    public IChannelHandler RemoveIfExists(string name) { var c = Context(name); return c == null ? null : RemoveContext(c); }
    /**
     * Removes the first {@link ChannelHandler} in this pipeline.
     *
     * @return the removed handler
     *
     * @throws NoSuchElementException
     *         if this pipeline is empty
     */
    public IChannelHandler RemoveFirst() => RemoveContext(FirstContext ?? throw new InvalidOperationException("Empty pipeline."));
    /**
     * Removes the last {@link ChannelHandler} in this pipeline.
     *
     * @return the removed handler
     *
     * @throws NoSuchElementException
     *         if this pipeline is empty
     */
    public IChannelHandler RemoveLast() => RemoveContext(LastContext ?? throw new InvalidOperationException("Empty pipeline."));
    private IChannelHandler RemoveContext(ChannelHandlerContext context)
    {
        lock (_gate)
        {
            if (!context.Linked)
                throw new KeyNotFoundException(context.Name);
            context.Previous.Next = context.Next;
            context.Next.Previous = context.Previous;
            context.Linked = false;
            if (!_registered)
                _pendingCallbacks.Add((context, false));
        }
        if (_registered)
            CallRemoved(context);
        return context.Handler;
    }
    /**
     * Replaces the {@link ChannelHandler} of the specified name with a new handler in this pipeline.
     *
     * @param  oldName       the name of the {@link ChannelHandler} to be replaced
     * @param  newName       the name under which the replacement should be added
     * @param  newHandler    the {@link ChannelHandler} which is used as replacement
     *
     * @return the removed handler
     *
     * @throws NoSuchElementException
     *         if the handler with the specified old name does not exist in this pipeline
     * @throws IllegalArgumentException
     *         if a handler with the specified new name already exists in this
     *         pipeline, except for the handler to be replaced
     * @throws NullPointerException
     *         if the specified old handler or new handler is
     *         {@code null}
     */
    /**
     * Replaces the {@link ChannelHandler} of the specified type with a new handler in this pipeline.
     *
     * @param  oldHandlerType   the type of the handler to be removed
     * @param  newName          the name under which the replacement should be added
     * @param  newHandler       the {@link ChannelHandler} which is used as replacement
     *
     * @return the removed handler
     *
     * @throws NoSuchElementException
     *         if the handler of the specified old handler type does not exist
     *         in this pipeline
     * @throws IllegalArgumentException
     *         if a handler with the specified new name already exists in this
     *         pipeline, except for the handler to be replaced
     * @throws NullPointerException
     *         if the specified old handler or new handler is
     *         {@code null}
     */
    public IChannelHandler Replace(string oldName, string newName, IChannelHandler handler)
        => ReplaceContext(Context(oldName) ?? throw new KeyNotFoundException(oldName), newName, handler);
    /**
     * Replaces the specified {@link ChannelHandler} with a new handler in this pipeline.
     *
     * @param  oldHandler    the {@link ChannelHandler} to be replaced
     * @param  newName       the name under which the replacement should be added
     * @param  newHandler    the {@link ChannelHandler} which is used as replacement
     *
     * @return itself

     * @throws NoSuchElementException
     *         if the specified old handler does not exist in this pipeline
     * @throws IllegalArgumentException
     *         if a handler with the specified new name already exists in this
     *         pipeline, except for the handler to be replaced
     * @throws NullPointerException
     *         if the specified old handler or new handler is
     *         {@code null}
     */
    public DefaultChannelPipeline Replace(IChannelHandler oldHandler, string newName, IChannelHandler handler)
    { ReplaceContext(Context(oldHandler) ?? throw new KeyNotFoundException(), newName, handler); return this; }
    private IChannelHandler ReplaceContext(ChannelHandlerContext old, string newName, IChannelHandler handler)
    {
        ChannelHandlerContext next;
        lock (_gate)
        {
            if (!old.Linked)
                throw new KeyNotFoundException(old.Name);
            string oldName = old.Name;
            var candidate = Create(newName == oldName ? null : newName, handler, null);
            next = new(this, newName == oldName ? oldName : candidate.Name, handler, old.AssignedExecutor);
            Link(old.Previous, next, old.Next);
            old.Linked = false;
            // A removed context continues through its replacement, including reentrant callbacks.
            old.Previous = old.Next = next;
            if (!_registered)
            { _pendingCallbacks.Add((next, true)); _pendingCallbacks.Add((old, false)); }
        }
        if (_registered)
        { CallAdded(next); CallRemoved(old); }
        return old.Handler;
    }
    /**
     * Returns the {@link List} of the handler names.
     */
    public IReadOnlyList<string> Names()
    { lock (_gate) { var names = new List<string>(); for (var c = Head.Next; c != null; c = c.Next) names.Add(c.Name); return names; } }
    public IReadOnlyDictionary<string, IChannelHandler> ToMap()
    { lock (_gate) { var map = new Dictionary<string, IChannelHandler>(); for (var c = Head.Next; c != Tail; c = c.Next) map.Add(c.Name, c.Handler); return map; } }
    public IEnumerator<KeyValuePair<string, IChannelHandler>> GetEnumerator() => ToMap().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    internal void Registered()
    {
        (ChannelHandlerContext Context, bool Added)[] pending;
        lock (_gate)
        {
            if (_registered)
                return;
            _registered = true;
            pending = _pendingCallbacks.ToArray();
            _pendingCallbacks.Clear();
        }
        foreach (var callback in pending)
        { if (callback.Added) CallAdded(callback.Context); else CallRemoved(callback.Context); }
    }
    private void CallAdded(ChannelHandlerContext context)
    {
        void Invoke()
        {
            if (context.State != 0)
                return;
            context.State = 1;
            try
            { context.Handler.HandlerAdded(context); }
            catch (Exception error)
            {
                if (!context.IsRemoved && Context(context.Handler) == context)
                    RemoveContext(context);
                FireExceptionCaught(new ChannelPipelineException("HandlerAdded failed: " + context.Name, error));
            }
        }
        if (context.Executor.InEventLoop())
            Invoke();
        else
            context.Executor.Execute(Invoke);
    }
    private void CallRemoved(ChannelHandlerContext context)
    {
        if (context.Executor.InEventLoop())
            CallRemoved0(context);
        else
            context.Executor.Execute(() => CallRemoved0(context));
    }
    private void CallRemoved0(ChannelHandlerContext context)
    {
        if (context.State == 2)
            return;
        bool added = context.State == 1;
        context.State = 2;
        if (!added)
            return;
        try
        { context.Handler.HandlerRemoved(context); }
        catch (Exception error) { FireExceptionCaught(new ChannelPipelineException("HandlerRemoved failed: " + context.Name, error)); }
    }
    /**
     * Removes all handlers from the pipeline one by one from tail (exclusive) to head (exclusive) to trigger
     * handlerRemoved().
     *
     * Note that we traverse up the pipeline ({@link #destroyUp(AbstractChannelHandlerContext, boolean)})
     * before traversing down ({@link #destroyDown(Thread, AbstractChannelHandlerContext, boolean)}) so that
     * the handlers are removed after all events are handled.
     *
     * See: https://github.com/netty/netty/issues/3156
     */
    internal void Destroy()
    {
        lock (_gate)
            DestroyUp(Head.Next, false);
    }
    private void DestroyUp(ChannelHandlerContext context, bool inEventLoop)
    {
        for (; ; )
        {
            if (context == Tail)
            { DestroyDown(Tail.Previous, inEventLoop); return; }
            if (!inEventLoop && !context.Executor.InEventLoop())
            {
                var next = context;
                context.Executor.Execute(() => DestroyUp(next, true));
                return;
            }
            context = context.Next;
            inEventLoop = false;
        }
    }
    private void DestroyDown(ChannelHandlerContext context, bool inEventLoop)
    {
        // We have reached at tail; now traverse backwards.
        while (context != Head)
        {
            if (inEventLoop || context.Executor.InEventLoop())
            {
                bool removed;
                // Skip a context that a handlerRemoved(...) callback already removed or replaced.
                lock (_gate)
                {
                    removed = context.Linked;
                    if (removed)
                    { context.Previous.Next = context.Next; context.Next.Previous = context.Previous; context.Linked = false; }
                }
                if (removed)
                    CallRemoved0(context);
            }
            else
            {
                var next = context;
                context.Executor.Execute(() => DestroyDown(next, true));
                return;
            }
            context = context.Previous;
            inEventLoop = false;
        }
    }
    internal void LogExceptionHandlerFailure(Exception error, Exception original) => Logger.Warn("An ExceptionCaught callback threw while handling an exception.", error);
    public DefaultChannelPipeline FireChannelRegistered() { Head.FireChannelRegistered(); return this; }
    public DefaultChannelPipeline FireChannelUnregistered() { Head.FireChannelUnregistered(); return this; }
    public DefaultChannelPipeline FireChannelActive() { Head.FireChannelActive(); return this; }
    public DefaultChannelPipeline FireChannelInactive() { Head.FireChannelInactive(); return this; }
    public DefaultChannelPipeline FireChannelRead(object message) { Head.FireChannelRead(message); return this; }
    public DefaultChannelPipeline FireChannelReadComplete() { Head.FireChannelReadComplete(); return this; }
    public DefaultChannelPipeline FireUserEventTriggered(object evt) { Head.FireUserEventTriggered(evt); return this; }
    public DefaultChannelPipeline FireExceptionCaught(Exception cause) { Head.FireExceptionCaught(cause); return this; }
    public DefaultChannelPipeline FireChannelWritabilityChanged() { Head.FireChannelWritabilityChanged(); return this; }
    public Task BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default) => Tail.BindAsync(localAddress, cancellationToken);
    public Task ConnectAsync(EndPoint remoteAddress, EndPoint localAddress = null, CancellationToken cancellationToken = default) => Tail.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Tail.DisconnectAsync(cancellationToken);
    public Task CloseAsync(CancellationToken cancellationToken = default) => Tail.CloseAsync(cancellationToken);
    public Task DeregisterAsync(CancellationToken cancellationToken = default) => Tail.DeregisterAsync(cancellationToken);
    public Task WriteAsync(object message, CancellationToken cancellationToken = default) => Tail.WriteAsync(message, cancellationToken);
    public Task WriteAndFlushAsync(object message, CancellationToken cancellationToken = default) => Tail.WriteAndFlushAsync(message, cancellationToken);
    public IChannelOutboundInvoker Read() { Tail.Read(); return this; }
    public IChannelOutboundInvoker Flush() { Tail.Flush(); return this; }

    /**
     * Called once the {@link ChannelInboundHandler#channelActive(ChannelHandlerContext)}event hit
     * the end of the {@link ChannelPipeline}.
     */
    protected virtual void OnUnhandledInboundChannelActive() { }
    /**
     * Called once the {@link ChannelInboundHandler#channelInactive(ChannelHandlerContext)} event hit
     * the end of the {@link ChannelPipeline}.
     */
    protected virtual void OnUnhandledInboundChannelInactive() { }
    /**
     * Called once a {@link Throwable} hit the end of the {@link ChannelPipeline} without been handled by the user
     * in {@link ChannelHandler#exceptionCaught(ChannelHandlerContext, Throwable)}.
     */
    protected virtual void OnUnhandledInboundException(Exception error) => Owner.OnUnhandledException(error);
    /**
     * Called once a message hit the end of the {@link ChannelPipeline} without been handled by the user
     * in {@link ChannelInboundHandler#channelRead(ChannelHandlerContext, Object)}. This method is responsible
     * to call {@link ReferenceCountUtil#release(Object)} on the given msg at some point.
     */
    protected virtual void OnUnhandledInboundMessage(object message) => Owner.OnUnhandledInboundMessage(message);
    protected virtual void OnUnhandledInboundMessage(ChannelHandlerContext context, object message) => OnUnhandledInboundMessage(message);
    /**
     * Called once the {@link ChannelInboundHandler#channelReadComplete(ChannelHandlerContext)} event hit
     * the end of the {@link ChannelPipeline}.
     */
    protected virtual void OnUnhandledInboundChannelReadComplete() { }
    /**
     * Called once an user event hit the end of the {@link ChannelPipeline} without been handled by the user
     * in {@link ChannelInboundHandler#userEventTriggered(ChannelHandlerContext, Object)}. This method is responsible
     * to call {@link ReferenceCountUtil#release(Object)} on the given event at some point.
     */
    protected virtual void OnUnhandledInboundUserEventTriggered(object evt) => Owner.OnUnhandledUserEvent(evt);
    /**
     * Called once the {@link ChannelInboundHandler#channelWritabilityChanged(ChannelHandlerContext)} event hit
     * the end of the {@link ChannelPipeline}.
     */
    protected virtual void OnUnhandledChannelWritabilityChanged() { }

    [Sharable]
    private sealed class TailHandler(DefaultChannelPipeline pipeline) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context) => pipeline.OnUnhandledInboundChannelActive();
        public override void ChannelInactive(ChannelHandlerContext context) => pipeline.OnUnhandledInboundChannelInactive();
        public override void ChannelRead(ChannelHandlerContext context, object message) => pipeline.OnUnhandledInboundMessage(context, message);
        public override void ChannelReadComplete(ChannelHandlerContext context) => pipeline.OnUnhandledInboundChannelReadComplete();
        public override void ChannelWritabilityChanged(ChannelHandlerContext context) => pipeline.OnUnhandledChannelWritabilityChanged();
        public override void UserEventTriggered(ChannelHandlerContext context, object evt) => pipeline.OnUnhandledInboundUserEventTriggered(evt);
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => pipeline.OnUnhandledInboundException(cause);
    }
    [Sharable]
    private sealed class HeadHandler(AbstractChannel channel) : ChannelOutboundHandlerAdapter
    {
        public override Task BindAsync(ChannelHandlerContext context, EndPoint localAddress, CancellationToken cancellationToken) => channel.BindTransportAsync(localAddress, cancellationToken);
        public override Task ConnectAsync(ChannelHandlerContext context, EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken) => channel.ConnectTransportAsync(remoteAddress, localAddress, cancellationToken);
        public override Task DisconnectAsync(ChannelHandlerContext context, CancellationToken cancellationToken) => channel.DisconnectTransportAsync(cancellationToken);
        public override Task CloseAsync(ChannelHandlerContext context, CancellationToken cancellationToken) => channel.CloseTransportAsync();
        public override Task DeregisterAsync(ChannelHandlerContext context, CancellationToken cancellationToken) => channel.DeregisterTransportAsync();
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken cancellationToken) => channel.WriteTransportAsync(message, cancellationToken);
        public override void Read(ChannelHandlerContext context) => channel.BeginRead();
        public override void Flush(ChannelHandlerContext context) => channel.FlushTransport();
    }
}
