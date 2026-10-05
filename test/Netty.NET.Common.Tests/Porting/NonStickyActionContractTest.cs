using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class NonStickyActionContractTest
{
    private sealed class ManualExecutor : AbstractEventExecutor
    {
        internal readonly Queue<Action> Pending = new();
        public override void Execute(Action command) => Pending.Enqueue(command);
        internal void RunNext() => Pending.Dequeue()();
        public override bool InEventLoop(Thread thread) => false;
        public override bool IsShuttingDown() => false;
        public override bool IsShutdown() => false;
        public override bool IsTerminated() => false;
        [Obsolete]
        public override void Shutdown() => throw new NotSupportedException();
        public override Task Termination => throw new NotSupportedException();
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => throw new NotSupportedException();
        public override bool AwaitTermination(TimeSpan timeout) => false;
    }

    private sealed class OwnedSubmission : INativeSubmission
    {
        private readonly TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Result => source.Task;
        internal int Runs, Cancellations, Rejections;
        public bool IsCanceled => source.Task.IsCanceled;
        public void Run() { Runs++; source.TrySetResult(); }
        public void CancelForShutdown() { Cancellations++; source.TrySetCanceled(); }
        public void Reject(Exception error) { Rejections++; source.TrySetException(error); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingSettlementRecognizesOnlyTheExactIssuedSubmission(bool reject)
    {
        var executor = new ManualExecutor();
        IEventExecutor child = new NonStickyEventExecutorGroup(executor).Next();
        var submission = new OwnedSubmission();
        Action issued = ExecutorWork.Wrap(submission);
        Action copied = (Action)issued.Clone();
        int callerRuns = 0;
        Action prefix = () => callerRuns++;
        child.Execute(issued);
        child.Execute(copied);
        child.Execute(prefix + issued);
        child.Execute(() => callerRuns++);
        var runner = Assert.IsAssignableFrom<INativeSubmission>(ExecutorWork.GetNativeSubmission(Assert.Single(executor.Pending)));
        var cause = new RejectedExecutionException("runner rejected");
        if (reject)
        {
            runner.Reject(cause);
            Assert.Same(cause, await Assert.ThrowsAsync<RejectedExecutionException>(() => submission.Result));
        }
        else
        {
            runner.CancelForShutdown();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submission.Result);
        }
        runner.CancelForShutdown();
        runner.Reject(cause);
        executor.RunNext();
        Assert.Equal(0, submission.Runs);
        Assert.Equal(reject ? 0 : 1, submission.Cancellations);
        Assert.Equal(reject ? 1 : 0, submission.Rejections);
        Assert.Equal(0, callerRuns);
    }

    [Fact]
    public void NativeMulticastFailurePreservesReentrantFifoAcrossBatchHandoffs()
    {
        var executor = new ManualExecutor();
        IEventExecutor child = new NonStickyEventExecutorGroup(executor, 1).Next();
        var order = new List<int>();
        bool ownedThread = true;
        Action first = () =>
        {
            ownedThread &= child.InEventLoop();
            order.Add(1);
            child.Execute(() => { ownedThread &= child.InEventLoop(); order.Add(4); });
        };
        Action failing = () => { order.Add(2); throw new InvalidOperationException("multicast"); };
        Action skipped = () => order.Add(-1);
        child.Execute(first);
        child.Execute(failing + skipped);
        child.Execute(() => { ownedThread &= child.InEventLoop(); order.Add(3); });
        int runners = 0;
        while (executor.Pending.Count != 0 && ++runners <= 5) executor.RunNext();
        Assert.Empty(executor.Pending);
        Assert.Equal(5, runners);
        Assert.Equal(new[] { 1, 2, 3, 4 }, order);
        Assert.True(ownedThread);
        Assert.False(child.InEventLoop());
        Assert.Equal("command", Assert.Throws<ArgumentNullException>(() => child.Execute(null)).ParamName);
    }
}
