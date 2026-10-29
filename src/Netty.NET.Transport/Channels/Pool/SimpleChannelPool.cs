/*
 * Copyright 2015 The Netty Project
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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;

namespace Netty.NET.Transport.Channels.Pool;

/**
 * Simple {@link ChannelPool} implementation which will create new {@link Channel}s if someone tries to acquire
 * a {@link Channel} but none is in the pool atm. No limit on the maximal concurrent {@link Channel}s is enforced.
 *
 * This implementation uses LIFO order for {@link Channel}s in the {@link ChannelPool}.
 *
 */
public class SimpleChannelPool : IChannelPool
{
    private static readonly AttributeKey<SimpleChannelPool> PoolKey = AttributeKey<SimpleChannelPool>.NewInstance("io.netty.channel.pool.SimpleChannelPool");
    private readonly LinkedList<IChannel> _channels = new();
    private readonly object _storageLock = new();
    private readonly bool _lastRecentUsed;
    /**
     * Returns the {@link Bootstrap} this pool will use to open new connections.
     *
     * @return the {@link Bootstrap} this pool will use to open new connections
     */
    protected internal Bootstrap Bootstrap { get; }
    /**
     * Returns the {@link ChannelPoolHandler} that will be notified for the different pool actions.
     *
     * @return the {@link ChannelPoolHandler} that will be notified for the different pool actions
     */
    protected internal IChannelPoolHandler Handler { get; }
    /**
     * Returns the {@link ChannelHealthChecker} that will be used to check if a {@link Channel} is healthy.
     *
     * @return the {@link ChannelHealthChecker} that will be used to check if a {@link Channel} is healthy
     */
    protected internal Func<IChannel, Task<bool>> HealthChecker { get; }
    /**
     * Indicates whether this pool will check the health of channels before offering them back into the pool.
     *
     * @return {@code true} if this pool will check the health of channels before offering them back into the pool, or
     * {@code false} if channel health is only checked at acquisition time
     */
    protected internal bool ReleaseHealthCheck { get; }

    /**
     * Creates a new instance using the {@link ChannelHealthChecker#ACTIVE}.
     *
     * @param bootstrap         the {@link Bootstrap} that is used for connections
     * @param handler           the {@link ChannelPoolHandler} that will be notified for the different pool actions
     */
    public SimpleChannelPool(Bootstrap bootstrap, IChannelPoolHandler handler)
        : this(bootstrap, handler, ChannelHealthChecker.Active) { }

    /**
     * Creates a new instance.
     *
     * @param bootstrap         the {@link Bootstrap} that is used for connections
     * @param handler           the {@link ChannelPoolHandler} that will be notified for the different pool actions
     * @param healthCheck       the {@link ChannelHealthChecker} that will be used to check if a {@link Channel} is
     *                          still healthy when obtain from the {@link ChannelPool}
     */
    public SimpleChannelPool(Bootstrap bootstrap, IChannelPoolHandler handler, Func<IChannel, Task<bool>> healthCheck)
        : this(bootstrap, handler, healthCheck, true) { }

    /**
     * Creates a new instance.
     *
     * @param bootstrap          the {@link Bootstrap} that is used for connections
     * @param handler            the {@link ChannelPoolHandler} that will be notified for the different pool actions
     * @param healthCheck        the {@link ChannelHealthChecker} that will be used to check if a {@link Channel} is
     *                           still healthy when obtain from the {@link ChannelPool}
     * @param releaseHealthCheck will check channel health before offering back if this parameter set to {@code true};
     *                           otherwise, channel health is only checked at acquisition time
     */
    public SimpleChannelPool(Bootstrap bootstrap, IChannelPoolHandler handler, Func<IChannel, Task<bool>> healthCheck,
        bool releaseHealthCheck)
        : this(bootstrap, handler, healthCheck, releaseHealthCheck, true) { }

