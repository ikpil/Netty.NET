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
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels;

/// <summary>A thread-safe doubly linked handler pipeline with executor-confined callbacks.</summary>
/**
 * The default {@link ChannelPipeline} implementation.  It is usually created
 * by a {@link Channel} implementation when the {@link Channel} is created.
 */
public class DefaultChannelPipeline : IChannelPipeline
{
    internal static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(DefaultChannelPipeline));

    // CLR: the JVM binary names generateName0(HeadContext.class)/generateName0(TailContext.class) are kept as text;
    // StringUtil.SimpleClassName spells a nested CLR type Outer+Inner.
    private const string HEAD_NAME = "DefaultChannelPipeline$HeadContext#0";
    private const string TAIL_NAME = "DefaultChannelPipeline$TailContext#0";

    // CLR: a weak, thread-safe ConditionalWeakTable replaces the per-thread WeakHashMap in a FastThreadLocal.
    private static readonly ConditionalWeakTable<Type, string> nameCaches = new();

    // CLR: Volatile.Read and Interlocked.CompareExchange on _estimatorHandle replace the AtomicReferenceFieldUpdater
    // ESTIMATOR (https://learn.microsoft.com/dotnet/api/system.threading.interlocked.compareexchange).
    internal readonly HeadContext Head;
    internal readonly TailContext Tail;

    // CLR: the channel is the AbstractChannel that folds Channel.Unsafe (2026-11-16 owner decision); there is no
    // succeededFuture or voidPromise, because every operation returns its own Task.
    private readonly AbstractChannel _channel;
    private readonly bool _touch = ResourceLeakDetector.IsEnabled();

    // CLR: a private lock object stands for synchronized (this)
    // (https://learn.microsoft.com/dotnet/csharp/language-reference/statements/lock).
    private readonly object _gate = new();

    private Dictionary<IEventExecutorGroup, IEventExecutor> _childExecutors;
    private IMessageSizeEstimator.IHandle _estimatorHandle;
    private bool _firstRegistration = true;

    /**
     * This is the head of a linked list that is processed by {@link #callHandlerAddedForAllHandlers()} and so process
     * all the pending {@link #callHandlerAdded0(AbstractChannelHandlerContext)}.
     * <p>
     * We only keep the head because it is expected that the list is used infrequently and its size is small.
     * Thus full iterations to do insertions is assumed to be a good compromised to saving memory and tail management
     * complexity.
     */
    private PendingHandlerCallback _pendingHandlerCallbackHead;

    /**
     * Set to {@code true} once the {@link AbstractChannel} is registered.Once set to {@code true} the value will never
     * change.
     */
    private bool _registered;

    // CLR: protected internal, because AbstractChannel.NewChannelPipeline() creates it outside a subclass.
    protected internal DefaultChannelPipeline(AbstractChannel channel)
    {
        _channel = ObjectUtil.CheckNotNull(channel, "channel");

        Tail = new TailContext(this);
        Head = new HeadContext(this);

        Head.Next = Tail;
        Tail.Prev = Head;
    }

    internal IMessageSizeEstimator.IHandle EstimatorHandle()
    {
        IMessageSizeEstimator.IHandle handle = Volatile.Read(ref _estimatorHandle);
        if (handle == null)
        {
            handle = _channel.Config.GetMessageSizeEstimator().NewHandle();
            IMessageSizeEstimator.IHandle witness = Interlocked.CompareExchange(ref _estimatorHandle, handle, null);
            if (witness != null)
            {
                handle = witness;
            }
        }

        return handle;
    }

    internal object Touch(object msg, AbstractChannelHandlerContext next)
    {
        return _touch ? ReferenceCountUtil.Touch(msg, next) : msg;
    }

    // CLR: the channel with Channel.Unsafe folded in (see the field).
    internal AbstractChannel Owner => _channel;

    private AbstractChannelHandlerContext NewContext(IEventExecutorGroup group, string name, IChannelHandler handler)
    {
        return new DefaultChannelHandlerContext(this, ChildExecutor(group), name, handler);
    }

    private IEventExecutor ChildExecutor(IEventExecutorGroup group)
    {
        if (group == null)
        {
            return null;
        }
        // CLR: TryGetOption reports an unset option as false where getOption returns null.
        if (_channel.Config.TryGetOption(ChannelOption.SINGLE_EVENTEXECUTOR_PER_GROUP, out bool pinEventExecutor) &&
            !pinEventExecutor)
        {
            return group.Next();
        }

        Dictionary<IEventExecutorGroup, IEventExecutor> childExecutors = _childExecutors;
        if (childExecutors == null)
        {
            // Use size of 4 as most people only use one extra EventExecutor.
            // CLR: ReferenceEqualityComparer makes the Dictionary an identity map, as IdentityHashMap is
            // (https://learn.microsoft.com/dotnet/api/system.collections.generic.referenceequalitycomparer).
            childExecutors = _childExecutors =
                new Dictionary<IEventExecutorGroup, IEventExecutor>(4, ReferenceEqualityComparer.Instance);
        }

        // Pin one of the child executors once and remember it so that the same child executor
        // is used to fire events for the same channel.
        if (!childExecutors.TryGetValue(group, out IEventExecutor childExecutor))
        {
            childExecutor = group.Next();
            childExecutors[group] = childExecutor;
        }

        return childExecutor;
    }

    public IChannel Channel => _channel;

    public IChannelPipeline AddFirst(string name, IChannelHandler handler)
    {
        return AddFirst(null, name, handler);
    }

    private enum AddStrategy
    {
        ADD_FIRST,
        ADD_LAST,
        ADD_BEFORE,
        ADD_AFTER
    }

    private IChannelPipeline InternalAdd(IEventExecutorGroup group, string name,
        IChannelHandler handler, string baseName,
        AddStrategy addStrategy)
    {
        // CLR: the source's NullPointerException comes from handler.getClass() while the context is created.
        ObjectUtil.CheckNotNull(handler, "handler");

        AbstractChannelHandlerContext newCtx;

        lock (_gate)
        {
            CheckMultiplicity(handler);
            name = FilterName(name, handler);

            newCtx = NewContext(group, name, handler);

            switch (addStrategy)
            {
                case AddStrategy.ADD_FIRST:
                    AddFirst0(newCtx);
                    break;
                case AddStrategy.ADD_LAST:
                    AddLast0(newCtx);
                    break;
                case AddStrategy.ADD_BEFORE:
                    AddBefore0(GetContextOrDie(baseName), newCtx);
                    break;
                case AddStrategy.ADD_AFTER:
                    AddAfter0(GetContextOrDie(baseName), newCtx);
                    break;
                default:
                    throw new ArgumentException("unknown add strategy: " + addStrategy);
            }

            // If the registered is false it means that the channel was not registered on an eventLoop yet.
            // In this case we add the context to the pipeline and add a task that will call
            // ChannelHandler.handlerAdded(...) once the channel is registered.
            if (!_registered)
            {
                newCtx.SetAddPending();
                CallHandlerCallbackLater(newCtx, true);
                return this;
            }

            IEventExecutor executor = newCtx.Executor;
            if (!executor.InEventLoop())
            {
                CallHandlerAddedInEventLoop(newCtx, executor);
                return this;
            }
        }

        CallHandlerAdded0(newCtx);
        return this;
    }

    public IChannelPipeline AddFirst(IEventExecutorGroup group, string name, IChannelHandler handler)
    {
        return InternalAdd(group, name, handler, null, AddStrategy.ADD_FIRST);
    }

    private void AddFirst0(AbstractChannelHandlerContext newCtx)
    {
        AbstractChannelHandlerContext nextCtx = Head.Next;
        newCtx.Prev = Head;
        newCtx.Next = nextCtx;
        Head.Next = newCtx;
        nextCtx.Prev = newCtx;
    }

    public IChannelPipeline AddLast(string name, IChannelHandler handler)
    {
        return AddLast(null, name, handler);
    }

    public IChannelPipeline AddLast(IEventExecutorGroup group, string name, IChannelHandler handler)
    {
        return InternalAdd(group, name, handler, null, AddStrategy.ADD_LAST);
    }

    private void AddLast0(AbstractChannelHandlerContext newCtx)
    {
        AbstractChannelHandlerContext prev = Tail.Prev;
        newCtx.Prev = prev;
        newCtx.Next = Tail;
        prev.Next = newCtx;
        Tail.Prev = newCtx;
    }

    public IChannelPipeline AddBefore(string baseName, string name, IChannelHandler handler)
    {
        return AddBefore(null, baseName, name, handler);
    }

    public IChannelPipeline AddBefore(
        IEventExecutorGroup group, string baseName, string name, IChannelHandler handler)
    {
        return InternalAdd(group, name, handler, baseName, AddStrategy.ADD_BEFORE);
    }

    private static void AddBefore0(AbstractChannelHandlerContext ctx, AbstractChannelHandlerContext newCtx)
    {
        newCtx.Prev = ctx.Prev;
        newCtx.Next = ctx;
        ctx.Prev.Next = newCtx;
        ctx.Prev = newCtx;
    }

    private string FilterName(string name, IChannelHandler handler)
    {
        if (name == null)
        {
            return GenerateName(handler);
        }

        CheckDuplicateName(name);
        return name;
    }

    public IChannelPipeline AddAfter(string baseName, string name, IChannelHandler handler)
    {
        return AddAfter(null, baseName, name, handler);
    }

    public IChannelPipeline AddAfter(
        IEventExecutorGroup group, string baseName, string name, IChannelHandler handler)
    {
        return InternalAdd(group, name, handler, baseName, AddStrategy.ADD_AFTER);
    }

    private static void AddAfter0(AbstractChannelHandlerContext ctx, AbstractChannelHandlerContext newCtx)
    {
        newCtx.Prev = ctx;
        newCtx.Next = ctx.Next;
        ctx.Next.Prev = newCtx;
        ctx.Next = newCtx;
    }

    public IChannelPipeline AddFirst(params IChannelHandler[] handlers)
    {
        ObjectUtil.CheckNotNull(handlers, "handlers");
        if (handlers.Length == 0 || handlers[0] == null)
        {
            return this;
        }

        int size;

        for (size = 1; size < handlers.Length; size++)
        {
            if (handlers[size] == null)
            {
                break;
            }
        }

        for (int i = size - 1; i >= 0; i--)
        {
            IChannelHandler h = handlers[i];
            AddFirst((IEventExecutorGroup)null, null, h);
        }

        return this;
    }

    public IChannelPipeline AddLast(params IChannelHandler[] handlers)
    {
        ObjectUtil.CheckNotNull(handlers, "handlers");

        foreach (IChannelHandler h in handlers)
        {
            if (h == null)
            {
                break;
            }

            AddLast((IEventExecutorGroup)null, null, h);
        }

        return this;
    }

    private string GenerateName(IChannelHandler handler)
    {
        Type handlerType = handler.GetType();
        string name = nameCaches.GetValue(handlerType, GenerateName0);

        // It's not very likely for a user to put more than one handler of the same type, but make sure to avoid
        // any name conflicts.  Note that we don't cache the names generated here.
        if (Context0(name) != null)
        {
            string baseName = name.Substring(0, name.Length - 1); // Strip the trailing '0'.

            for (int i = 1; ; i++)
            {
                string newName = baseName + i.ToString(CultureInfo.InvariantCulture);
                if (Context0(newName) == null)
                {
                    name = newName;
                    break;
                }
            }
        }

        return name;
    }

    // CLR: StringUtil.SimpleClassName is the namespace-less Type.FullName, so nested types read Outer+Inner where the
    // JVM binary name reads Outer$Inner.
    private static string GenerateName0(Type handlerType)
    {
        return StringUtil.SimpleClassName(handlerType) + "#0";
    }

    public IChannelPipeline Remove(IChannelHandler handler)
    {
        Remove(GetContextOrDie(handler));
        return this;
    }

    public IChannelHandler Remove(string name)
    {
        return Remove(GetContextOrDie(name)).Handler;
    }

    public T Remove<T>() where T : class, IChannelHandler
    {
        return (T)Remove(GetContextOrDie(typeof(T))).Handler;
    }

    public IChannelHandler RemoveIfExists(string name)
    {
        return RemoveIfExists(Context(name));
    }

    public IChannelHandler RemoveIfExists(Type handlerType)
    {
        return RemoveIfExists(Context(handlerType));
    }

    public IChannelHandler RemoveIfExists(IChannelHandler handler)
    {
        return RemoveIfExists(Context(handler));
    }

    private IChannelHandler RemoveIfExists(IChannelHandlerContext ctx)
    {
        if (ctx == null || !TryRemove((AbstractChannelHandlerContext)ctx))
        {
            return null;
        }

        return ctx.Handler;
    }

    private AbstractChannelHandlerContext Remove(AbstractChannelHandlerContext ctx)
    {
        if (!TryRemove(ctx))
        {
            // The context was removed or replaced concurrently after it was looked up.
            throw new KeyNotFoundException(ctx.Name);
        }

        return ctx;
    }

    /**
     * Removes the given context from the pipeline and schedules the call of
     * {@link ChannelHandler#handlerRemoved(ChannelHandlerContext)}.
     *
     * @return {@code false} if the context was not part of the pipeline anymore, in which case nothing is done.
     */
    private bool TryRemove(AbstractChannelHandlerContext ctx)
    {
        Debug.Assert(ctx != Head && ctx != Tail);

        lock (_gate)
        {
            if (!AtomicRemoveFromHandlerList(ctx))
            {
                return false;
            }

            // If the registered is false it means that the channel was not registered on an eventloop yet.
            // In this case we remove the context from the pipeline and add a task that will call
            // ChannelHandler.handlerRemoved(...) once the channel is registered.
            if (!_registered)
            {
                CallHandlerCallbackLater(ctx, false);
                return true;
            }

            IEventExecutor executor = ctx.Executor;
            if (!executor.InEventLoop())
            {
                executor.Execute(() => CallHandlerRemoved0(ctx));
                return true;
            }
        }

        CallHandlerRemoved0(ctx);
        return true;
    }

    /**
     * Returns {@code true} if the given context is still linked into the pipeline. A removed context keeps its
     * {@code prev} and {@code next} pointers (and a replaced context points to its replacement), so the check must
     * be done by looking at the neighbour.
     */
    private bool IsLinked(AbstractChannelHandlerContext ctx)
    {
        Debug.Assert(Monitor.IsEntered(_gate));
        return ctx.Prev.Next == ctx;
    }

    /**
     * Method is synchronized to make the handler removal from the double linked list atomic.
     *
     * @return {@code false} if the context was already removed or replaced, in which case the list is not modified.
     */
    private bool AtomicRemoveFromHandlerList(AbstractChannelHandlerContext ctx)
    {
        lock (_gate)
        {
            if (!IsLinked(ctx))
            {
                // Unlinking the context again would link its stale neighbours back into the pipeline.
                return false;
            }

            AbstractChannelHandlerContext prev = ctx.Prev;
            AbstractChannelHandlerContext next = ctx.Next;
            prev.Next = next;
            next.Prev = prev;
            return true;
        }
    }

    // CLR: InvalidOperationException stands for NoSuchElementException on an empty pipeline, as
    // LinkedList<T>.RemoveFirst() throws it for an empty list
    // (https://learn.microsoft.com/dotnet/api/system.collections.generic.linkedlist-1.removefirst).
    public IChannelHandler RemoveFirst()
    {
        if (Head.Next == Tail)
        {
            throw new InvalidOperationException();
        }

        return Remove(Head.Next).Handler;
    }

    public IChannelHandler RemoveLast()
    {
        if (Head.Next == Tail)
        {
            throw new InvalidOperationException();
        }

        return Remove(Tail.Prev).Handler;
    }

    public IChannelPipeline Replace(IChannelHandler oldHandler, string newName, IChannelHandler newHandler)
    {
        Replace(GetContextOrDie(oldHandler), newName, newHandler);
        return this;
    }

    public IChannelHandler Replace(string oldName, string newName, IChannelHandler newHandler)
    {
        return Replace(GetContextOrDie(oldName), newName, newHandler);
    }

    public T Replace<T>(string newName, IChannelHandler newHandler) where T : class, IChannelHandler
    {
        return (T)Replace(GetContextOrDie(typeof(T)), newName, newHandler);
    }

    private IChannelHandler Replace(
        AbstractChannelHandlerContext ctx, string newName, IChannelHandler newHandler)
    {
        Debug.Assert(ctx != Head && ctx != Tail);

        // CLR: the source's NullPointerException comes from newHandler.getClass() while the context is created.
        ObjectUtil.CheckNotNull(newHandler, "newHandler");

        AbstractChannelHandlerContext newCtx;

        lock (_gate)
        {
            if (!IsLinked(ctx))
            {
                // The context was removed or replaced concurrently after it was looked up.
                throw new KeyNotFoundException(ctx.Name);
            }

            CheckMultiplicity(newHandler);

            if (newName == null)
            {
                newName = GenerateName(newHandler);
            }
            else
            {
                bool sameName = ctx.Name.Equals(newName);
                if (!sameName)
                {
                    CheckDuplicateName(newName);
                }
            }

            newCtx = NewContext(ctx.childExecutor, newName, newHandler);

            Replace0(ctx, newCtx);

            // If the registered is false it means that the channel was not registered on an eventloop yet.
            // In this case we replace the context in the pipeline
            // and add a task that will call ChannelHandler.handlerAdded(...) and
            // ChannelHandler.handlerRemoved(...) once the channel is registered.
            if (!_registered)
            {
                CallHandlerCallbackLater(newCtx, true);
                CallHandlerCallbackLater(ctx, false);
                return ctx.Handler;
            }

            IEventExecutor executor = ctx.Executor;
            if (!executor.InEventLoop())
            {
                executor.Execute(() =>
                {
                    // Invoke newHandler.handlerAdded() first (i.e. before oldHandler.handlerRemoved() is invoked)
                    // because callHandlerRemoved() will trigger channelRead() or flush() on newHandler and
                    // those event handlers must be called after handlerAdded().
                    CallHandlerAdded0(newCtx);
                    CallHandlerRemoved0(ctx);
                });
                return ctx.Handler;
            }
        }

        // Invoke newHandler.handlerAdded() first (i.e. before oldHandler.handlerRemoved() is invoked)
        // because callHandlerRemoved() will trigger channelRead() or flush() on newHandler and those
        // event handlers must be called after handlerAdded().
        CallHandlerAdded0(newCtx);
        CallHandlerRemoved0(ctx);
        return ctx.Handler;
    }

    private static void Replace0(AbstractChannelHandlerContext oldCtx, AbstractChannelHandlerContext newCtx)
    {
        AbstractChannelHandlerContext prev = oldCtx.Prev;
        AbstractChannelHandlerContext next = oldCtx.Next;
        newCtx.Prev = prev;
        newCtx.Next = next;

        // Finish the replacement of oldCtx with newCtx in the linked list.
        // Note that this doesn't mean events will be sent to the new handler immediately
        // because we are currently at the event handler thread and no more than one handler methods can be invoked
        // at the same time (we ensured that in replace().)
        prev.Next = newCtx;
        next.Prev = newCtx;

        // update the reference to the replacement so forward of buffered content will work correctly
        oldCtx.Prev = newCtx;
        oldCtx.Next = newCtx;
    }

    private static void CheckMultiplicity(IChannelHandler handler)
    {
        if (handler is ChannelHandlerAdapter h)
        {
            if (!h.IsSharable && h.added)
            {
                throw new ChannelPipelineException(
                        h.GetType().FullName +
                        " is not a @Sharable handler, so can't be added or removed multiple times.");
            }

            h.added = true;
        }
    }

    private void CallHandlerAdded0(AbstractChannelHandlerContext ctx)
    {
        try
        {
            ctx.CallHandlerAdded();
        }
        catch (Exception t)
        {
            bool removed = false;

            try
            {
                // If handlerAdded(...) already removed or replaced the handler, whoever did that is responsible for
                // calling handlerRemoved(...).
                if (AtomicRemoveFromHandlerList(ctx))
                {
                    ctx.CallHandlerRemoved();
                }

                removed = true;
            }
            catch (Exception t2)
            {
                if (logger.IsWarnEnabled())
                {
                    logger.Warn("Failed to remove a handler: " + ctx.Name, t2);
                }
            }

            if (removed)
            {
                FireExceptionCaught(new ChannelPipelineException(
                        ctx.Handler.GetType().FullName +
                        ".handlerAdded() has thrown an exception; removed.", t));
            }
            else
            {
                FireExceptionCaught(new ChannelPipelineException(
                        ctx.Handler.GetType().FullName +
                        ".handlerAdded() has thrown an exception; also failed to remove.", t));
            }
        }
    }

    private void CallHandlerRemoved0(AbstractChannelHandlerContext ctx)
    {
        // Notify the complete removal.
        try
        {
            ctx.CallHandlerRemoved();
        }
        catch (Exception t)
        {
            FireExceptionCaught(new ChannelPipelineException(
                    ctx.Handler.GetType().FullName + ".handlerRemoved() has thrown an exception.", t));
        }
    }

    internal void InvokeHandlerAddedIfNeeded()
    {
        Debug.Assert(_channel.EventLoop.InEventLoop());

        if (_firstRegistration)
        {
            _firstRegistration = false;
            // We are now registered to the EventLoop. It's time to call the callbacks for the ChannelHandlers,
            // that were added before the registration was done.
            CallHandlerAddedForAllHandlers();
        }
    }

    public IChannelHandler First
    {
        get
        {
            IChannelHandlerContext first = FirstContext;
            if (first == null)
            {
                return null;
            }

            return first.Handler;
        }
    }

    public IChannelHandlerContext FirstContext
    {
        get
        {
            AbstractChannelHandlerContext first = Head.Next;
            if (first == Tail)
            {
                return null;
            }

            return Head.Next;
        }
    }

    public IChannelHandler Last
    {
        get
        {
            AbstractChannelHandlerContext last = Tail.Prev;
            if (last == Head)
            {
                return null;
            }

            return last.Handler;
        }
    }

    public IChannelHandlerContext LastContext
    {
        get
        {
            AbstractChannelHandlerContext last = Tail.Prev;
            if (last == Head)
            {
                return null;
            }

            return last;
        }
    }

    public IChannelHandler Get(string name)
    {
        IChannelHandlerContext ctx = Context(name);
        if (ctx == null)
        {
            return null;
        }
        else
        {
            return ctx.Handler;
        }
    }

    public T Get<T>() where T : class, IChannelHandler
    {
        IChannelHandlerContext ctx = Context(typeof(T));
        if (ctx == null)
        {
            return null;
        }
        else
        {
            return (T)ctx.Handler;
        }
    }

    public IChannelHandlerContext Context(string name)
    {
        return Context0(ObjectUtil.CheckNotNull(name, "name"));
    }

    public IChannelHandlerContext Context(IChannelHandler handler)
    {
        ObjectUtil.CheckNotNull(handler, "handler");

        AbstractChannelHandlerContext ctx = Head.Next;

        for (; ; )
        {

            if (ctx == null)
            {
                return null;
            }

            if (ctx.Handler == handler)
            {
                return ctx;
            }

            ctx = ctx.Next;
        }
    }

    public IChannelHandlerContext Context(Type handlerType)
    {
        ObjectUtil.CheckNotNull(handlerType, "handlerType");

        AbstractChannelHandlerContext ctx = Head.Next;

        for (; ; )
        {
            if (ctx == null)
            {
                return null;
            }
            if (handlerType.IsAssignableFrom(ctx.Handler.GetType()))
            {
                return ctx;
            }

            ctx = ctx.Next;
        }
    }

    public IReadOnlyList<string> Names()
    {
        List<string> list = new List<string>();
        AbstractChannelHandlerContext ctx = Head.Next;

        for (; ; )
        {
            if (ctx == null)
            {
                return list;
            }

            list.Add(ctx.Name);
            ctx = ctx.Next;
        }
    }

    public IReadOnlyDictionary<string, IChannelHandler> ToMap()
    {
        Dictionary<string, IChannelHandler> map = new Dictionary<string, IChannelHandler>();
        AbstractChannelHandlerContext ctx = Head.Next;

        for (; ; )
        {
            if (ctx == Tail)
            {
                return map;
            }

            map[ctx.Name] = ctx.Handler;
            ctx = ctx.Next;
        }
    }

    public IEnumerator<KeyValuePair<string, IChannelHandler>> GetEnumerator()
    {
        return ToMap().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /**
     * Returns the {@link String} representation of this pipeline.
     */
    public sealed override string ToString()
    {
        StringBuilder buf = new StringBuilder()
            .Append(StringUtil.SimpleClassName(this))
            .Append('{');
        AbstractChannelHandlerContext ctx = Head.Next;

        for (; ; )
        {
            if (ctx == Tail)
            {
                break;
            }

            buf.Append('(')
               .Append(ctx.Name)
               .Append(" = ")
               .Append(ctx.Handler.GetType().FullName)
               .Append(')');

            ctx = ctx.Next;
            if (ctx == Tail)
            {
                break;
            }

            buf.Append(", ");
        }

        buf.Append('}');

        return buf.ToString();
    }

    public IChannelInboundInvoker FireChannelRegistered()
    {
        if (Head.Executor.InEventLoop())
        {
            if (Head.InvokeHandler())
            {
                Head.ChannelRegistered(Head);
            }
            else
            {
                Head.FireChannelRegistered();
            }
        }
        else
        {
            Head.Executor.Execute(() => FireChannelRegistered());
        }

        return this;
    }

    public IChannelInboundInvoker FireChannelUnregistered()
    {
        if (Head.Executor.InEventLoop())
        {
            if (Head.InvokeHandler())
            {
                Head.ChannelUnregistered(Head);
            }
            else
            {
                Head.FireChannelUnregistered();
            }
        }
        else
        {
            Head.Executor.Execute(() => FireChannelUnregistered());
        }

        return this;
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
    // CLR: internal, because AbstractChannel also destroys the pipeline of a channel whose deregistration preceded
    // the completion of an offloaded native close.
    internal void Destroy()
    {
        lock (_gate)
        {
            DestroyUp(Head.Next, false);
        }
    }

    private void DestroyUp(AbstractChannelHandlerContext ctx, bool inEventLoop)
    {
        Thread currentThread = Thread.CurrentThread;
        AbstractChannelHandlerContext tail = Tail;

        for (; ; )
        {
            if (ctx == tail)
            {
                DestroyDown(currentThread, tail.Prev, inEventLoop);
                break;
            }

            IEventExecutor executor = ctx.Executor;
            if (!inEventLoop && !executor.InEventLoop(currentThread))
            {
                AbstractChannelHandlerContext finalCtx = ctx;
                executor.Execute(() => DestroyUp(finalCtx, true));
                break;
            }

            ctx = ctx.Next;
            inEventLoop = false;
        }
    }

    private void DestroyDown(Thread currentThread, AbstractChannelHandlerContext ctx, bool inEventLoop)
    {
        // We have reached at tail; now traverse backwards.
        AbstractChannelHandlerContext head = Head;

        for (; ; )
        {
            if (ctx == head)
            {
                break;
            }

            IEventExecutor executor = ctx.Executor;
            if (inEventLoop || executor.InEventLoop(currentThread))
            {
                // Skip a context that a handlerRemoved(...) callback already removed or replaced.
                if (AtomicRemoveFromHandlerList(ctx))
                {
                    CallHandlerRemoved0(ctx);
                }
            }
            else
            {
                AbstractChannelHandlerContext finalCtx = ctx;
                executor.Execute(() => DestroyDown(Thread.CurrentThread, finalCtx, true));
                break;
            }

            ctx = ctx.Prev;
            inEventLoop = false;
        }
    }

    public IChannelInboundInvoker FireChannelActive()
    {
        if (Head.Executor.InEventLoop())
        {
            if (Head.InvokeHandler())
            {
                Head.ChannelActive(Head);
            }
            else
            {
                Head.FireChannelActive();
            }
        }
        else
        {
            Head.Executor.Execute(() => FireChannelActive());
        }

        return this;
    }

    public IChannelInboundInvoker FireChannelInactive()
    {
        if (Head.Executor.InEventLoop())
        {
            if (Head.InvokeHandler())
            {
                Head.ChannelInactive(Head);
            }
            else
            {
                Head.FireChannelInactive();
            }
        }
        else
        {
            Head.Executor.Execute(() => FireChannelInactive());
        }

        return this;
    }

    public IChannelInboundInvoker FireExceptionCaught(Exception cause)
    {
        if (Head.Executor.InEventLoop())
        {
            if (Head.InvokeHandler())
            {
                Head.ExceptionCaught(Head, cause);
            }
            else
            {
                Head.FireExceptionCaught(cause);
            }
        }
        else
        {
            Head.Executor.Execute(() => FireExceptionCaught(cause));
        }

        return this;
    }

    public IChannelInboundInvoker FireUserEventTriggered(object @event)
    {
        if (Head.Executor.InEventLoop())
        {
            if (Head.InvokeHandler())
            {
                Head.UserEventTriggered(Head, @event);
            }
            else
            {
                Head.FireUserEventTriggered(@event);
            }
        }
        else
        {
            Head.Executor.Execute(() => FireUserEventTriggered(@event));
        }

        return this;
    }

    public IChannelInboundInvoker FireChannelRead(object msg)
    {
        if (Head.Executor.InEventLoop())
        {
            if (Head.InvokeHandler())
            {
                Head.ChannelRead(Head, msg);
            }
            else
            {
                Head.FireChannelRead(msg);
            }
        }
        else
        {
            Head.Executor.Execute(() => FireChannelRead(msg));
        }

        return this;
    }

    public IChannelInboundInvoker FireChannelReadComplete()
    {
        if (Head.Executor.InEventLoop())
        {
            if (Head.InvokeHandler())
            {
                Head.ChannelReadComplete(Head);
            }
            else
            {
                Head.FireChannelReadComplete();
            }
        }
        else
        {
            Head.Executor.Execute(() => FireChannelReadComplete());
        }

        return this;
    }

    public IChannelInboundInvoker FireChannelWritabilityChanged()
    {
        if (Head.Executor.InEventLoop())
        {
            if (Head.InvokeHandler())
            {
                Head.ChannelWritabilityChanged(Head);
            }
            else
            {
                Head.FireChannelWritabilityChanged();
            }
        }
        else
        {
            Head.Executor.Execute(() => FireChannelWritabilityChanged());
        }

        return this;
    }

    public Task BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default)
    {
        return Tail.BindAsync(localAddress, cancellationToken);
    }

    public Task ConnectAsync(EndPoint remoteAddress, EndPoint localAddress = null, CancellationToken cancellationToken = default)
    {
        return Tail.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return Tail.DisconnectAsync(cancellationToken);
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        return Tail.CloseAsync(cancellationToken);
    }

    public Task DeregisterAsync(CancellationToken cancellationToken = default)
    {
        return Tail.DeregisterAsync(cancellationToken);
    }

    public IChannelOutboundInvoker Flush()
    {
        Tail.Flush();
        return this;
    }

    public IChannelOutboundInvoker Read()
    {
        Tail.Read();
        return this;
    }

    public Task WriteAsync(object msg, CancellationToken cancellationToken = default)
    {
        return Tail.WriteAsync(msg, cancellationToken);
    }

    public Task WriteAndFlushAsync(object msg, CancellationToken cancellationToken = default)
    {
        return Tail.WriteAndFlushAsync(msg, cancellationToken);
    }

    private void CheckDuplicateName(string name)
    {
        if (Context0(name) != null)
        {
            throw new ArgumentException("Duplicate handler name: " + name);
        }
    }

    private AbstractChannelHandlerContext Context0(string name)
    {
        AbstractChannelHandlerContext context = Head.Next;

        while (context != Tail)
        {
            if (context.Name.Equals(name))
            {
                return context;
            }

            context = context.Next;
        }

        return null;
    }

    // CLR: KeyNotFoundException stands for NoSuchElementException of a failed lookup
    // (https://learn.microsoft.com/dotnet/api/system.collections.generic.keynotfoundexception).
    private AbstractChannelHandlerContext GetContextOrDie(string name)
    {
        AbstractChannelHandlerContext ctx = (AbstractChannelHandlerContext)Context(name);
        if (ctx == null)
        {
            throw new KeyNotFoundException(name);
        }
        else
        {
            return ctx;
        }
    }

    private AbstractChannelHandlerContext GetContextOrDie(IChannelHandler handler)
    {
        AbstractChannelHandlerContext ctx = (AbstractChannelHandlerContext)Context(handler);
        if (ctx == null)
        {
            throw new KeyNotFoundException(handler.GetType().FullName);
        }
        else
        {
            return ctx;
        }
    }

    private AbstractChannelHandlerContext GetContextOrDie(Type handlerType)
    {
        AbstractChannelHandlerContext ctx = (AbstractChannelHandlerContext)Context(handlerType);
        if (ctx == null)
        {
            throw new KeyNotFoundException(handlerType.FullName);
        }
        else
        {
            return ctx;
        }
    }

    private void CallHandlerAddedForAllHandlers()
    {
        PendingHandlerCallback pendingHandlerCallbackHead;

        lock (_gate)
        {
            Debug.Assert(!_registered);

            // This Channel itself was registered.
            _registered = true;

            pendingHandlerCallbackHead = _pendingHandlerCallbackHead;
            // Null out so it can be GC'ed.
            _pendingHandlerCallbackHead = null;
        }

        // This must happen outside of the synchronized(...) block as otherwise handlerAdded(...) may be called while
        // holding the lock and so produce a deadlock if handlerAdded(...) will try to add another handler from outside
        // the EventLoop.
        PendingHandlerCallback task = pendingHandlerCallbackHead;

        while (task != null)
        {
            task.Execute();
            task = task.next;
        }
    }

    private void CallHandlerCallbackLater(AbstractChannelHandlerContext ctx, bool added)
    {
        Debug.Assert(!_registered);

        PendingHandlerCallback task = added ? new PendingHandlerAddedTask(this, ctx) : new PendingHandlerRemovedTask(this, ctx);
        PendingHandlerCallback pending = _pendingHandlerCallbackHead;
        if (pending == null)
        {
            _pendingHandlerCallbackHead = task;
        }
        else
        {
            // Find the tail of the linked-list.
            while (pending.next != null)
            {
                pending = pending.next;
            }

            pending.next = task;
        }
    }

    private void CallHandlerAddedInEventLoop(AbstractChannelHandlerContext newCtx, IEventExecutor executor)
    {
        newCtx.SetAddPending();
        executor.Execute(() => CallHandlerAdded0(newCtx));
    }

    /**
     * Called once a {@link Throwable} hit the end of the {@link ChannelPipeline} without been handled by the user
     * in {@link ChannelHandler#exceptionCaught(ChannelHandlerContext, Throwable)}.
     */
    protected virtual void OnUnhandledInboundException(Exception cause)
    {
        try
        {
            logger.Warn(
                    "An exceptionCaught() event was fired, and it reached at the tail of the pipeline. " +
                            "It usually means the last handler in the pipeline did not handle the exception.",
                    cause);
        }
        finally
        {
            ReferenceCountUtil.Release(cause);
        }
    }

    /**
     * Called once the {@link ChannelInboundHandler#channelActive(ChannelHandlerContext)}event hit
     * the end of the {@link ChannelPipeline}.
     */
    protected virtual void OnUnhandledInboundChannelActive()
    {
    }

    /**
     * Called once the {@link ChannelInboundHandler#channelInactive(ChannelHandlerContext)} event hit
     * the end of the {@link ChannelPipeline}.
     */
    protected virtual void OnUnhandledInboundChannelInactive()
    {
    }

    /**
     * Called once a message hit the end of the {@link ChannelPipeline} without been handled by the user
     * in {@link ChannelInboundHandler#channelRead(ChannelHandlerContext, Object)}. This method is responsible
     * to call {@link ReferenceCountUtil#release(Object)} on the given msg at some point.
     */
    protected virtual void OnUnhandledInboundMessage(object msg)
    {
        try
        {
            logger.Debug(
                    "Discarded inbound message {} that reached at the tail of the pipeline. " +
                            "Please check your pipeline configuration.", msg);
        }
        finally
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    /**
     * Called once a message hit the end of the {@link ChannelPipeline} without been handled by the user
     * in {@link ChannelInboundHandler#channelRead(ChannelHandlerContext, Object)}. This method is responsible
     * to call {@link ReferenceCountUtil#release(Object)} on the given msg at some point.
     */
    protected virtual void OnUnhandledInboundMessage(IChannelHandlerContext ctx, object msg)
    {
        OnUnhandledInboundMessage(msg);

        if (logger.IsDebugEnabled())
        {
            logger.Debug("Discarded message pipeline : {}. Channel : {}.",
                         ctx.Pipeline.Names(), ctx.Channel);
        }
    }

    /**
     * Called once the {@link ChannelInboundHandler#channelReadComplete(ChannelHandlerContext)} event hit
     * the end of the {@link ChannelPipeline}.
     */
    protected virtual void OnUnhandledInboundChannelReadComplete()
    {
    }

    /**
     * Called once an user event hit the end of the {@link ChannelPipeline} without been handled by the user
     * in {@link ChannelInboundHandler#userEventTriggered(ChannelHandlerContext, Object)}. This method is responsible
     * to call {@link ReferenceCountUtil#release(Object)} on the given event at some point.
     */
    protected virtual void OnUnhandledInboundUserEventTriggered(object evt)
    {
        // This may not be a configuration error and so don't log anything.
        // The event may be superfluous for the current pipeline configuration.
        ReferenceCountUtil.Release(evt);
    }

    /**
     * Called once the {@link ChannelInboundHandler#channelWritabilityChanged(ChannelHandlerContext)} event hit
     * the end of the {@link ChannelPipeline}.
     */
    protected virtual void OnUnhandledChannelWritabilityChanged()
    {
    }

    // CLR: a closed ChannelOutboundBuffer stands for the null outboundBuffer() of a closed channel.
    protected internal virtual void IncrementPendingOutboundBytes(long size)
    {
        ChannelOutboundBuffer buffer = _channel.OutboundBuffer;
        if (buffer != null && !buffer.IsClosed)
        {
            buffer.IncrementPendingOutboundBytes(size);
        }
    }

    protected internal virtual void DecrementPendingOutboundBytes(long size)
    {
        ChannelOutboundBuffer buffer = _channel.OutboundBuffer;
        if (buffer != null && !buffer.IsClosed)
        {
            buffer.DecrementPendingOutboundBytes(size);
        }
    }

    // A special catch-all handler that handles both bytes and messages.
    internal sealed class TailContext : AbstractChannelHandlerContext, IChannelInboundHandler
    {
        // CLR: the source's inner class reaches the pipeline through DefaultChannelPipeline.this.
        private readonly DefaultChannelPipeline _pipeline;

        internal TailContext(DefaultChannelPipeline pipeline) : base(pipeline, null, TAIL_NAME, null)
        {
            _pipeline = pipeline;
            SetAddComplete();
        }

        public override IChannelHandler Handler => this;

        public void ChannelRegistered(IChannelHandlerContext ctx)
        {
        }

        public void ChannelUnregistered(IChannelHandlerContext ctx)
        {
        }

        public void ChannelActive(IChannelHandlerContext ctx)
        {
            _pipeline.OnUnhandledInboundChannelActive();
        }

        public void ChannelInactive(IChannelHandlerContext ctx)
        {
            _pipeline.OnUnhandledInboundChannelInactive();
        }

        public void ChannelWritabilityChanged(IChannelHandlerContext ctx)
        {
            _pipeline.OnUnhandledChannelWritabilityChanged();
        }

        public void HandlerAdded(IChannelHandlerContext ctx)
        {
        }

        public void HandlerRemoved(IChannelHandlerContext ctx)
        {
        }

        public void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            _pipeline.OnUnhandledInboundUserEventTriggered(evt);
        }

        public void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _pipeline.OnUnhandledInboundException(cause);
        }

        public void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            _pipeline.OnUnhandledInboundMessage(ctx, msg);
        }

        public void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            _pipeline.OnUnhandledInboundChannelReadComplete();
        }
    }

    // CLR: Channel.Unsafe is folded into AbstractChannel, so the head forwards outbound operations to its transport
    // members (the source's unsafe field).
    internal sealed class HeadContext : AbstractChannelHandlerContext, IChannelOutboundHandler, IChannelInboundHandler
    {
        // CLR: the source's inner class reaches the pipeline through DefaultChannelPipeline.this.
        private readonly DefaultChannelPipeline _pipeline;
        private readonly AbstractChannel _channel;

        internal HeadContext(DefaultChannelPipeline pipeline) : base(pipeline, null, HEAD_NAME, null)
        {
            _pipeline = pipeline;
            _channel = pipeline._channel;
            SetAddComplete();
        }

        public override IChannelHandler Handler => this;

        public void HandlerAdded(IChannelHandlerContext ctx)
        {
            // NOOP
        }

        public void HandlerRemoved(IChannelHandlerContext ctx)
        {
            // NOOP
        }

        public Task BindAsync(
            IChannelHandlerContext ctx, EndPoint localAddress, CancellationToken cancellationToken)
        {
            return _channel.BindTransportAsync(localAddress, cancellationToken);
        }

        public Task ConnectAsync(
            IChannelHandlerContext ctx,
            EndPoint remoteAddress, EndPoint localAddress,
            CancellationToken cancellationToken)
        {
            return _channel.ConnectTransportAsync(remoteAddress, localAddress, cancellationToken);
        }

        public Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
        {
            return _channel.DisconnectTransportAsync(cancellationToken);
        }

        public Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
        {
            return _channel.Close();
        }

        public Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
        {
            return _channel.DeregisterTransportAsync();
        }

        public void Read(IChannelHandlerContext ctx)
        {
            _channel.BeginRead();
        }

        public Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            return _channel.WriteTransportAsync(msg, cancellationToken);
        }

        public void Flush(IChannelHandlerContext ctx)
        {
            _channel.FlushTransport();
        }

        public void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            ctx.FireExceptionCaught(cause);
        }

        public void ChannelRegistered(IChannelHandlerContext ctx)
        {
            _pipeline.InvokeHandlerAddedIfNeeded();
            ctx.FireChannelRegistered();
        }

        public void ChannelUnregistered(IChannelHandlerContext ctx)
        {
            ctx.FireChannelUnregistered();

            // Remove all handlers sequentially if channel is closed and unregistered.
            // CLR: AbstractChannel marks itself closed before an offloaded native close completes; that close destroys
            // the pipeline when it finishes, while the source's isOpen() still reports the open socket here.
            if (!_channel.IsOpen && !_channel.ClosePending)
            {
                _pipeline.Destroy();
            }
        }

        public void ChannelActive(IChannelHandlerContext ctx)
        {
            ctx.FireChannelActive();

            ReadIfIsAutoRead();
        }

        public void ChannelInactive(IChannelHandlerContext ctx)
        {
            ctx.FireChannelInactive();
        }

        public void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ctx.FireChannelRead(msg);
        }

        public void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            ctx.FireChannelReadComplete();

            ReadIfIsAutoRead();
        }

        private void ReadIfIsAutoRead()
        {
            if (_channel.Config.IsAutoRead())
            {
                _channel.Read();
            }
        }

        public void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            ctx.FireUserEventTriggered(evt);
        }

        public void ChannelWritabilityChanged(IChannelHandlerContext ctx)
        {
            ctx.FireChannelWritabilityChanged();
        }
    }

    // CLR: the Runnable is the Run method, passed to execute(...) as an Action.
    private abstract class PendingHandlerCallback
    {
        internal readonly AbstractChannelHandlerContext ctx;
        internal PendingHandlerCallback next;

        protected PendingHandlerCallback(AbstractChannelHandlerContext ctx)
        {
            this.ctx = ctx;
        }

        public abstract void Run();

        internal abstract void Execute();
    }

    private sealed class PendingHandlerAddedTask : PendingHandlerCallback
    {
        // CLR: the source's inner class reaches the pipeline through DefaultChannelPipeline.this.
        private readonly DefaultChannelPipeline _pipeline;

        internal PendingHandlerAddedTask(DefaultChannelPipeline pipeline, AbstractChannelHandlerContext ctx) : base(ctx)
        {
            _pipeline = pipeline;
        }

        public override void Run()
        {
            _pipeline.CallHandlerAdded0(ctx);
        }

        internal override void Execute()
        {
            IEventExecutor executor = ctx.Executor;
            if (executor.InEventLoop())
            {
                _pipeline.CallHandlerAdded0(ctx);
            }
            else
            {
                try
                {
                    executor.Execute(Run);
                }
                catch (RejectedExecutionException e)
                {
                    if (logger.IsWarnEnabled())
                    {
                        logger.Warn(
                                "Can't invoke handlerAdded() as the EventExecutor {} rejected it, removing handler {}.",
                                executor, ctx.Name, e);
                    }

                    _pipeline.AtomicRemoveFromHandlerList(ctx);
                    ctx.SetRemoved();
                }
            }
        }
    }

    private sealed class PendingHandlerRemovedTask : PendingHandlerCallback
    {
        // CLR: the source's inner class reaches the pipeline through DefaultChannelPipeline.this.
        private readonly DefaultChannelPipeline _pipeline;

        internal PendingHandlerRemovedTask(DefaultChannelPipeline pipeline, AbstractChannelHandlerContext ctx) : base(ctx)
        {
            _pipeline = pipeline;
        }

        public override void Run()
        {
            _pipeline.CallHandlerRemoved0(ctx);
        }

        internal override void Execute()
        {
            IEventExecutor executor = ctx.Executor;
            if (executor.InEventLoop())
            {
                _pipeline.CallHandlerRemoved0(ctx);
            }
            else
            {
                try
                {
                    executor.Execute(Run);
                }
                catch (RejectedExecutionException e)
                {
                    if (logger.IsWarnEnabled())
                    {
                        logger.Warn(
                                "Can't invoke handlerRemoved() as the EventExecutor {} rejected it," +
                                        " removing handler {}.", executor, ctx.Name, e);
                    }

                    // remove0(...) was call before so just call AbstractChannelHandlerContext.setRemoved().
                    ctx.SetRemoved();
                }
            }
        }
    }
}
