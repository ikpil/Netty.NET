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
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;

namespace Netty.NET.Transport.Channels.Pool;

/**
 * {@link ChannelPool} implementation that takes another {@link ChannelPool} implementation and enforce a maximum
 * number of concurrent connections.
 */
public class FixedChannelPool : SimpleChannelPool
{
    public enum AcquireTimeoutAction
    {
        /**
         * Create a new connection when the timeout is detected.
         */
        New,
        /**
         * Fail the {@link Future} of the acquire call with a {@link TimeoutException}.
         */
        Fail
    }

    private readonly IEventExecutor _executor;
    private readonly AcquireTimeoutAction? _timeoutAction;
    private readonly TimeSpan _acquireTimeout;
    private readonly long _acquireTimeoutNanos;
    // There is no need to worry about synchronization as everything that modified the queue or counts is done
    // by the above EventExecutor.
    private readonly LinkedList<AcquireRequest> _pending = new();
    private readonly int _maxConnections;
    private readonly int _maxPendingAcquires;
    private int _acquiredChannelCount;
    private bool _closed;
    private Task _closeTask;

    /**
     * Creates a new instance using the {@link ChannelHealthChecker#ACTIVE}.
     *
     * @param bootstrap         the {@link Bootstrap} that is used for connections
     * @param handler           the {@link ChannelPoolHandler} that will be notified for the different pool actions
     * @param maxConnections    the number of maximal active connections, once this is reached new tries to acquire
     *                          a {@link Channel} will be delayed until a connection is returned to the pool again.
     */
    public FixedChannelPool(Bootstrap bootstrap, IChannelPoolHandler handler, int maxConnections)
        : this(bootstrap, handler, maxConnections, int.MaxValue) { }

    /**
     * Creates a new instance using the {@link ChannelHealthChecker#ACTIVE}.
     *
     * @param bootstrap             the {@link Bootstrap} that is used for connections
     * @param handler               the {@link ChannelPoolHandler} that will be notified for the different pool actions
     * @param maxConnections        the number of maximal active connections, once this is reached new tries to
     *                              acquire a {@link Channel} will be delayed until a connection is returned to the
     *                              pool again.
     * @param maxPendingAcquires    the maximum number of pending acquires. Once this is exceed acquire tries will
     *                              be failed.
     */
    public FixedChannelPool(Bootstrap bootstrap, IChannelPoolHandler handler, int maxConnections, int maxPendingAcquires)
        : this(bootstrap, handler, ChannelHealthChecker.Active, null, Timeout.InfiniteTimeSpan, maxConnections, maxPendingAcquires) { }

    /**
     * Creates a new instance.
     *
     * @param bootstrap             the {@link Bootstrap} that is used for connections
     * @param handler               the {@link ChannelPoolHandler} that will be notified for the different pool actions
     * @param healthCheck           the {@link ChannelHealthChecker} that will be used to check if a {@link Channel} is
     *                              still healthy when obtain from the {@link ChannelPool}
     * @param action                the {@link AcquireTimeoutAction} to use or {@code null} if non should be used.
     *                              In this case {@param acquireTimeoutMillis} must be {@code -1}.
     * @param acquireTimeoutMillis  the time (in milliseconds) after which an pending acquire must complete or
     *                              the {@link AcquireTimeoutAction} takes place.
     * @param maxConnections        the number of maximal active connections, once this is reached new tries to
     *                              acquire a {@link Channel} will be delayed until a connection is returned to the
     *                              pool again.
     * @param maxPendingAcquires    the maximum number of pending acquires. Once this is exceed acquire tries will
     *                              be failed.
     */
    public FixedChannelPool(Bootstrap bootstrap, IChannelPoolHandler handler, Func<IChannel, Task<bool>> healthCheck,
        AcquireTimeoutAction? action, TimeSpan acquireTimeout, int maxConnections, int maxPendingAcquires)
        : this(bootstrap, handler, healthCheck, action, acquireTimeout, maxConnections, maxPendingAcquires, true) { }

