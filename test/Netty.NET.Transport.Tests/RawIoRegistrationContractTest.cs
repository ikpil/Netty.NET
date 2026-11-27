using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Tests;

// CLR supplemental: raw INioIoHandle registrations on the select()-based NioIoHandler.
#pragma warning disable xUnit1051 // Independent teardown.
public class RawIoRegistrationContractTest
{
    [Fact(Timeout = 5000)]
    public void ManualLoopCountsTwoReadyDatagramsAsOneHandleBeforeTasks()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread, NioIoHandler.NewFactory());
        using var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        receiver.Blocking = false;
        var handle = new RecordingHandle(loop, receiver);
        try
        {
            IIoRegistration registration = loop.RegisterAsync(handle).GetAwaiter().GetResult();
            Assert.Equal(NioIoOps.READ.Value(), registration.Submit(NioIoOps.READ));
            Assert.Equal(NioIoOps.READ, registration.Attachment<NioIoOps>());
            sender.SendTo(new byte[] { 11 }, receiver.LocalEndPoint);
            sender.SendTo(new byte[] { 22 }, receiver.LocalEndPoint);
            SpinWait.SpinUntil(() => receiver.Available >= 2, TimeSpan.FromSeconds(2));

            bool ordinary = false;
            loop.Execute(() => ordinary = true);
            Assert.Equal(1, loop.RunNow(-1));
            Assert.False(ordinary);
            Assert.Equal(new[] { 11, 22 }, handle.Payloads);
            Assert.Equal(1, loop.RunNonBlockingTasks(0));
            Assert.True(ordinary);
        }
        finally
        {
            Terminate(loop);
        }
        Assert.Equal(new[] { "registered", "read", "unregistered", "closed" }, handle.Events);
    }

    [Fact(Timeout = 5000)]
    public void CancellationUnregistersOnTheOwnerAndAllowsANewRegistration()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread, NioIoHandler.NewFactory());
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Blocking = false;
        var handle = new RecordingHandle(loop, socket);
        try
        {
            IIoRegistration registration = loop.RegisterAsync(handle).GetAwaiter().GetResult();
            Assert.True(registration.IsValid());
            Assert.True(registration.Cancel());
            Assert.False(registration.IsValid());
            Assert.False(registration.Cancel());
            Assert.Equal(-1, registration.Submit(NioIoOps.READ));

            IIoRegistration next = loop.RegisterAsync(handle).GetAwaiter().GetResult();
            Assert.True(next.Cancel());
        }
        finally
        {
            Terminate(loop);
        }
        Assert.Equal(new[] { "registered", "unregistered", "registered", "unregistered" }, handle.Events);
    }

    [Fact(Timeout = 5000)]
    public async Task ThreadedRegistrationRejectsDuplicatesAndClosesRemainingHandlesOnShutdown()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var loop = Assert.IsType<SingleThreadIoEventLoop>(group.Next());
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Blocking = false;
        var handle = new RecordingHandle(loop, socket);
        try
        {
            Assert.Same(loop, ((IIoEventLoopGroup)loop).Next());
            IIoRegistration registration = await group.RegisterAsync(handle);
            await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RegisterAsync(handle));
            await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
            Assert.False(registration.IsValid());
            Assert.Throws<RejectedExecutionException>(() =>
            {
                _ = loop.RegisterAsync(handle);
            });
            Assert.Equal(new[] { "registered", "unregistered", "closed" }, handle.Events);
        }
        finally
        {
            await group.StopAsync();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task BlockingSocketsAreRejectedAsIllegalBlockingMode()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => group.RegisterAsync(new RecordingHandle(group.Next(), socket)));
        }
        finally
        {
            await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }

    [Fact(Timeout = 5000)]
    public async Task ShutdownContinuesAfterAHandleFailsToClose()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var loop = group.Next();
        using var first = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        using var second = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        var failing = new RecordingHandle(loop, first) { FailClose = true };
        var healthy = new RecordingHandle(loop, second);
        try
        {
            IIoRegistration one = await loop.RegisterAsync(failing);
            IIoRegistration two = await loop.RegisterAsync(healthy);
            await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
            Assert.False(one.IsValid());
            Assert.False(two.IsValid());
            Assert.Equal(new[] { "registered", "unregistered", "closed" }, failing.Events);
            Assert.Equal(new[] { "registered", "unregistered", "closed" }, healthy.Events);
        }
        finally
        {
            await group.StopAsync();
        }
    }

    private static void Terminate(ManualIoEventLoop loop)
    {
        loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);

        while (!loop.IsTerminated())
        {
            loop.RunNow();
        }
    }

    private sealed class RecordingHandle(IEventExecutor owner, Socket socket) : INioIoHandle
    {
        internal readonly List<string> Events = new();
        internal readonly List<int> Payloads = new();
        internal bool FailClose;

        public Socket SelectableChannel()
        {
            return socket;
        }

        public void Registered()
        {
            Assert.True(owner.InEventLoop());
            Events.Add("registered");
        }

        public void Handle(IIoRegistration registration, IIoEvent ioEvent)
        {
            Assert.True(owner.InEventLoop());
            Assert.True(registration.IsValid());
            Assert.True(NioIoOps.READ.Contains(((INioIoEvent)ioEvent).Ops()));
            byte[] buffer = new byte[1];
            while (socket.Receive(buffer, 0, 1, SocketFlags.None, out SocketError error) == 1 && error == SocketError.Success)
                Payloads.Add(buffer[0]);
            Events.Add("read");
        }

        public void Unregistered()
        {
            Assert.True(owner.InEventLoop());
            Events.Add("unregistered");
        }

        public void Close()
        {
            Assert.True(owner.InEventLoop());
            Events.Add("closed");
            if (FailClose)
            {
                throw new InvalidOperationException("Close failed.");
            }
        }
    }
}
