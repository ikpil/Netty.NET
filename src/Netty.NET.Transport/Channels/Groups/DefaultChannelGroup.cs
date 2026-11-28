/*
 * Copyright 2013 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Channels.Groups;

/// <summary>A concurrent channel set with native Task fan-out and independent buffer views for each write.</summary>
/**
 * The default {@link ChannelGroup} implementation.
 */
/**
 * The result of an asynchronous {@link ChannelGroup} operation.
 * {@link ChannelGroupFuture} is composed of {@link ChannelFuture}s which
 * represent the outcome of the individual I/O operations that affect the
 * {@link Channel}s in the {@link ChannelGroup}.
 *
 * <p>
 * All I/O operations in {@link ChannelGroup} are asynchronous.  It means any
 * I/O calls will return immediately with no guarantee that the requested I/O
 * operations have been completed at the end of the call.  Instead, you will be
 * returned with a {@link ChannelGroupFuture} instance which tells you when the
 * requested I/O operations have succeeded, failed, or cancelled.
 * <p>
 * Various methods are provided to let you check if the I/O operations has been
 * completed, wait for the completion, and retrieve the result of the I/O
 * operation. It also allows you to add more than one
 * {@link ChannelGroupFutureListener} so you can get notified when the I/O
 * operation have been completed.
 *
 * <h3>Prefer {@link #addListener(GenericFutureListener)} to {@link #await()}</h3>
 *
 * It is recommended to prefer {@link #addListener(GenericFutureListener)} to
 * {@link #await()} wherever possible to get notified when I/O operations are
 * done and to do any follow-up tasks.
 * <p>
 * {@link #addListener(GenericFutureListener)} is non-blocking.  It simply
 * adds the specified {@link ChannelGroupFutureListener} to the
 * {@link ChannelGroupFuture}, and I/O thread will notify the listeners when
 * the I/O operations associated with the future is done.
 * {@link ChannelGroupFutureListener} yields the best performance and resource
 * utilization because it does not block at all, but it could be tricky to
 * implement a sequential logic if you are not used to event-driven programming.
 * <p>
 * By contrast, {@link #await()} is a blocking operation.  Once called, the
 * caller thread blocks until all I/O operations are done.  It is easier to
 * implement a sequential logic with {@link #await()}, but the caller thread
 * blocks unnecessarily until all I/O operations are done and there's relatively
 * expensive cost of inter-thread notification.  Moreover, there's a chance of
 * dead lock in a particular circumstance, which is described below.
 *
 * <h3>Do not call {@link #await()} inside {@link ChannelHandler}</h3>
 * <p>
 * The event handler methods in {@link ChannelHandler} is often called by
 * an I/O thread.  If {@link #await()} is called by an event handler
 * method, which is called by the I/O thread, the I/O operation it is waiting
 * for might never be complete because {@link #await()} can block the I/O
 * operation it is waiting for, which is a dead lock.
 * <pre>
 * // BAD - NEVER DO THIS
 * {@code @Override}
 * public void messageReceived({@link ChannelHandlerContext} ctx, ShutdownMessage msg) {
 *     {@link ChannelGroup} allChannels = MyServer.getAllChannels();
 *     {@link ChannelGroupFuture} future = allChannels.close();
 *     future.awaitUninterruptibly();
 *     // Perform post-shutdown operation
 *     // ...
 *
 * }
 *
 * // GOOD
 * {@code @Override}
 * public void messageReceived(ChannelHandlerContext ctx, ShutdownMessage msg) {
 *     {@link ChannelGroup} allChannels = MyServer.getAllChannels();
 *     {@link ChannelGroupFuture} future = allChannels.close();
 *     future.addListener(new {@link ChannelGroupFutureListener}() {
 *         public void operationComplete({@link ChannelGroupFuture} future) {
 *             // Perform post-closure operation
 *             // ...
 *         }
 *     });
 * }
 * </pre>
 * <p>
 * In spite of the disadvantages mentioned above, there are certainly the cases
 * where it is more convenient to call {@link #await()}. In such a case, please
 * make sure you do not call {@link #await()} in an I/O thread.  Otherwise,
 * {@link IllegalStateException} will be raised to prevent a dead lock.
 */
/**
 * Returns the {@link ChannelGroup} which is associated with this future.
 */