    /**
     * Creates a new instance.
     *
     * @param bootstrap             the {@link Bootstrap} that is used for connections
     * @param handler               the {@link ChannelPoolHandler} that will be notified for the different pool actions
     * @param healthCheck           the {@link ChannelHealthChecker} that will be used to check if a {@link Channel} is
     *                              still healthy when obtain from the {@link ChannelPool}
     * @param action                the {@link AcquireTimeoutAction} to use or {@code null} if non should be used.
     *                              In this case {@param acquireTimeoutMillis} must be {@code -1}.
     * @param acquireTimeoutMillis  the time (in milliseconds) after which an pending acquire must complete or
     *                              the {@link AcquireTimeoutAction} takes place.
     * @param maxConnections        the number of maximal active connections, once this is reached new tries to
     *                              acquire a {@link Channel} will be delayed until a connection is returned to the
     *                              pool again.
     * @param maxPendingAcquires    the maximum number of pending acquires. Once this is exceed acquire tries will
     *                              be failed.
     * @param releaseHealthCheck    will check channel health before offering back if this parameter set to
     *                              {@code true}.
     */
    public FixedChannelPool(Bootstrap bootstrap, IChannelPoolHandler handler, Func<IChannel, Task<bool>> healthCheck,
        AcquireTimeoutAction? action, TimeSpan acquireTimeout, int maxConnections, int maxPendingAcquires, bool releaseHealthCheck)
        : this(bootstrap, handler, healthCheck, action, acquireTimeout, maxConnections, maxPendingAcquires, releaseHealthCheck, true) { }

