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

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Channels.Groups;

/// <summary>A concurrent channel set with native Task fan-out and independent buffer views for each write.</summary>
/**
 * The default {@link ChannelGroup} implementation.
 */
public class DefaultChannelGroup : IReadOnlyCollection<IChannel>
{
    private readonly ConcurrentDictionary<IChannelId, IChannel> _channels = new();
    private readonly bool _stayClosed;
    private int _closed;
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
    { Name = name ?? "group-" + Guid.NewGuid().ToString("N"); _stayClosed = stayClosed; }
    /**
     * Returns the name of this group.  A group name is purely for helping
     * you to distinguish one group from others.
     */
    public string Name { get; }
    public int Count => _channels.Count;
    public bool Add(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (!_channels.TryAdd(channel.Id, channel))
            return false;
        var group = new WeakReference<DefaultChannelGroup>(this);
        var member = new WeakReference<IChannel>(channel);
        channel.CloseCompletion.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
        {
            if (group.TryGetTarget(out var owner) && member.TryGetTarget(out var value))
                owner.Remove(value);
        });
        if (_stayClosed && Volatile.Read(ref _closed) != 0)
            _ = channel.CloseAsync();
        return true;
    }
    public bool Remove(IChannel channel) => channel != null && _channels.TryRemove(new KeyValuePair<IChannelId, IChannel>(channel.Id, channel));
    public bool Contains(IChannel channel) => channel != null && _channels.TryGetValue(channel.Id, out var value) && ReferenceEquals(value, channel);
    /**
     * Returns the {@link Channel} which has the specified {@link ChannelId}.
     *
     * @return the matching {@link Channel} if found. {@code null} otherwise.
     */
    public IChannel Find(IChannelId id) => _channels.TryGetValue(id, out var channel) ? channel : null;
    public void Clear() => _channels.Clear();
    public IEnumerator<IChannel> GetEnumerator() => ((IEnumerable<IChannel>)_channels.Values.ToArray()).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    /**
     * Closes all {@link Channel}s in this group that are matched by the given {@link ChannelMatcher}.
     * If the {@link Channel} is  connected to a remote peer or bound to a local address, it is
     * automatically disconnected and unbound.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    public Task CloseAsync(Func<IChannel, bool> matcher = null)
    {
        if (_stayClosed)
            Volatile.Write(ref _closed, 1);
        return OperateAsync(async channel => { await channel.CloseAsync().ConfigureAwait(false); Remove(channel); }, matcher, true);
    }
    /**
     * Disconnects all {@link Channel}s in this group from their remote peers,
     * that are matched by the given {@link ChannelMatcher}.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    public Task DisconnectAsync(Func<IChannel, bool> matcher = null) => OperateAsync(channel => channel.DisconnectAsync(), matcher, true);
    /**
     * @deprecated This method will be removed in the next major feature release.
     *
     * Deregister all {@link Channel}s in this group from their {@link EventLoop} that are matched by the given
     * {@link ChannelMatcher}. Please note that this operation is asynchronous as {@link Channel#deregister()} is.
     *
     * @return the {@link ChannelGroupFuture} instance that notifies when
     *         the operation is done for all channels
     */
    public Task DeregisterAsync(Func<IChannel, bool> matcher = null) => OperateAsync(channel => channel.DeregisterAsync(), matcher, true);
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
    public Task WriteAsync(object message, Func<IChannel, bool> matcher = null) => WriteCoreAsync(message, matcher, false);
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
    public Task WriteAndFlushAsync(object message, Func<IChannel, bool> matcher = null) => WriteCoreAsync(message, matcher, true);
    private Task WriteCoreAsync(object message, Func<IChannel, bool> matcher, bool flush)
    {
        ArgumentNullException.ThrowIfNull(message);
        var tasks = new List<Task>();
        foreach (var channel in _channels.Values)
        {
            if (channel is IServerChannel || matcher != null && !matcher(channel))
                continue;
            object copy = message switch
            {
                ByteBuf buffer => buffer.RetainedDuplicate(),
                IByteBufHolder holder => holder.RetainedDuplicate(),
                _ => ReferenceCountUtil.Retain(message)
            };
            try
            { tasks.Add(flush ? channel.WriteAndFlushAsync(copy) : channel.WriteAsync(copy)); }
            catch (Exception error) { ReferenceCountUtil.Release(copy); tasks.Add(Task.FromException(error)); }
        }
        ReferenceCountUtil.Release(message);
        return Task.WhenAll(tasks);
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
    public DefaultChannelGroup Flush(Func<IChannel, bool> matcher = null)
    { foreach (var channel in _channels.Values) if (channel is not IServerChannel && (matcher == null || matcher(channel))) channel.Flush(); return this; }
    private Task OperateAsync(Func<IChannel, Task> operation, Func<IChannel, bool> matcher, bool includeServers)
    {
        var tasks = new List<Task>();
        foreach (var channel in _channels.Values.OrderBy(channel => channel is IServerChannel ? 0 : 1))
        {
            if ((!includeServers && channel is IServerChannel) || matcher != null && !matcher(channel))
                continue;
            try
            { tasks.Add(operation(channel)); }
            catch (Exception error) { tasks.Add(Task.FromException(error)); }
        }
        return Task.WhenAll(tasks);
    }
    /**
     * Returns the {@link ChannelGroupFuture} which will be notified when all {@link Channel}s that are part of this
     * {@link ChannelGroup}, at the time of calling, are closed.
     */
    public Task NewCloseFuture(Func<IChannel, bool> matcher = null) => Task.WhenAll(_channels.Values.Where(channel => matcher == null || matcher(channel)).Select(channel => channel.CloseCompletion));
    public override string ToString() => $"DefaultChannelGroup(name: {Name}, size: {Count})";
}