/**
 * A thread-safe {@link Set} that contains open {@link Channel}s and provides
 * various bulk operations on them.  Using {@link ChannelGroup}, you can
 * categorize {@link Channel}s into a meaningful group (e.g. on a per-service
 * or per-state basis.)  A closed {@link Channel} is automatically removed from
 * the collection, so that you don't need to worry about the life cycle of the
 * added {@link Channel}.  A {@link Channel} can belong to more than one
 * {@link ChannelGroup}.
 *
 * <h3>Broadcast a message to multiple {@link Channel}s</h3>
 * <p>
 * If you need to broadcast a message to more than one {@link Channel}, you can
 * add the {@link Channel}s associated with the recipients and call {@link ChannelGroup#write(Object)}:
 * <pre>
 * <strong>{@link ChannelGroup} recipients =
 *         new {@link DefaultChannelGroup}({@link GlobalEventExecutor}.INSTANCE);</strong>
 * recipients.add(channelA);
 * recipients.add(channelB);
 * ..
 * <strong>recipients.write({@link Unpooled}.copiedBuffer(
 *         "Service will shut down for maintenance in 5 minutes.",
 *         {@link CharsetUtil}.UTF_8));</strong>
 * </pre>
 *
 * <h3>Simplify shutdown process with {@link ChannelGroup}</h3>
 * <p>
 * If both {@link ServerChannel}s and non-{@link ServerChannel}s exist in the
 * same {@link ChannelGroup}, any requested I/O operations on the group are
 * performed for the {@link ServerChannel}s first and then for the others.
 * <p>
 * This rule is very useful when you shut down a server in one shot:
 *
 * <pre>
 * <strong>{@link ChannelGroup} allChannels =
 *         new {@link DefaultChannelGroup}({@link GlobalEventExecutor}.INSTANCE);</strong>
 *
 * public static void main(String[] args) throws Exception {
 *     {@link ServerBootstrap} b = new {@link ServerBootstrap}(..);
 *     ...
 *     b.childHandler(new MyHandler());
 *
 *     // Start the server
 *     b.getPipeline().addLast("handler", new MyHandler());
 *     {@link Channel} serverChannel = b.bind(..).sync();
 *     <strong>allChannels.add(serverChannel);</strong>
 *
 *     ... Wait until the shutdown signal reception ...
 *
 *     // Close the serverChannel and then all accepted connections.
 *     <strong>allChannels.close().awaitUninterruptibly();</strong>
 * }
 *
 * public class MyHandler extends {@link ChannelInboundHandlerAdapter} {
 *     {@code @Override}
 *     public void channelActive({@link ChannelHandlerContext} ctx) {
 *         // closed on shutdown.
 *         <strong>allChannels.add(ctx.channel());</strong>
 *         super.channelActive(ctx);
 *     }
 * }
 * </pre>
 */
