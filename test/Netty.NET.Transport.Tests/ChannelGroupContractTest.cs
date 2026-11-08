using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Groups;

namespace Netty.NET.Transport.Tests;

// Supplemental source-contract tests. The upstream DefaultChannelGroupTest has one separate original mapping.
#pragma warning disable xUnit1051 // Child tasks and cleanup have independent ownership.
public class ChannelGroupContractTest
{
    [Theory]
    [InlineData("write")]
    [InlineData("writeAndFlush")]
    [InlineData("disconnect")]
    [InlineData("deregister")]
    [InlineData("close")]
    [InlineData("closeFuture")]
    public async Task FailureWaitsForEverySelectedChannelAndRetainsEachCause(string operation)
    {
        var firstError = new IOException("first");
        var lastError = new InvalidOperationException("last");
        var last = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var success = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstChannel = Channel(first.Task, operation);
        var successful = Channel(success.Task, operation);
        var lastChannel = Channel(last.Task, operation);
        var excluded = Channel(Task.CompletedTask, operation);
        var group = new DefaultChannelGroup();
        foreach (var channel in new[] { firstChannel, successful, lastChannel, excluded }) group.Add(channel.Object);

        Task result = Operate(group, operation, channel => !ReferenceEquals(channel, excluded.Object));
        first.SetException(firstError);
        success.SetResult();
        Assert.False(result.IsCompleted);
        last.SetException(lastError);
        var error = await Assert.ThrowsAsync<ChannelGroupException>(() => result.WaitAsync(TimeSpan.FromSeconds(5)));
        var failures = error.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(2, error.Count);
        Assert.Same(firstError, failures[firstChannel.Object]);
        Assert.Same(lastError, failures[lastChannel.Object]);
        Assert.DoesNotContain(failures.Keys, channel => ReferenceEquals(channel, successful.Object));
        Assert.Contains(firstError, error.InnerExceptions);
        Assert.Contains(lastError, error.InnerExceptions);
        Assert.True(result.IsFaulted);
        Assert.DoesNotContain(excluded.Invocations, call => !call.Method.IsSpecialName);
    }

