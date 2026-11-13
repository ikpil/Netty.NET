/*
 * Copyright 2017 The Netty Project
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
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

// Supplemental CLR Task/TCS contracts. Java constructor/varargs-listener assertions are reviewed separately,
// not replaced by these tests or by new Promise/Future wrappers.
#pragma warning disable xUnit1031 // Native queue completion is driven on the manual event-loop owner.
public class ChannelPromiseTaskContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationAfterFlushDoesNotClaimTheNativeWriteResult(bool fail)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var buf = Unpooled.Buffer(1).WriteByte(1);
        Task future = fixture.Channel.WriteAndFlushAsync(buf, cancellation.Token);
        Assert.False(future.IsCompleted);
        Assert.Equal(2, buf.ReferenceCount);
        cancellation.Cancel();
        Assert.False(future.IsCompleted);
        var e = new IOException("Native write failed.");
        if (fail) fixture.Write.SetException(e);
        else fixture.Write.SetResult();
        fixture.PumpUntil(() => future.IsCompleted);
        Assert.False(future.IsCanceled);
        if (fail) Assert.Same(e, Assert.Throws<IOException>(() => future.GetAwaiter().GetResult()));
        else Assert.True(future.IsCompletedSuccessfully);
        Assert.Equal(0, buf.ReferenceCount);
        Assert.True(fixture.Channel.IsOpen);
    }

    [Fact]
    public void CancelingAnAwaitDoesNotCancelTheQueuedWrite()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var buf = Unpooled.Buffer(1).WriteByte(1);
        Task future = fixture.Channel.WriteAndFlushAsync(buf, TestContext.Current.CancellationToken);
        Task waiting = future.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => waiting.GetAwaiter().GetResult());
        Assert.False(future.IsCompleted);
        Assert.Equal(2, buf.ReferenceCount);
        fixture.Write.SetResult();
        fixture.PumpUntil(() => future.IsCompleted);
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Equal(0, buf.ReferenceCount);
    }

    [Fact]
    public void ConsumerContinuationObservesReleaseWithoutReenteringTheOwner()
    {
        using var fixture = new Fixture();
        var buf = Unpooled.Buffer(1).WriteByte(1);
        Task future = fixture.Channel.WriteAndFlushAsync(buf, TestContext.Current.CancellationToken);
        Task<(bool, int)> consumer = future.ContinueWith(_ => (fixture.Loop.InEventLoop(), buf.ReferenceCount),
            TestContext.Current.CancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        fixture.Write.SetResult();
        fixture.PumpUntil(() => consumer.IsCompleted);
        var result = consumer.GetAwaiter().GetResult();
        Assert.False(result.Item1);
        Assert.Equal(0, result.Item2);
        Assert.True(future.IsCompletedSuccessfully);
    }

    [Fact]
    public void AFailedWriteDoesNotBecomeTheChannelsCloseResult()
    {
        using var fixture = new Fixture();
        var buf = Unpooled.Buffer(1).WriteByte(1);
        Task future = fixture.Channel.WriteAndFlushAsync(buf, TestContext.Current.CancellationToken);
        var e = new IOException("Native write failed.");
        fixture.Write.SetException(e);
        fixture.PumpUntil(() => future.IsCompleted);
        Assert.Same(e, Assert.Throws<IOException>(() => future.GetAwaiter().GetResult()));
        Assert.False(fixture.Channel.CloseCompletion.IsCompleted);
        Task closeFuture = fixture.Channel.CloseAsync(TestContext.Current.CancellationToken);
        fixture.PumpUntil(() => closeFuture.IsCompleted);
        Assert.True(closeFuture.IsCompletedSuccessfully);
        Assert.True(fixture.Channel.CloseCompletion.IsCompletedSuccessfully);
        Assert.Equal(0, buf.ReferenceCount);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly TaskCompletionSource Write = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualIoEventLoop Loop = new(Thread.CurrentThread);
        internal readonly TaskTestChannel Channel;
        internal Fixture()
        {
            Channel = new TaskTestChannel(_ => Write.Task);
            Loop.RegisterAsync(Channel, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        }
        internal void PumpUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "The queued Task did not complete on its owner.");
                Loop.RunNow();
                Thread.Yield();
            }
        }
        public void Dispose()
        {
            Write.TrySetResult();
            ManualIoEventLoopSourceTest.Terminate(Loop);
        }
    }
}

// In-memory transport producer used to exercise the real pipeline, outbound queue and Task completion path.
// These overrides implement existing CLR transport hooks, not additional Java Future APIs.
internal sealed class TaskTestChannel(Func<object, Task> write) : AbstractChannel(null, new ChannelMetadata(false))
{
    public override EndPoint LocalAddress => null;
    public override EndPoint RemoteAddress => null;
    protected override void DoRegister() => SetActive(true);
    protected override bool ContinueOnWriteError => true;
    protected override Task DoWrite(object msg) => write(msg);
    protected override Task DoBindAsync(EndPoint localAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
    protected override Task DoConnectAsync(EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
    protected override void DoClose() { }
    protected override void DoBeginRead() { }
}
