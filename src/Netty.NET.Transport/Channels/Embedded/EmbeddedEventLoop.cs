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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels.Embedded;

/// <summary>A manually driven event loop. Embedded calls are owned by the test thread, as in Netty.</summary>
public sealed class EmbeddedEventLoop : AbstractScheduledEventExecutor, IEventLoop
{
    private readonly ConcurrentQueue<Action> _tasks = new();
    private readonly Task _unsupported = Task.FromException(new NotSupportedException("Embedded loops are manually driven."));
    private readonly Ticker _ticker;
    public EmbeddedEventLoop(Ticker ticker = null) => _ticker = ticker ?? new FreezableTicker();
    public override Ticker Ticker() => _ticker;
    public override bool InEventLoop(Thread thread) => true;
    public override void Execute(Action task) { ArgumentNullException.ThrowIfNull(task); _tasks.Enqueue(task); }
    public void RunTasks() { while (_tasks.TryDequeue(out var task)) task(); }
    public bool HasPendingTasks => !_tasks.IsEmpty;
    public long RunScheduledTasks() { long now = _ticker.NanoTime(); while (PollScheduledTask(now) is { } task) task(); return NextScheduledTaskNano(); }
    public long NextScheduledTask() => NextScheduledTaskNano();
    public void CancelTasks() => CancelScheduledTasks();
    public Task RegisterAsync(IChannel channel, CancellationToken cancellationToken = default) => channel.RegisterAsync(this, cancellationToken);
    public override Task Termination => _unsupported;
    public override bool IsShutdown() => false;
    public override bool IsTerminated() => false;
    public override bool IsShuttingDown() => false;
    public override bool AwaitTermination(TimeSpan timeout) => false;
    public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => throw new NotSupportedException();
    [Obsolete]
    public override void Shutdown() => throw new NotSupportedException();

    internal sealed class FreezableTicker : MockTicker
    {
        private readonly Ticker _unfrozen = global::Netty.NET.Common.Concurrent.Ticker.SystemTicker();
        private long _startTime;
        private long _frozenTimestamp;
        private bool _timeFrozen;
        public override long NanoTime() => _timeFrozen ? _frozenTimestamp : unchecked(_unfrozen.NanoTime() - _startTime);
        public override void Advance(long amountNanos)
        { if (_timeFrozen) _frozenTimestamp = unchecked(_frozenTimestamp + amountNanos); else _startTime = unchecked(_startTime - amountNanos); }
        public override void Sleep(long delayNanos) => throw new NotSupportedException("Sleeping is not supported by the default embedded ticker.");
        internal void Freeze() { if (!_timeFrozen) { _frozenTimestamp = NanoTime(); _timeFrozen = true; } }
        internal void Unfreeze() { if (_timeFrozen) { _startTime = unchecked(_unfrozen.NanoTime() - _frozenTimestamp); _timeFrozen = false; } }
    }
}
