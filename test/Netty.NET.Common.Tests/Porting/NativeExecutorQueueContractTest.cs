using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class NativeExecutorQueueContractTest
{
    private sealed class Loop : SingleThreadEventExecutor
    {
        private readonly MockTicker clock = global::Netty.NET.Common.Concurrent.Ticker.NewMockTicker();
        internal IQueue<Action> Queue;
        internal Action Added;
        internal Action WakeChecked;
        internal int Capacity;
        internal bool Closed;

        internal Loop(Action<Action, SingleThreadEventExecutor> rejected = null)
            : base(null, _ => throw new Exception("unexpected worker start"), false, 16,
                rejected ?? RejectedExecutionHandlers.Reject()) { }
        public override bool InEventLoop(Thread thread) => thread == Thread.CurrentThread;
        public override bool IsShutdown() => Closed || base.IsShutdown();
        public override Ticker Ticker() => clock ?? global::Netty.NET.Common.Concurrent.Ticker.SystemTicker();
        protected override IQueue<Action> NewTaskQueue(int maxPendingTasks)
        {
            Capacity = maxPendingTasks;
            return Queue = base.NewTaskQueue(maxPendingTasks);
        }
        protected override void AddTask(Action task) { Added = task; base.AddTask(task); }
        protected override bool WakesUpForTask(Action task) { WakeChecked = task; return true; }
        protected override void Run() => throw new Exception("unexpected worker loop");
        internal Action Peek() => PeekTask();
        internal Action Poll() => PollTask();
        internal bool Remove(Action task) => RemoveTask(task);
        internal bool Drain() => RunAllTasks();
        internal bool Tail(IQueue<Action> queue) => RunAllTasksFrom(queue);
        internal bool Transfer(IQueue<Action> queue) => FetchFromScheduledTaskQueue(queue);
        internal IScheduledWork Head => PeekScheduledTask();
        internal void Advance(long nanos) => clock.Advance(nanos);
    }

    private sealed class Receiver
    {
        internal int Calls;
        internal void Invoke() => Calls++;
    }

    [Fact]
    public void NativeQueueHooksPreserveExactEntriesWhenMethodGroupsAreValueEqual()
    {
        var executor = new Loop();
        var receiver = new Receiver();
        Action first = new Action(receiver.Invoke), second = new Action(receiver.Invoke);
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
        executor.Execute(first);
        executor.Execute(second);
        Assert.Equal(16, executor.Capacity);
        Assert.Same(second, executor.Added);
        Assert.Same(second, executor.WakeChecked);
        Assert.True(executor.Remove(second));
        Assert.False(executor.Remove(second));
        Assert.Same(first, executor.Peek());
        Assert.Same(first, executor.Poll());
        first();
        Assert.Equal(1, receiver.Calls);
        Assert.Null(executor.Poll());
    }

    [Fact]
    public void NativeTailQueueContinuesAfterAFailingCallbackAndWakeupsDoNotBecomeUserWork()
    {
        var executor = new Loop();
        executor.Wakeup(false);
        Assert.Equal(1, executor.PendingTasks());
        Assert.Null(executor.Poll());
        var tail = new LinkedBlockingQueue<Action>(2, ReferenceEqualityComparer.Instance);
        int calls = 0;
        Assert.True(tail.TryEnqueue(() => throw new InvalidOperationException("tail failure")));
        Assert.True(tail.TryEnqueue(() => calls++));
        Assert.True(executor.Tail(tail));
        Assert.Equal(1, calls);
        Assert.False(executor.Tail(tail));
        Assert.Equal(0, executor.PendingTasks());
    }

    private sealed class RecordingQueue : IQueue<Action>
    {
        internal Action LastAttempt;
        private readonly LinkedBlockingQueue<Action> queue = new(1, ReferenceEqualityComparer.Instance);
        public int Count => queue.Count;
        public bool IsEmpty() => queue.IsEmpty();
        public bool TryRemove(Action item) => queue.TryRemove(item);
        public bool TryEnqueue(Action item) { LastAttempt = item; return queue.TryEnqueue(item); }
        public bool TryDequeue(out Action item) => queue.TryDequeue(out item);
        public bool TryPeek(out Action item) => queue.TryPeek(out item);
        public void Clear() => queue.Clear();
    }

    [Fact]
    public void DueWorkReusesItsCallbackAfterFullQueueRollbackAndPeriodicReinsertion()
    {
        var executor = new Loop();
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        Task completion = executor.ScheduleAtFixedRateAsync(() => calls++, TimeSpan.Zero,
            TimeSpan.FromTicks(1), cancellation.Token);
        IScheduledWork work = executor.Head;
        long id = work.GetId();
        var ready = new RecordingQueue();
        Action occupied = static () => { };
        Assert.True(ready.TryEnqueue(occupied));
        Assert.False(executor.Transfer(ready));
        Action callback = ready.LastAttempt;
        Assert.Same(work, executor.Head);
        Assert.False(executor.Transfer(ready));
        Assert.Same(callback, ready.LastAttempt);
        Assert.True(ready.TryDequeue(out var retained));
        Assert.Same(occupied, retained);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(executor.Transfer(ready));
            Assert.Same(callback, ready.LastAttempt);
            Assert.True(ready.TryDequeue(out retained));
            Assert.Same(callback, retained);
            retained();
            Assert.Equal(i + 1, calls);
            Assert.Same(work, executor.Head);
            Assert.Equal(id, work.GetId());
            executor.Advance(100);
        }
        Assert.True(executor.Transfer(ready));
        cancellation.Cancel();
        Assert.True(completion.IsCanceled);
        Assert.True(ready.TryDequeue(out retained));
        retained();
        Assert.Equal(3, calls);
        Assert.Null(executor.Head);
    }

    [Fact]
    public void RejectionPolicyReceivesTheOriginalCallbackAndCanReofferIt()
    {
        Action rejected = null;
        var executor = new Loop((callback, owner) =>
        {
            rejected = callback;
            var loop = (Loop)owner;
            Assert.NotNull(loop.Poll());
            Assert.True(owner.OfferTask(callback));
        });
        for (int i = 0; i < 16; i++) executor.Execute(static () => { });
        int calls = 0;
        Action original = () => calls++;
        executor.Execute(original);
        Assert.Same(original, rejected);
        Assert.True(executor.Drain());
        Assert.Equal(1, calls);
        Assert.Equal(0, executor.PendingTasks());
    }

    [Fact]
    public void ScheduledCallbacksKeepOneInvocationWhenCopiedOrComposed()
    {
        var executor = new Loop();
        int calls = 0, prefixes = 0;
        Task result = executor.ScheduleAsync(() => calls++, TimeSpan.FromTicks(1), TestContext.Current.CancellationToken);
        IScheduledWork work = executor.Head;
        Action callback = work.QueueCallback;
        Assert.Same(callback, work.QueueCallback);
        Assert.False((object)work is Netty.NET.Common.Functional.IRunnable);
        Action copied = (Action)callback.Clone();
        Assert.Equal(callback, copied);
        Assert.NotSame(callback, copied);
        Action forwarded = () => callback();
        Action composed = (() => prefixes++) + copied;

        executor.Advance(100);
        var ready = new LinkedBlockingQueue<Action>(1);
        Assert.True(executor.Transfer(ready));
        Assert.True(ready.TryDequeue(out var retained));
        Assert.Same(callback, retained);
        composed();
        retained();
        copied();
        forwarded();
        Assert.Equal(1, prefixes);
        Assert.Equal(1, calls);
        Assert.True(result.IsCompletedSuccessfully);
        Assert.False(executor.Drain());
    }

    [Fact]
    public void CachedOwnerCallbacksObserveLiveVirtualTimeAndShutdownState()
    {
        var executor = new Loop();
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        Task periodic = executor.ScheduleWithFixedDelayAsync(() =>
        {
            calls++;
            executor.Advance(700);
        }, TimeSpan.Zero, TimeSpan.FromTicks(3), cancellation.Token);
        Assert.True(executor.Drain());
        Assert.Equal(300, executor.Head.DelayNanos());
        executor.Advance(299);
        Assert.False(executor.Drain());
        Assert.Equal(1, calls);
        executor.Advance(1);
        executor.Closed = true;
        Assert.True(executor.Drain());
        Assert.Equal(1, calls);
        Assert.True(periodic.IsCanceled);
        Assert.Null(executor.Head);
    }
}