    /**
     * Creates a new instance.
     *
     * @param bootstrap             the {@link Bootstrap} that is used for connections
     * @param handler               the {@link ChannelPoolHandler} that will be notified for the different pool actions
     * @param healthCheck           the {@link ChannelHealthChecker} that will be used to check if a {@link Channel} is
     *                              still healthy when obtain from the {@link ChannelPool}
     * @param action                the {@link AcquireTimeoutAction} to use or {@code null} if non should be used.
     *                              In this case {@param acquireTimeoutMillis} must be {@code -1}.
     * @param acquireTimeoutMillis  the time (in milliseconds) after which an pending acquire must complete or
     *                              the {@link AcquireTimeoutAction} takes place.
     * @param maxConnections        the number of maximal active connections, once this is reached new tries to
     *                              acquire a {@link Channel} will be delayed until a connection is returned to the
     *                              pool again.
     * @param maxPendingAcquires    the maximum number of pending acquires. Once this is exceed acquire tries will
     *                              be failed.
     * @param releaseHealthCheck    will check channel health before offering back if this parameter set to
     *                              {@code true}.
     * @param lastRecentUsed        {@code true} {@link Channel} selection will be LIFO, if {@code false} FIFO.
     */
    public FixedChannelPool(Bootstrap bootstrap, IChannelPoolHandler handler, Func<IChannel, Task<bool>> healthCheck,
        AcquireTimeoutAction? action, TimeSpan acquireTimeout, int maxConnections, int maxPendingAcquires,
        bool releaseHealthCheck, bool lastRecentUsed)
        : base(bootstrap, handler, healthCheck, releaseHealthCheck, lastRecentUsed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConnections);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPendingAcquires);
        if (action == null && acquireTimeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentNullException(nameof(action));
        if (action != null && acquireTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(acquireTimeout));
        if (action != null && action != AcquireTimeoutAction.New && action != AcquireTimeoutAction.Fail)
            throw new ArgumentOutOfRangeException(nameof(action));
        _executor = bootstrap.NextEventLoop();
        _maxConnections = maxConnections;
        _maxPendingAcquires = maxPendingAcquires;
        _timeoutAction = action;
        _acquireTimeout = acquireTimeout;
        // CLR TimeSpan ticks are 100ns; saturate like TimeUnit.MILLISECONDS.toNanos rather than overflow.
        _acquireTimeoutNanos = action == null ? -1 : acquireTimeout.Ticks > long.MaxValue / 100 ? long.MaxValue : acquireTimeout.Ticks * 100;
    }

    /** Returns the number of acquired channels that this pool thinks it has. */
    public int AcquiredChannelCount => Volatile.Read(ref _acquiredChannelCount);

    public override Task<IChannel> AcquireAsync(CancellationToken cancellationToken = default)
    {
        var request = new AcquireRequest(cancellationToken);
        try
        {
            if (_executor.InEventLoop())
                Acquire0(request);
            else
                _executor.Execute(() => Acquire0(request));
        }
        catch (Exception error) { request.Fail(error); }
        return request.Producer.Task;
    }

    private void Acquire0(AcquireRequest request)
    {
        Debug.Assert(_executor.InEventLoop());
        try
        {
            if (_closed)
            { request.Fail(ClosedException()); return; }
            if (_acquiredChannelCount < _maxConnections)
            {
                StartAcquire(request);
            }
            else if (_pending.Count >= _maxPendingAcquires)
            {
                request.Fail(new InvalidOperationException("Too many outstanding acquire operations"));
            }
            else
            {
                request.Node = _pending.AddLast(request);
                if (_timeoutAction != null)
                {
                    request.ExpireNanoTime = unchecked(_executor.Ticker().NanoTime() + _acquireTimeoutNanos);
                    request.TimeoutCancellation = new CancellationTokenSource();
                    _ = ObserveTimeoutAsync(request, _executor.ScheduleAsync(OnTimeout, _acquireTimeout, request.TimeoutCancellation.Token));
                }
            }
        }
        catch (Exception error)
        {
            if (request.Node != null)
            { _pending.Remove(request.Node); request.Node = null; }
            request.Fail(error);
        }
    }

    private void StartAcquire(AcquireRequest request)
    {
        Debug.Assert(_executor.InEventLoop());
        if (!request.Acquired)
        {
            Interlocked.Increment(ref _acquiredChannelCount);
            request.Acquired = true;
        }
        // We need to create a new promise as we need to ensure the AcquireListener runs in the correct
        // EventLoop
        // CLR: base AcquireAsync owns a separate native producer. Consumer cancellation never abandons it.
        Task<IChannel> acquisition;
        try
        { acquisition = base.AcquireAsync(); }
        catch (Exception error) { acquisition = Task.FromException<IChannel>(error); }
        _ = ObserveAcquireAsync(request, acquisition);
    }

    private async Task ObserveAcquireAsync(AcquireRequest request, Task<IChannel> acquisition)
    {
        IChannel channel = null;
        Exception failure = null;
        try
        { channel = await acquisition.ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        try
        {
            // Always queue native completion. This supplies the source promise listener's bounded
            // notification depth when many cancelled pending acquires return their channels in a row.
            await _executor.SubmitAsync(() => FinishAcquire(request, channel, failure)).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (channel != null)
                await global::Netty.NET.Transport.Bootstrapping.Bootstrap.CleanupAsync(channel).ConfigureAwait(false);
            request.Fail(error);
        }
    }

    private void FinishAcquire(AcquireRequest request, IChannel channel, Exception failure)
    {
        Debug.Assert(_executor.InEventLoop());
        if (_closed)
        {
            // Since the pool is closed, we have no choice but to close the channel
            if (channel != null)
                _ = global::Netty.NET.Transport.Bootstrapping.Bootstrap.CleanupAsync(channel);
            request.Fail(ClosedException());
            return;
        }
        if (failure != null)
        {
            if (request.Acquired)
                DecrementAndRunTaskQueue();
            else
                RunTaskQueue();
            request.Fail(failure);
        }
        else
        {
            if (!request.Producer.TrySetResult(channel))
            {
                // Promise was completed in the meantime (like cancelled), just release the channel again.
                _ = ReleaseCancelledAcquireAsync(channel);
            }
            request.Finish();
        }
    }

    private async Task ReleaseCancelledAcquireAsync(IChannel channel)
    {
        try
        { await ReleaseAsync(channel).ConfigureAwait(false); }
        catch { /* ReleaseAsync closes the channel and reconciles the slot even on failure. */ }
    }

    public override async Task ReleaseAsync(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        Exception failure = null;
        try
        { await base.ReleaseAsync(channel).ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        try
        {
            await OnExecutorAsync(() =>
            {
                if (_closed)
                {
                    // Since the pool is closed, we have no choice but to close the channel
                    _ = global::Netty.NET.Transport.Bootstrapping.Bootstrap.CleanupAsync(channel);
                    throw ClosedException();
                }
                // Check if the exception was not because of we passed the Channel to the wrong pool.
                // Other failures (for example an IllegalArgumentException thrown by the ChannelPoolHandler)
                // must still give back the slot.
                if (failure is not ChannelNotAcquiredFromPoolException)
                    DecrementAndRunTaskQueue();
                if (failure != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }).ConfigureAwait(false);
        }
        catch
        {
            await global::Netty.NET.Transport.Bootstrapping.Bootstrap.CleanupAsync(channel).ConfigureAwait(false);
            throw;
        }
    }

    private Task OnExecutorAsync(Action action)
    {
        if (!_executor.InEventLoop())
            return _executor.SubmitAsync(action);
        try
        { action(); return Task.CompletedTask; }
        catch (Exception error) { return Task.FromException(error); }
    }

    private void DecrementAndRunTaskQueue()
    {
        // We should never have a negative value.
        int currentCount = Interlocked.Decrement(ref _acquiredChannelCount);
        Debug.Assert(currentCount >= 0);
        // Run the pending acquire tasks before notify the original promise so if the user would
        // try to acquire again from the ChannelFutureListener and the pendingAcquireCount is >=
        // maxPendingAcquires we may be able to run some pending tasks first and so allow to add
        // more.
        RunTaskQueue();
    }

    private void RunTaskQueue()
    {
        while (_acquiredChannelCount < _maxConnections && _pending.First != null)
        {
            var request = _pending.First.Value;
            _pending.RemoveFirst();
            request.Node = null;
            // Cancel the timeout if one was scheduled
            request.CancelTimeout();
            StartAcquire(request);
        }
        // We should never have a negative value.
        Debug.Assert(_acquiredChannelCount >= 0);
    }

    private void OnTimeout()
    {
        Debug.Assert(_executor.InEventLoop());
        long nanoTime = _executor.Ticker().NanoTime();
        for (; ; )
        {
            var request = _pending.First?.Value;
            // Compare nanoTime as descripted in the javadocs of System.nanoTime()
            //
            // See https://docs.oracle.com/javase/7/docs/api/java/lang/System.html#nanoTime()
            // See https://github.com/netty/netty/issues/3705
            if (request == null || unchecked(nanoTime - request.ExpireNanoTime) < 0)
                break;
            _pending.RemoveFirst();
            request.Node = null;
            request.CancelTimeout();
            if (_timeoutAction == AcquireTimeoutAction.Fail)
            {
                // Fail the promise as we timed out.
                FinishAcquire(request, null, new AcquireTimeoutException());
            }
            else
            {
                // Increment the acquire count and delegate to super to actually acquire a Channel which will
                // create a new connection.
                StartAcquire(request);
            }
        }
    }

    private async Task ObserveTimeoutAsync(AcquireRequest request, Task scheduled)
    {
        try
        { await scheduled.ConfigureAwait(false); }
        catch (OperationCanceledException) when (scheduled.IsCanceled) { }
        catch (Exception error)
        {
            try
            {
                await OnExecutorAsync(() =>
                {
                    if (request.Node == null)
                        return;
                    _pending.Remove(request.Node);
                    request.Node = null;
                    FinishAcquire(request, null, error);
                }).ConfigureAwait(false);
            }
            catch (Exception rejection) { request.Fail(rejection); }
        }
    }

    /**
     * Closes the pool in an async manner.
     *
     * @return Future which represents completion of the close task
     */
    public override Task CloseAsync()
    {
        // CLR awaits supply nonblocking completion; there is no GlobalEventExecutor worker
        // whose only purpose is to block on channel-close futures.
        return _executor.InEventLoop() ? Close0() : _executor.SubmitAsync(Close0);
    }

    private Task Close0()
    {
        Debug.Assert(_executor.InEventLoop());
        if (_closed)
            return _closeTask ?? Task.CompletedTask;
        _closed = true;
        while (_pending.First != null)
        {
            var request = _pending.First.Value;
            _pending.RemoveFirst();
            request.Node = null;
            request.Fail(ClosedException());
        }
        Interlocked.Exchange(ref _acquiredChannelCount, 0);
        // Ensure we dispatch this on another Thread as close0 will be called from the EventExecutor and we need
        // to ensure we will not block in a EventExecutor.
        // CLR: base CloseAsync marshals channel operations and awaits them without blocking this executor.
        return _closeTask = base.CloseAsync();
    }

    private static InvalidOperationException ClosedException() => new("FixedChannelPool was closed");

    // AcquireTask extends AcquireListener to reduce object creations and so GC pressure
    // CLR: one request holds native producer/queue/deadline state instead of a promise-listener hierarchy.
    private sealed class AcquireRequest
    {
        internal readonly TaskCompletionSource<IChannel> Producer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;
        internal LinkedListNode<AcquireRequest> Node;
        internal CancellationTokenSource TimeoutCancellation;
        internal long ExpireNanoTime;
        internal bool Acquired;
        // We need to create a new promise as we need to ensure the AcquireListener runs in the correct
        // EventLoop.
        // CLR: request producer ownership and executor dispatch are independent native concerns.
        internal AcquireRequest(CancellationToken token) => _registration = token.Register(() => Producer.TrySetCanceled(token));
        internal void Fail(Exception error) { Producer.TrySetException(error); Finish(); }
        internal void Finish() { _registration.Unregister(); CancelTimeout(); }
        internal void CancelTimeout()
        {
            var cancellation = Interlocked.Exchange(ref TimeoutCancellation, null);
            if (cancellation == null)
                return;
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    // Suppress a warning since the method doesn't need synchronization
    // CLR: retain native exception stacks; the JVM fillInStackTrace override has no portable purpose.
    private sealed class AcquireTimeoutException() : TimeoutException("Acquire operation took longer then configured maximum time");
}
