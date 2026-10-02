using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class PendingWriteContractTest
{
    private sealed class Message : AbstractReferenceCounted
    {
        internal int Deallocations;
        internal Exception ReleaseError;
        public override IReferenceCounted touch(object hint) => this;
        protected override void deallocate()
        {
            ++Deallocations;
            if (ReleaseError != null) throw ReleaseError;
        }
    }

    [Fact]
    public async Task FailureReleasesTheMessageAndPublishesTheExactCause()
    {
        var message = new Message();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = PendingWrite.Rent(message, source);
        Task result = node.Completion;
        var cause = new InvalidOperationException("write failed");
        Assert.Same(message, node.Message);
        Assert.Same(source.Task, result);
        Assert.True(node.FailAndRecycle(cause));
        Assert.Same(cause, await Assert.ThrowsAsync<InvalidOperationException>(async () => await result));
        Assert.Equal(0, message.refCnt());
        Assert.Equal(1, message.Deallocations);
        Assert.Null(node.Message);
        Assert.Null(node.Completion);
        Assert.Throws<InvalidOperationException>(() => node.FailAndRecycle(cause));
        Assert.Equal(1, message.Deallocations);
    }

    [Fact]
    public async Task SuccessTransfersMessageOwnershipWithoutReleasingIt()
    {
        var message = new Message();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = PendingWrite.Rent(message, source);
        object nextOwnerMessage = node.Message;
        Task result = node.Completion;
        Assert.True(node.SucceedAndRecycle());
        await result;
        Assert.Equal(1, message.refCnt());
        Assert.Equal(0, message.Deallocations);
        Assert.Null(node.Message);
        Assert.Null(node.Completion);
        Assert.Throws<InvalidOperationException>(() => node.SucceedAndRecycle());
        Assert.True(ReferenceCountUtil.release(nextOwnerMessage));
    }

    [Fact]
    public async Task RecyclingTransfersTheProducerWithoutCompletingOrReleasing()
    {
        var message = new Message();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = PendingWrite.Rent(message, source);
        Task result = node.Completion;
        object nextOwnerMessage = node.Message;
        var nextProducer = node.RecycleAndGetCompletionSource();
        Assert.Same(source, nextProducer);
        Assert.False(result.IsCompleted);
        Assert.Equal(1, message.refCnt());
        Assert.Null(node.Message);
        Assert.Null(node.Completion);
        nextProducer.SetResult();
        await result;
        Assert.True(ReferenceCountUtil.release(nextOwnerMessage));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAlreadySettledProducerStillAllowsFailureCleanup(bool canceled)
    {
        var message = new Message();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        if (canceled) source.SetCanceled(cancellation.Token);
        else source.SetResult();
        var node = PendingWrite.Rent(message, source);
        Assert.True(node.FailAndRecycle(new InvalidOperationException("late failure")));
        Assert.Equal(1, message.Deallocations);
        Assert.Equal(canceled, source.Task.IsCanceled);
        Assert.Equal(!canceled, source.Task.IsCompletedSuccessfully);
        Assert.Null(node.Message);
        Assert.Null(node.Completion);
    }

    [Fact]
    public async Task CancellationExceptionAsFailureRemainsAnExactFault()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = new OperationCanceledException("write failed", cancellation.Token);
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = PendingWrite.Rent(new object(), source);
        node.FailAndRecycle(error);
        Assert.True(source.Task.IsFaulted);
        Assert.False(source.Task.IsCanceled);
        Assert.Same(error, await Assert.ThrowsAsync<OperationCanceledException>(async () => await source.Task));
    }

    [Fact]
    public void ReleaseFailureReturnsTheNodeWithoutPretendingTheWriteCompleted()
    {
        var error = new InvalidOperationException("release failed");
        var message = new Message { ReleaseError = error };
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = PendingWrite.Rent(message, source);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => node.FailAndRecycle(new Exception("write failed"))));
        Assert.Equal(1, message.Deallocations);
        Assert.False(source.Task.IsCompleted);
        Assert.Null(node.Message);
        Assert.Null(node.Completion);
        Assert.Throws<InvalidOperationException>(() => node.Recycle());
        // The write owner still holds its producer and can settle after catching the release error.
        source.SetException(error);
        Assert.Same(error, source.Task.Exception.InnerException);
    }

    [Fact]
    public void NullFailureDoesNotConsumeOwnershipAndAnUnobservedWriteStillReleases()
    {
        var message = new Message();
        var node = PendingWrite.Rent(message);
        Assert.Null(node.Completion);
        Assert.Throws<ArgumentNullException>(() => node.FailAndRecycle(null));
        Assert.Same(message, node.Message);
        Assert.Equal(1, message.refCnt());
        node.FailAndRecycle(new Exception("unobserved write failed"));
        Assert.Equal(1, message.Deallocations);
        var transferred = new Message();
        var next = PendingWrite.Rent(transferred);
        Assert.True(next.Recycle());
        Assert.Equal(1, transferred.refCnt());
        Assert.True(transferred.release());
    }
}