    /**
     * Creates a new instance.
     *
     * @param bootstrap          the {@link Bootstrap} that is used for connections
     * @param handler            the {@link ChannelPoolHandler} that will be notified for the different pool actions
     * @param healthCheck        the {@link ChannelHealthChecker} that will be used to check if a {@link Channel} is
     *                           still healthy when obtain from the {@link ChannelPool}
     * @param releaseHealthCheck will check channel health before offering back if this parameter set to {@code true};
     *                           otherwise, channel health is only checked at acquisition time
     * @param lastRecentUsed    {@code true} {@link Channel} selection will be LIFO, if {@code false} FIFO.
     */
    public SimpleChannelPool(Bootstrap bootstrap, IChannelPoolHandler handler, Func<IChannel, Task<bool>> healthCheck,
        bool releaseHealthCheck, bool lastRecentUsed)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        HealthChecker = healthCheck ?? throw new ArgumentNullException(nameof(healthCheck));
        ReleaseHealthCheck = releaseHealthCheck;
        // Clone the original Bootstrap as we want to set our own handler
        Bootstrap = bootstrap.Clone().Handler(new ActionChannelInitializer<IChannel>(channel =>
        {
            Debug.Assert(channel.EventLoop.InEventLoop());
            handler.ChannelCreated(channel);
        }));
        _lastRecentUsed = lastRecentUsed;
    }

    public virtual Task<IChannel> AcquireAsync(CancellationToken cancellationToken = default)
    {
        var producer = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => producer.TrySetCanceled(cancellationToken));
        _ = AcquireHealthyAsync(producer, registration);
        return producer.Task;
    }

    /**
     * Tries to retrieve healthy channel from the pool if any or creates a new channel otherwise.
     * @param promise the promise to provide acquire result.
     * @return future for acquiring a channel.
     */
    private async Task AcquireHealthyAsync(TaskCompletionSource<IChannel> producer, CancellationTokenRegistration registration)
    {
        IChannel channel = null;
        try
        {
            for (;;)
            {
                channel = PollChannel();
                if (channel == null)
                {
                    // No Channel left in the pool bootstrap a new Channel
                    // Cancellation completes the consumer promptly, but the connection must still be
                    // reclaimed if it succeeds after cancellation, as with the original acquire promise.
                    channel = await ConnectChannelAsync(Bootstrap.Clone().Attr(PoolKey, this)).ConfigureAwait(false);
                    await NotifyAcquiredAsync(channel, producer).ConfigureAwait(false);
                    return;
                }

                Task<bool> healthCheck = null;
                await OnLoopAsync(channel, () => healthCheck = HealthChecker(channel)).ConfigureAwait(false);
                bool healthy;
                try { healthy = await healthCheck.ConfigureAwait(false); }
                catch { healthy = false; }
                if (healthy)
                {
                    await NotifyAcquiredAsync(channel, producer).ConfigureAwait(false);
                    return;
                }
                await CloseChannelAsync(channel).ConfigureAwait(false);
                channel = null;
            }
        }
        catch (Exception error)
        {
            if (channel != null) await CloseChannelAsync(channel).ConfigureAwait(false);
            producer.TrySetException(error);
        }
        finally { registration.Unregister(); }
    }

    private async Task NotifyAcquiredAsync(IChannel channel, TaskCompletionSource<IChannel> producer)
    {
        bool returned = false;
        await OnLoopAsync(channel, () =>
        {
            channel.Attr(PoolKey).Set(this);
            Handler.ChannelAcquired(channel);
            returned = !producer.TrySetResult(channel);
        }).ConfigureAwait(false);
        if (returned)
        {
            // Promise was completed in the meantime (like cancelled), just release the channel again
            // Cleanup is observed even though the cancelled consumer no longer awaits it.
            try { await ReleaseAsync(channel).ConfigureAwait(false); }
            catch { /* ReleaseAsync closes the channel on failure. */ }
        }
    }

    /**
     * Bootstrap a new {@link Channel}. The default implementation uses {@link Bootstrap#connect()}, sub-classes may
     * override this.
     * <p>
     * The {@link Bootstrap} that is passed in here is cloned via {@link Bootstrap#clone()}, so it is safe to modify.
     */
    protected virtual Task<IChannel> ConnectChannelAsync(Bootstrap bootstrap) => bootstrap.ConnectAsync();

    /**
     * Adds the channel back to the pool only if the channel is healthy.
     * @param channel the channel to put back to the pool
     * @param promise offer operation promise.
     * @param future the future that contains information fif channel is healthy or not.
     * @throws Exception in case when failed to notify handler about release operation.
     */
    public virtual async Task ReleaseAsync(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        try
        {
            await OnLoopAsync(channel, () =>
            {
                // Remove the POOL_KEY attribute from the Channel and check if it was acquired from this pool, if not fail.
                if (!ReferenceEquals(channel.Attr(PoolKey).GetAndSet(null), this))
                {
                    // Better include a stacktrace here as this is an user error.
                    throw new ChannelNotAcquiredFromPoolException(channel);
                }
            }).ConfigureAwait(false);

            bool healthy = true;
            if (ReleaseHealthCheck)
            {
                Task<bool> check = null;
                await OnLoopAsync(channel, () => check = HealthChecker(channel)).ConfigureAwait(false);
                healthy = await check.ConfigureAwait(false);
            }

            await OnLoopAsync(channel, () =>
            {
                if (healthy)
                {
                    //channel turns out to be healthy, offering and releasing it.
                    if (!OfferChannel(channel)) throw new ChannelPoolFullException();
                    Handler.ChannelReleased(channel);
                }
                else
                {
                    //channel not healthy, just releasing it.
                    Handler.ChannelReleased(channel);
                    channel.Attr(PoolKey).Set(null);
                    _ = channel.CloseAsync();
                }
            }).ConfigureAwait(false);
        }
        catch
        {
            await CloseChannelAsync(channel).ConfigureAwait(false);
            throw;
        }
    }

    private static Task OnLoopAsync(IChannel channel, Action action)
    {
        if (channel is AbstractChannel native) return native.OnLoopAsync(action);
        if (!channel.EventLoop.InEventLoop()) return channel.EventLoop.SubmitAsync(action);
        action();
        return Task.CompletedTask;
    }

    private static async Task CloseChannelAsync(IChannel channel)
    {
        channel.Attr(PoolKey).Set(null);
        await global::Netty.NET.Transport.Bootstrapping.Bootstrap.CleanupAsync(channel).ConfigureAwait(false);
    }

    /**
     * Poll a {@link Channel} out of the internal storage to reuse it. This will return {@code null} if no
     * {@link Channel} is ready to be reused.
     *
     * Sub-classes may override {@link #pollChannel()} and {@link #offerChannel(Channel)}. Be aware that
     * implementations of these methods needs to be thread-safe!
     */
    protected virtual IChannel PollChannel()
    {
        lock (_storageLock)
        {
            var node = _lastRecentUsed ? _channels.Last : _channels.First;
            if (node == null) return null;
            _channels.Remove(node);
            return node.Value;
        }
    }

    /**
     * Offer a {@link Channel} back to the internal storage. This will return {@code true} if the {@link Channel}
     * could be added, {@code false} otherwise.
     *
     * Sub-classes may override {@link #pollChannel()} and {@link #offerChannel(Channel)}. Be aware that
     * implementations of these methods needs to be thread-safe!
     */
    protected virtual bool OfferChannel(IChannel channel)
    {
        lock (_storageLock) _channels.AddLast(channel);
        return true;
    }

    /**
     * Closes the pool in an async manner.
     *
     * @return Future which represents completion of the close task
     */
    public virtual async Task CloseAsync()
    {
        // Execute close asynchronously in case this is being invoked on an eventloop to avoid blocking
        for (;;)
        {
            var channel = PollChannel();
            if (channel == null) return;
            // Just ignore any errors that are reported back from close().
            await CloseChannelAsync(channel).ConfigureAwait(false);
        }
    }

    // CLR: native awaits replace blocking channel futures. CloseAsync can safely be called on
    // an event loop; synchronous Close/Dispose retain the source's blocking caller contract.
    public virtual void Close() => CloseAsync().GetAwaiter().GetResult();
    public void Dispose() => Close();
    public ValueTask DisposeAsync() => new(CloseAsync());

    /**
     * Fails the release of a {@link Channel} that was not acquired from this pool. This lets {@link FixedChannelPool}
     * tell it apart from other release failures, which may also be {@link IllegalArgumentException}s thrown by user
     * code.
     */
    internal sealed class ChannelNotAcquiredFromPoolException(IChannel channel)
        : ArgumentException("Channel " + channel + " was not acquired from this ChannelPool");

    // Suppress a warning since the method doesn't need synchronization
    // CLR: fillInStackTrace synchronization/suppression is JVM-specific; native exception stacks are retained.
    private sealed class ChannelPoolFullException() : InvalidOperationException("ChannelPool full");
}
