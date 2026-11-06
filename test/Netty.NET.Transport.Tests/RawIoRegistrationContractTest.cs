using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.ManualIoEventLoopSourceTest;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1031 // Manual I/O must be pumped by the original owner.
#pragma warning disable xUnit1051 // Independent cleanup.
public class RawIoRegistrationContractTest
{
    [Fact(Timeout = 5000)]
    public void ManualLoopCountsTwoSocketCompletionsAsOneHandleBeforeTasks()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var token = TestContext.Current.CancellationToken;
        using var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var handle = new RecordingHandle(loop);
        try
        {
            receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var registration = loop.RegisterIoAsync(handle, token).GetAwaiter().GetResult();
            var publication = Task.Run(async () =>
            {
                byte[] buffer = new byte[1];
                var completions = new Task<bool>[2];
                for (int i = 0; i < completions.Length; i++)
                {
                    Assert.Equal(1, await receiver.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, token));
                    completions[i] = registration.PublishAsync((int)buffer[0]);
                }
                return completions;
            }, token);
            sender.SendTo(new byte[] { 11 }, receiver.LocalEndPoint);
            sender.SendTo(new byte[] { 22 }, receiver.LocalEndPoint);
            var published = publication.WaitAsync(token).GetAwaiter().GetResult();

            bool ordinary = false;
            loop.Execute(() => ordinary = true);
            Assert.Equal(1, loop.RunNow(-1));
            Assert.False(ordinary);
            Assert.All(published, task => Assert.True(task.GetAwaiter().GetResult()));
            Assert.Equal(new[] { 11, 22 }, handle.Payloads);
            Assert.Equal(1, loop.RunNonBlockingTasks(0));
            Assert.True(ordinary);
        }
        finally { Terminate(loop); }
        Assert.Equal(new[] { "registered", "read", "read", "disposed", "unregistered" }, handle.Events);
    }

    [Fact(Timeout = 5000)]
    public void CancellationInvalidatesQueuedCompletionsAndUnregistersOnTheOwner()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var token = TestContext.Current.CancellationToken;
        var handle = new RecordingHandle(loop);
        try
        {
            var registration = loop.RegisterIoAsync(handle, token).GetAwaiter().GetResult();
            var completion = registration.PublishAsync(11);
            var cancelled = Task.Factory.StartNew(() => registration.CancelAsync(), token, TaskCreationOptions.None,
                TaskScheduler.Default).WaitAsync(token).GetAwaiter().GetResult();
            Assert.False(registration.IsValid);
            Assert.False(cancelled.IsCompleted);
            Assert.Equal(0, loop.RunNow(-1));
            Assert.False(completion.GetAwaiter().GetResult());
            Assert.True(cancelled.GetAwaiter().GetResult());
            Assert.False(registration.CancelAsync().GetAwaiter().GetResult());

            var next = loop.RegisterIoAsync(handle, token).GetAwaiter().GetResult();
            Assert.True(next.CancelAsync().GetAwaiter().GetResult());
        }
        finally { Terminate(loop); }
        Assert.Equal(new[] { "registered", "unregistered", "registered", "unregistered" }, handle.Events);
    }

    [Fact(Timeout = 5000)]
    public async Task ThreadedRegistrationRejectsDuplicatesAndClosesRemainingHandlesOnShutdown()
    {
        var group = new DefaultEventLoopGroup(1);
        var loop = Assert.IsType<DefaultEventLoop>(group.Next());
        var token = TestContext.Current.CancellationToken;
        var handle = new RecordingHandle(loop);
        try
        {
            Assert.Same(loop, ((IIoEventLoopGroup)loop).Next());
            var registration = await group.RegisterIoAsync(handle, token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RegisterIoAsync(handle, token));
            Assert.Equal(1, loop.RegisteredIoHandles);
            Assert.True(await registration.PublishAsync(42).WaitAsync(token));
            await group.StopAsync().WaitAsync(token);
            Assert.False(registration.IsValid);
            Assert.False(await registration.PublishAsync(43));
            Assert.Equal(0, loop.RegisteredIoHandles);
            await Assert.ThrowsAsync<RejectedExecutionException>(() => loop.RegisterIoAsync(handle, token));
            Assert.Equal(new[] { "registered", "read", "disposed", "unregistered" }, handle.Events);
        }
        finally { await group.StopAsync(); }
    }

    [Fact(Timeout = 5000)]
    public async Task RegistrationFailureDoesNotLeakTheHandleSlot()
    {
        var loop = new DefaultEventLoop();
        var token = TestContext.Current.CancellationToken;
        var handle = new RecordingHandle(loop) { FailRegistration = true };
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RegisterIoAsync(handle, token));
            Assert.Equal(0, loop.RegisteredIoHandles);
            handle.FailRegistration = false;
            Assert.True((await loop.RegisterIoAsync(handle, token)).IsValid);
        }
        finally { await loop.StopAsync(); }
        Assert.Equal(new[] { "registered", "disposed", "unregistered" }, handle.Events);
    }

    [Fact(Timeout = 5000)]
    public async Task ShutdownContinuesAfterAHandleFailsToDispose()
    {
        var loop = new DefaultEventLoop();
        var token = TestContext.Current.CancellationToken;
        var failing = new RecordingHandle(loop) { FailDispose = true };
        var healthy = new RecordingHandle(loop);
        try
        {
            var first = await loop.RegisterIoAsync(failing, token);
            var second = await loop.RegisterIoAsync(healthy, token);
            await loop.StopAsync().WaitAsync(token);
            Assert.False(first.IsValid);
            Assert.False(second.IsValid);
            Assert.Equal(0, loop.RegisteredIoHandles);
            Assert.Equal(new[] { "registered", "disposed", "unregistered" }, failing.Events);
            Assert.Equal(new[] { "registered", "disposed", "unregistered" }, healthy.Events);
        }
        finally { await loop.StopAsync(); }
    }

    private sealed class RecordingHandle(IEventLoop owner) : IIoHandle
    {
        internal readonly List<string> Events = new();
        internal readonly List<int> Payloads = new();
        internal bool FailRegistration;
        internal bool FailDispose;

        public void Registered(IoRegistration registration)
        {
            Assert.True(owner.InEventLoop());
            Assert.Same(owner, registration.EventLoop);
            Assert.True(registration.IsValid);
            if (FailRegistration) throw new InvalidOperationException("Registration callback failed.");
            Events.Add("registered");
        }

        public void HandleCompletion(IoRegistration registration, object completion)
        {
            Assert.True(owner.InEventLoop());
            Assert.True(registration.IsValid);
            Payloads.Add(Assert.IsType<int>(completion));
            Events.Add("read");
        }

        public void Unregistered()
        {
            Assert.True(owner.InEventLoop());
            Events.Add("unregistered");
        }

        public void Dispose()
        {
            Assert.True(owner.InEventLoop());
            Events.Add("disposed");
            if (FailDispose) throw new InvalidOperationException("Disposal failed.");
        }
    }
}