public class DefaultChannelGroup : IReadOnlyCollection<IChannel>, ISet<IChannel>, IComparable<DefaultChannelGroup>
{
    private readonly ConcurrentDictionary<IChannelId, IChannel> _channels = new();
    private readonly bool _stayClosed;
    private int _closed;
    /**
     * Creates a new group with a generated name and the provided {@link EventExecutor} to notify the
     * {@link ChannelGroupFuture}s.
     */
    /**
     * Creates a new group with the specified {@code name} and {@link EventExecutor} to notify the
     * {@link ChannelGroupFuture}s.  Please note that different groups can have the same name, which means no
     * duplicate check is done against group names.
     */
    /**
     * Creates a new group with a generated name and the provided {@link EventExecutor} to notify the
     * {@link ChannelGroupFuture}s. {@code stayClosed} defines whether or not, this group can be closed
     * more than once. Adding channels to a closed group will immediately close them, too. This makes it
     * easy, to shutdown server and child channels at once.
     */
    /**
     * Creates a new group with the specified {@code name} and {@link EventExecutor} to notify the
     * {@link ChannelGroupFuture}s. {@code stayClosed} defines whether or not, this group can be closed
     * more than once. Adding channels to a closed group will immediately close them, too. This makes it
     * easy, to shutdown server and child channels at once. Please note that different groups can have
     * the same name, which means no duplicate check is done against group names.
     */
    public DefaultChannelGroup(string name = null, bool stayClosed = false)
    {
        Name = name ?? "group-" + Guid.NewGuid().ToString("N");
        _stayClosed = stayClosed;
    }
    /**
     * Returns the name of this group.  A group name is purely for helping
     * you to distinguish one group from others.
     */
    public string Name { get; }
    public int Count => _channels.Count;
    public bool Add(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        bool added = _channels.TryAdd(channel.Id, channel);
        if (added)
        {
            var group = new WeakReference<DefaultChannelGroup>(this);
            var member = new WeakReference<IChannel>(channel);
            channel.CloseFuture.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
            {
                if (group.TryGetTarget(out var owner) && member.TryGetTarget(out var value))
                    owner.Remove(value);
            });
        }
        // First add channel, than check if closed.
        // Seems inefficient at first, but this way a volatile
        // gives us enough synchronization to be thread-safe.
        //
        // If true: Close right away.
        // (Might be closed a second time by ChannelGroup.close(), but this is ok)
        //
        // If false: Channel will definitely be closed by the ChannelGroup.
        // (Because closed=true always happens-before ChannelGroup.close())
        //
        // See https://github.com/netty/netty/issues/4020
        if (_stayClosed && Volatile.Read(ref _closed) != 0)
            _ = channel.CloseAsync();
        return added;
    }

    public bool Remove(IChannel channel)
    {
        return channel != null && _channels.TryRemove(new KeyValuePair<IChannelId, IChannel>(channel.Id, channel));
    }

    public bool RemoveById(IChannelId id)
    {
        return id != null && _channels.TryRemove(id, out _);
    }

    public bool Contains(IChannel channel)
    {
        return channel != null && _channels.TryGetValue(channel.Id, out var value) && ReferenceEquals(value, channel);
    }

    /**
     * Returns the {@link Channel} which has the specified {@link ChannelId}.
     *
     * @return the matching {@link Channel} if found. {@code null} otherwise.
     */
    public IChannel Find(IChannelId id)
    {
        return _channels.TryGetValue(id, out var channel) ? channel : null;
    }

    public void Clear()
    {
        _channels.Clear();
    }

    public IEnumerator<IChannel> GetEnumerator()
    {
        return _channels.Values.OrderBy(channel => channel is IServerChannel ? 0 : 1).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /**
     * Closes all {@link Channel}s in this group.  If the {@link Channel} is
     * connected to a remote peer or bound to a local address, it is
     * automatically disconnected and unbound.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    public Task CloseAsync()
    {
        return CloseAsync(ChannelMatchers.All());
    }

    /**
     * Closes all {@link Channel}s in this group that are matched by the given {@link ChannelMatcher}.
     * If the {@link Channel} is  connected to a remote peer or bound to a local address, it is
     * automatically disconnected and unbound.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    public Task CloseAsync(Func<IChannel, bool> matcher)
    {
        return CloseAsync(out _, matcher);
    }

    public Task CloseAsync(out IReadOnlyDictionary<IChannel, Task> operations, Func<IChannel, bool> matcher = null)
    {
        if (_stayClosed)
        {
            // It is important to set the closed to true, before closing channels.
            // Our invariants are:
            // closed=true happens-before ChannelGroup.close()
            // ChannelGroup.add() happens-before checking closed==true
            //
            // See https://github.com/netty/netty/issues/4020
            Volatile.Write(ref _closed, 1);
        }
        return OperateAsync(async channel =>
        {
            await channel.CloseAsync().ConfigureAwait(false);
            Remove(channel);
        }, matcher, true, out operations);
    }

    /**
     * Disconnects all {@link Channel}s in this group from their remote peers.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    public Task DisconnectAsync()
    {
        return DisconnectAsync(ChannelMatchers.All());
    }

    /**
     * Disconnects all {@link Channel}s in this group from their remote peers,
     * that are matched by the given {@link ChannelMatcher}.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    public Task DisconnectAsync(Func<IChannel, bool> matcher)
    {
        return DisconnectAsync(out _, matcher);
    }

    public Task DisconnectAsync(out IReadOnlyDictionary<IChannel, Task> operations, Func<IChannel, bool> matcher = null)
    {
        return OperateAsync(channel => channel.DisconnectAsync(), matcher, true, out operations);
    }

    /**
     * @deprecated This method will be removed in the next major feature release.
     *
     * Deregister all {@link Channel}s in this group from their {@link EventLoop}.
     * Please note that this operation is asynchronous as {@link Channel#deregister()} is.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    // CLR: ported although @Deprecated because its @deprecated text names no replacement.
    public Task DeregisterAsync()
    {
        return DeregisterAsync(ChannelMatchers.All());
    }

    /**
     * @deprecated This method will be removed in the next major feature release.
     *
     * Deregister all {@link Channel}s in this group from their {@link EventLoop} that are matched by the given
     * {@link ChannelMatcher}. Please note that this operation is asynchronous as {@link Channel#deregister()} is.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    public Task DeregisterAsync(Func<IChannel, bool> matcher)
    {
        return DeregisterAsync(out _, matcher);
    }

    public Task DeregisterAsync(out IReadOnlyDictionary<IChannel, Task> operations, Func<IChannel, bool> matcher = null)
    {
        return OperateAsync(channel => channel.DeregisterAsync(), matcher, true, out operations);
    }

    /**
     * Writes the specified {@code message} to all {@link Channel}s in this
     * group. If the specified {@code message} is an instance of
     * {@link ByteBuf}, it is automatically
     * {@linkplain ByteBuf#duplicate() duplicated} to avoid a race
     * condition. The same is true for {@link ByteBufHolder}. Please note that this operation is asynchronous as
     * {@link Channel#write(Object)} is.
     *
     * @return itself
     */
    /**
     * Writes the specified {@code message} to all {@link Channel}s in this
     * group that are matched by the given {@link ChannelMatcher}. If the specified {@code message} is an instance of
     * {@link ByteBuf}, it is automatically
     * {@linkplain ByteBuf#duplicate() duplicated} to avoid a race
     * condition. The same is true for {@link ByteBufHolder}. Please note that this operation is asynchronous as
     * {@link Channel#write(Object)} is.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    /**
     * Writes the specified {@code message} to all {@link Channel}s in this
     * group that are matched by the given {@link ChannelMatcher}. If the specified {@code message} is an instance of
     * {@link ByteBuf}, it is automatically
     * {@linkplain ByteBuf#duplicate() duplicated} to avoid a race
     * condition. The same is true for {@link ByteBufHolder}. Please note that this operation is asynchronous as
     * {@link Channel#write(Object)} is.
     *
     * If {@code voidPromise} is {@code true} {@link Channel#voidPromise()} is used for the writes and so the same
     * restrictions to the returned {@link ChannelGroupFuture} apply as to a void promise.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    public Task WriteAsync(object message, Func<IChannel, bool> matcher = null)
    {
        return WriteAsync(message, out _, matcher);
    }

    public Task WriteAsync(object message, out IReadOnlyDictionary<IChannel, Task> operations, Func<IChannel, bool> matcher = null)
    {
        return WriteCoreAsync(message, matcher, false, out operations);
    }

    /**
     * Shortcut for calling {@link #write(Object)} and {@link #flush()}.
     */
    /**
     * Shortcut for calling {@link #write(Object)} and {@link #flush()} and only act on
     * {@link Channel}s that are matched by the {@link ChannelMatcher}.
     */
    /**
     * Shortcut for calling {@link #write(Object, ChannelMatcher, boolean)} and {@link #flush()} and only act on
     * {@link Channel}s that are matched by the {@link ChannelMatcher}.
     */
    public Task WriteAndFlushAsync(object message, Func<IChannel, bool> matcher = null)
    {
        return WriteAndFlushAsync(message, out _, matcher);
    }

    public Task WriteAndFlushAsync(object message, out IReadOnlyDictionary<IChannel, Task> operations, Func<IChannel, bool> matcher = null)
    {
        return WriteCoreAsync(message, matcher, true, out operations);
    }

    private Task WriteCoreAsync(object message, Func<IChannel, bool> matcher, bool flush, out IReadOnlyDictionary<IChannel, Task> operations)
    {
        ArgumentNullException.ThrowIfNull(message);
        var tasks = new List<KeyValuePair<IChannel, Task>>();
        foreach (var channel in _channels.Values)
        {
            if (channel is IServerChannel || matcher != null && !matcher(channel))
                continue;
            // Create a safe duplicate of the message to write it to a channel but not affect other writes.
            // See https://github.com/netty/netty/issues/1461
            object copy = message switch
            {
                ByteBuf buffer => buffer.RetainedDuplicate(),
                IByteBufHolder holder => holder.RetainedDuplicate(),
                _ => ReferenceCountUtil.Retain(message)
            };
            try
            {
                tasks.Add(new(channel, flush ? channel.WriteAndFlushAsync(copy) : channel.WriteAsync(copy)));
            }
            catch (Exception error)
            {
                ReferenceCountUtil.Release(copy);
                tasks.Add(new(channel, Task.FromException(error)));
            }
        }
        ReferenceCountUtil.Release(message);
        operations = Snapshot(tasks);
        return CompleteAsync(tasks);
    }

    /**
     * Flush all {@link Channel}s in this group. If the specified {@code messages} are an instance of
     * {@link ByteBuf}, it is automatically
     * {@linkplain ByteBuf#duplicate() duplicated} to avoid a race
     * condition. Please note that this operation is asynchronous as
     * {@link Channel#write(Object)} is.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    public DefaultChannelGroup Flush()
    {
        return Flush(ChannelMatchers.All());
    }

    /**
     * Flush all {@link Channel}s in this group that are matched by the given {@link ChannelMatcher}.
     * If the specified {@code messages} are an instance of
     * {@link ByteBuf}, it is automatically
     * {@linkplain ByteBuf#duplicate() duplicated} to avoid a race
     * condition. Please note that this operation is asynchronous as
     * {@link Channel#write(Object)} is.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    public DefaultChannelGroup Flush(Func<IChannel, bool> matcher)
    {
        foreach (var channel in _channels.Values)
        if (channel is not IServerChannel && (matcher == null || matcher(channel)))
            channel.Flush();
        return this;
    }

    private Task OperateAsync(Func<IChannel, Task> operation, Func<IChannel, bool> matcher, bool includeServers, out IReadOnlyDictionary<IChannel, Task> operations)
    {
        var tasks = new List<KeyValuePair<IChannel, Task>>();
        foreach (var channel in _channels.Values.OrderBy(channel => channel is IServerChannel ? 0 : 1))
        {
            if ((!includeServers && channel is IServerChannel) || matcher != null && !matcher(channel))
                continue;
            try
            {
                tasks.Add(new(channel, operation(channel)));
            }
            catch (Exception error)
            {
                tasks.Add(new(channel, Task.FromException(error)));
            }
        }
        operations = Snapshot(tasks);
        return CompleteAsync(tasks);
    }

    /**
     * Returns the {@link ChannelGroupFuture} which will be notified when all {@link Channel}s that are part of this
     * {@link ChannelGroup}, at the time of calling, are closed.
     */
    public Task NewCloseFuture(Func<IChannel, bool> matcher = null)
    {
        return NewCloseFuture(out _, matcher);
    }

    public Task NewCloseFuture(out IReadOnlyDictionary<IChannel, Task> operations, Func<IChannel, bool> matcher = null)
    {
        var tasks = _channels.Values.Where(channel => matcher == null || matcher(channel))
            .Select(channel => new KeyValuePair<IChannel, Task>(channel, channel.CloseFuture)).ToList();
        operations = Snapshot(tasks);
        return CompleteAsync(tasks);
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{StringUtil.SimpleClassName(this)}(name: {Name}, size: {Count})");
    }

    // CLR: the aggregate Task and the immutable channel-to-Task map jointly represent the source future.
    // Use await/WaitAsync for completion. Task continuations do not inherit event-loop affinity, and native
    // Task.Wait has no JVM deadlock guard; the channel owner must never synchronously wait for its own I/O.
    // CLR: the group is the operation receiver; no second future wrapper stores another group reference.
    /**
     * Returns the {@link ChannelFuture} of the individual I/O operation which
     * is associated with the specified {@link Channel}.
     *
     * @return the matching {@link ChannelFuture} if found.
     *         {@code null} otherwise.
     */
    /**
     * Returns the {@link Iterator} that enumerates all {@link ChannelFuture}s
     * which are associated with this future.  Please note that the returned
     * {@link Iterator} is unmodifiable, which means a {@link ChannelFuture}
     * cannot be removed from this future.
     */
    // CLR: lookup uses the dictionary indexer/TryGetValue, and iteration uses its read-only Values.
    /**
     * Returns {@code true} if and only if the I/O operations associated with
     * this future were partially successful with some failure.
     */
    /**
     * Returns {@code true} if and only if the I/O operations associated with
     * this future have failed partially with some success.
     */
    // CLR: partial success is 0 < successful Tasks < Count. Partial failure is
    // 0 < completed non-successful Tasks < Count (including canceled child Tasks).
    /**
     * Creates a new instance.
     */
    // CLR exposes channel lookup, immutable iteration and live partial completion through standard
    // dictionary/Task APIs. Callers keep the returned Task for aggregate await and inspect these Tasks
    // for progress; neither the map nor its task producers can be modified through the public API.
    private static IReadOnlyDictionary<IChannel, Task> Snapshot(List<KeyValuePair<IChannel, Task>> tasks)
    {
        return new ReadOnlyDictionary<IChannel, Task>(new Dictionary<IChannel, Task>(tasks, ReferenceEqualityComparer.Instance));
    }

    // CLR: aggregate success is Task.IsCompletedSuccessfully; the async method owns its completion producer.
    /**
     * The default {@link ChannelGroupFuture} implementation.
     */
    /**
     * Returns {@code true} if and only if all I/O operations associated with
     * this future were successful without any failure.
     */
    private static async Task CompleteAsync(List<KeyValuePair<IChannel, Task>> operations)
    {
        // Done on arrival?
        if (operations.Count == 0)
            return;
        try
        {
            await Task.WhenAll(operations.Select(pair => pair.Value)).ConfigureAwait(false);
        }
        catch
        {
            // WhenAll waits for every selected channel. Keep the complete set of channel/cause pairs,
            // including canceled child tasks, rather than exposing just the exception rethrown by await.
            var failures = new List<KeyValuePair<IChannel, Exception>>();
            foreach (var pair in operations)
            {
                if (pair.Value.IsCompletedSuccessfully)
                {
                    continue;
                }
                try
                {
                    pair.Value.GetAwaiter().GetResult();
                }
                catch (Exception error)
                {
                    failures.Add(new(pair.Key, pair.Value.Exception is { InnerExceptions.Count: > 1 } multiple ? multiple : error));
                }
            }
            throw new ChannelGroupException(failures);
        }
    }

    // CLR: ISet supplies the inherited set surface; Tasks and Func predicates replace JVM futures/matchers.
    // ChannelGroup extends Set and Comparable. Use CLR interfaces and reference-based HashSet
    // operations; all mutations still go through the group's Id admission and close-listener rules.
    // Each operation has a membership snapshot, not an atomic transaction spanning multiple calls.
    public bool IsReadOnly => false;
    void ICollection<IChannel>.Add(IChannel item)
    {
        Add(item);
    }

    /// <summary>Copies a server-first membership snapshot without closing or transferring channel ownership.</summary>
    public IChannel[] ToArray()
    {
        return _channels.Values.OrderBy(channel => channel is IServerChannel ? 0 : 1).ToArray();
    }

    public void CopyTo(IChannel[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        if (arrayIndex > array.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }
        var snapshot = ToArray();
        if (snapshot.Length > array.Length - arrayIndex)
        {
            throw new ArgumentException("Destination array is too small.", nameof(array));
        }
        snapshot.CopyTo(array, arrayIndex);
    }

    public void UnionWith(IEnumerable<IChannel> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            return;
        }

        foreach (var channel in other)
        {
            Add(channel);
        }
    }

    public void ExceptWith(IEnumerable<IChannel> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            Clear();
            return;
        }

        foreach (var channel in other)
        {
            Remove(channel);
        }
    }

    public void IntersectWith(IEnumerable<IChannel> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            return;
        }
        var keep = Members(other);
        foreach (var channel in ToArray())
        if (!keep.Contains(channel))
        {
            Remove(channel);
        }
    }

    public void SymmetricExceptWith(IEnumerable<IChannel> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            Clear();
            return;
        }
        foreach (var channel in Members(other))
        if (!Remove(channel))
        {
            Add(channel);
        }
    }

    public bool IsSubsetOf(IEnumerable<IChannel> other)
    {
        return Members(this).IsSubsetOf(Checked(other));
    }

    public bool IsSupersetOf(IEnumerable<IChannel> other)
    {
        return Members(this).IsSupersetOf(Checked(other));
    }

    public bool IsProperSubsetOf(IEnumerable<IChannel> other)
    {
        return Members(this).IsProperSubsetOf(Checked(other));
    }

    public bool IsProperSupersetOf(IEnumerable<IChannel> other)
    {
        return Members(this).IsProperSupersetOf(Checked(other));
    }

    public bool Overlaps(IEnumerable<IChannel> other)
    {
        return Members(this).Overlaps(Checked(other));
    }

    public bool SetEquals(IEnumerable<IChannel> other)
    {
        return Members(this).SetEquals(Checked(other));
    }

    private static IEnumerable<IChannel> Checked(IEnumerable<IChannel> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other;
    }

    private static HashSet<IChannel> Members(IEnumerable<IChannel> channels)
    {
        return new(channels, ReferenceEqualityComparer.Instance);
    }

    // Name is ordinal, as in Java String.compareTo. Same-name groups retain object identity;
    // group membership does not turn group equality/hash into mutable set-value equality.
    public int CompareTo(DefaultChannelGroup other)
    {
        if (other == null)
        {
            return 1;
        }
        int byName = string.CompareOrdinal(Name, other.Name);
        return byName != 0 ? byName : RuntimeHelpers.GetHashCode(this).CompareTo(RuntimeHelpers.GetHashCode(other));
    }
}