    [Fact]
    public async Task ChildCancellationIsReportedWithItsChannel()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var childTask = Task.FromCanceled(cancellation.Token);
        var child = Channel(childTask, "disconnect");
        var group = new DefaultChannelGroup();
        group.Add(child.Object);
        Task result = group.DisconnectAsync();
        var error = await Assert.ThrowsAsync<ChannelGroupException>(() => result);
        var failure = Assert.Single(error);
        Assert.Same(child.Object, failure.Key);
        var canceled = Assert.IsType<TaskCanceledException>(failure.Value);
        Assert.Same(childTask, canceled.Task);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.True(result.IsFaulted);
    }

    [Fact]
    public async Task NativeChildAggregateRetainsAllOfItsExceptions()
    {
        var first = new IOException("first");
        var second = new IOException("second");
        Task childTask = Task.WhenAll(Task.FromException(first), Task.FromException(second));
        var child = Channel(childTask, "disconnect");
        var group = new DefaultChannelGroup();
        group.Add(child.Object);
        var error = await Assert.ThrowsAsync<ChannelGroupException>(() => group.DisconnectAsync());
        var failure = Assert.Single(error);
        Assert.Equal(new Exception[] { first, second }, Assert.IsType<AggregateException>(failure.Value).InnerExceptions);
        Assert.Equal(new Exception[] { first, second }, error.Flatten().InnerExceptions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BroadcastRetainsIndependentBufferOrHolderViews(bool holder, bool flush)
    {
        var group = new DefaultChannelGroup();
        var first = new EmbeddedChannel();
        var second = new EmbeddedChannel();
        var excluded = new EmbeddedChannel();
        var server = new Mock<IServerChannel>();
        server.SetupGet(channel => channel.Id).Returns(DefaultChannelId.NewInstance());
        server.SetupGet(channel => channel.CloseCompletion).Returns(new TaskCompletionSource().Task);
        foreach (IChannel channel in new IChannel[] { first, second, excluded, server.Object }) group.Add(channel);
        ByteBuf content = Unpooled.WrappedBuffer(new byte[] { 9, 1, 2 });
        content.ReaderIndex = 1;
        object message = holder ? new DefaultByteBufHolder(content) : content;
        try
        {
            Func<IChannel, bool> matcher = channel => !ReferenceEquals(channel, excluded);
            Task write = flush ? group.WriteAndFlushAsync(message, matcher) : group.WriteAsync(message, matcher);
            if (!flush)
            {
                Assert.False(write.IsCompleted);
                Assert.Null(first.ReadOutbound());
                Assert.Same(group, group.Flush(matcher));
            }
            await write;
            object firstMessage = first.ReadOutbound(), secondMessage = second.ReadOutbound();
            try
            {
                Assert.NotSame(message, firstMessage);
                Assert.NotSame(firstMessage, secondMessage);
                ByteBuf firstView = holder ? Assert.IsType<DefaultByteBufHolder>(firstMessage).Content : Assert.IsAssignableFrom<ByteBuf>(firstMessage);
                ByteBuf secondView = holder ? Assert.IsType<DefaultByteBufHolder>(secondMessage).Content : Assert.IsAssignableFrom<ByteBuf>(secondMessage);
                Assert.Equal(1, firstView.ReadByte());
                Assert.Equal(1, secondView.ReaderIndex);
                Assert.Equal(1, content.ReaderIndex);
                Assert.Equal(2, content.ReferenceCount);
                Assert.Null(excluded.ReadOutbound());
            }
            finally { ReferenceCountUtil.Release(firstMessage); ReferenceCountUtil.Release(secondMessage); }
            Assert.Equal(0, content.ReferenceCount);
            server.Verify(channel => channel.WriteAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
            server.Verify(channel => channel.WriteAndFlushAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
            server.Verify(channel => channel.Flush(), Times.Never);
        }
        finally { first.FinishAndReleaseAll(); second.FinishAndReleaseAll(); excluded.FinishAndReleaseAll(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoRecipientsStillReleasesTheOriginalMessage(bool empty)
    {
        var group = new DefaultChannelGroup();
        var channel = new EmbeddedChannel();
        if (!empty) group.Add(channel);
        var buffer = Unpooled.Buffer(1).WriteByte(1);
        try
        {
            Task write = group.WriteAndFlushAsync(buffer, _ => false);
            Assert.True(write.IsCompletedSuccessfully);
            await write;
            Assert.Equal(0, buffer.ReferenceCount);
            Assert.Null(channel.ReadOutbound());
        }
        finally { channel.FinishAndReleaseAll(); }
    }

    [Theory]
    [InlineData("close")]
    [InlineData("disconnect")]
    [InlineData("deregister")]
    public async Task LifecycleOperationsVisitServersBeforeChildren(string operation)
    {
        var order = new List<IChannel>();
        var child = Channel(Task.CompletedTask, operation);
        var server = new Mock<IServerChannel>();
        server.SetupGet(channel => channel.Id).Returns(DefaultChannelId.NewInstance());
        server.SetupGet(channel => channel.CloseCompletion).Returns(new TaskCompletionSource().Task);
        var group = new DefaultChannelGroup();
        group.Add(child.Object);
        group.Add(server.Object);
        switch (operation)
        {
            case "close": server.Setup(channel => channel.CloseAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add(server.Object)).Returns(Task.CompletedTask); break;
            case "disconnect": server.Setup(channel => channel.DisconnectAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add(server.Object)).Returns(Task.CompletedTask); break;
            case "deregister": server.Setup(channel => channel.DeregisterAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add(server.Object)).Returns(Task.CompletedTask); break;
        }
        Assert.Same(server.Object, group.First());
        await Operate(group, operation, channel => { if (ReferenceEquals(channel, child.Object)) order.Add(channel); return true; });
        Assert.Equal(2, order.Count);
        Assert.Same(server.Object, order[0]);
        Assert.Same(child.Object, order[1]);
    }

    [Fact]
    public async Task CloseSnapshotKeepsRemovedMembersAndExcludesLaterMembers()
    {
        var group = new DefaultChannelGroup();
        var first = new EmbeddedChannel();
        var removed = new EmbeddedChannel();
        var later = new EmbeddedChannel();
        try
        {
            group.Add(first);
            group.Add(removed);
            Task snapshot = group.NewCloseFuture();
            Assert.True(group.Remove(removed));
            group.Add(later);
            await first.CloseAsync();
            Assert.False(snapshot.IsCompleted);
            await removed.CloseAsync();
            await snapshot.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(later.IsOpen);
        }
        finally { first.FinishAndReleaseAll(); removed.FinishAndReleaseAll(); later.FinishAndReleaseAll(); }
    }

    [Fact]
    public async Task StayClosedAppliesEvenWhenAddingAnExistingMember()
    {
        var member = Channel(Task.CompletedTask, "close");
        var group = new DefaultChannelGroup(stayClosed: true);
        Assert.True(group.Add(member.Object));
        await group.CloseAsync(_ => false);
        Assert.False(group.Add(member.Object));
        member.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StayClosedCoversChannelsAddedDuringClose()
    {
        var group = new DefaultChannelGroup(stayClosed: true);
        var late = Channel(Task.CompletedTask, "close");
        var existing = Channel(Task.CompletedTask, "close");
        existing.Setup(channel => channel.CloseAsync(It.IsAny<CancellationToken>()))
            .Callback(() => group.Add(late.Object)).Returns(Task.CompletedTask);
        group.Add(existing.Object);
        await group.CloseAsync();
        late.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public void FailureCollectionIsValidatedAndCopied()
    {
        var child = new EmbeddedChannel();
        try
        {
            var cause = new IOException();
            var input = new List<KeyValuePair<IChannel, Exception>> { new(child, cause) };
            var failure = new ChannelGroupException(input);
            input.Clear();
            Assert.Same(child, Assert.Single(failure).Key);
            Assert.Same(cause, Assert.Single(failure.InnerExceptions));
            Assert.Throws<ArgumentException>(() => new ChannelGroupException(input));
            Assert.Throws<ArgumentNullException>(() => new ChannelGroupException(null));
        }
        finally { child.FinishAndReleaseAll(); }
    }

    [Theory]
    [InlineData("write")]
    [InlineData("writeAndFlush")]
    [InlineData("disconnect")]
    [InlineData("deregister")]
    [InlineData("close")]
    [InlineData("closeFuture")]
    public async Task NativeTaskSnapshotSupportsLookupIterationAndLivePartialCompletion(string operation)
    {
        var success = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Channel(success.Task, operation);
        var second = Channel(failure.Task, operation);
        var third = Channel(canceled.Task, operation);
        var group = new DefaultChannelGroup();
        foreach (var child in new[] { first, second, third }) group.Add(child.Object);
        var (completion, operations) = OperateWithTasks(group, operation);
        group.Clear();
        var later = Channel(Task.CompletedTask, operation);
        group.Add(later.Object);
        Assert.Equal(3, operations.Count);
        Assert.False(operations.TryGetValue(later.Object, out _));
        Assert.All(operations.Values, task => Assert.False(task.IsCompleted));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<IChannel, Task>)operations).Clear());
        success.SetResult();
        await operations[first.Object].WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, operations.Values.Count(task => task.IsCompletedSuccessfully));
        Assert.False(completion.IsCompleted);
        var cause = new IOException("second");
        failure.SetException(cause);
        Assert.Same(cause, await Record.ExceptionAsync(() => operations[second.Object]));
        Assert.Equal(1, operations.Values.Count(task => task.IsFaulted));
        Assert.False(completion.IsCompleted);
        canceled.SetCanceled();
        var error = await Assert.ThrowsAsync<ChannelGroupException>(() => completion);
        Assert.Equal(2, error.Count);
        Assert.Equal(1, operations.Values.Count(task => task.IsCanceled));
        Assert.True(operations[first.Object].IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CancelingAGroupObserverDoesNotCancelTheChannelOperations()
    {
        var producer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = Channel(producer.Task, "disconnect");
        var group = new DefaultChannelGroup();
        group.Add(child.Object);
        Task completion = group.DisconnectAsync(out var operations);
        using var observer = new CancellationTokenSource();
        Task observation = completion.WaitAsync(observer.Token);
        observer.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observation);
        Assert.False(operations[child.Object].IsCompleted);
        Assert.False(completion.IsCompleted);
        producer.SetResult();
        await completion;
    }

    [Fact]
    public async Task RemovingByIdKeepsTheExistingCloseSnapshotAndCannotRemoveAReplacement()
    {
        var close = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = Channel(close.Task, "closeFuture");
        var replacement = Channel(new TaskCompletionSource().Task, "closeFuture");
        replacement.SetupGet(value => value.Id).Returns(original.Object.Id);
        var group = new DefaultChannelGroup();
        group.Add(original.Object);
        Task snapshot = group.NewCloseFuture(out var operations);
        Assert.False(group.Remove(null));
        Assert.True(group.RemoveById(original.Object.Id));
        Assert.False(group.RemoveById(original.Object.Id));
        Assert.True(group.Add(replacement.Object));
        close.SetResult();
        await snapshot;
        Assert.Same(close.Task, operations[original.Object]);
        Assert.Same(replacement.Object, group.Find(original.Object.Id));
        original.Verify(value => value.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (Task, IReadOnlyDictionary<IChannel, Task>) OperateWithTasks(DefaultChannelGroup group, string kind)
    {
        IReadOnlyDictionary<IChannel, Task> operations;
        Task completion = kind switch
        {
            "write" => group.WriteAsync(new object(), out operations),
            "writeAndFlush" => group.WriteAndFlushAsync(new object(), out operations),
            "close" => group.CloseAsync(out operations),
            "disconnect" => group.DisconnectAsync(out operations),
            "deregister" => group.DeregisterAsync(out operations),
            "closeFuture" => group.NewCloseFuture(out operations),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return (completion, operations);
    }

    private static Mock<IChannel> Channel(Task operation, string kind)
    {
        var channel = new Mock<IChannel>();
        channel.SetupGet(value => value.Id).Returns(DefaultChannelId.NewInstance());
        channel.SetupGet(value => value.CloseCompletion).Returns(kind == "closeFuture" ? operation : new TaskCompletionSource().Task);
        channel.Setup(value => value.WriteAsync(It.IsAny<object>(), It.IsAny<CancellationToken>())).Returns(operation);
        channel.Setup(value => value.WriteAndFlushAsync(It.IsAny<object>(), It.IsAny<CancellationToken>())).Returns(operation);
        channel.Setup(value => value.CloseAsync(It.IsAny<CancellationToken>())).Returns(operation);
        channel.Setup(value => value.DisconnectAsync(It.IsAny<CancellationToken>())).Returns(operation);
        channel.Setup(value => value.DeregisterAsync(It.IsAny<CancellationToken>())).Returns(operation);
        return channel;
    }

    private static Task Operate(DefaultChannelGroup group, string kind, Func<IChannel, bool> matcher) => kind switch
    {
        "write" => group.WriteAsync(new object(), matcher),
        "writeAndFlush" => group.WriteAndFlushAsync(new object(), matcher),
        "close" => group.CloseAsync(matcher),
        "disconnect" => group.DisconnectAsync(matcher),
        "deregister" => group.DeregisterAsync(matcher),
        "closeFuture" => group.NewCloseFuture(matcher),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
#pragma warning restore xUnit1051
