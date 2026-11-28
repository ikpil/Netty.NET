/*
 * Copyright 2019 The Netty Project
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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The Runner body observes TestContext.Current.CancellationToken in its blocking waits.
#pragma warning disable xUnit1051 // Peer socket operations and closes run independently of the test cancellation.
// CLR: AbstractSocketShutdownOutputByPeerTest<Socket> and its concrete SocketShutdownOutputByPeerTest are this one
// class. The peer socket of both concrete sources (java.net.Socket and the epoll LinuxSocket) is
// System.Net.Sockets.Socket, so the type parameter has a single argument, and DomainSocketShutdownOutputByPeerSourceTest
// (the EpollDomainSocketShutdownOutputByPeerTest counterpart) selects the epoll overrides through the Domain hook.
public class SocketShutdownOutputByPeerSourceTest : AbstractServerSocketTest
{
    [Fact(Timeout = 30000)]
    public async Task TestShutdownOutput()
    {
        await Run(TestContext.Current, TestShutdownOutput0);
    }

    // CLR: xUnit1024 forbids overloading the TestShutdownOutput test name.
    private async Task TestShutdownOutput0(ServerBootstrap sb)
    {
        var cancellation = TestContext.Current.CancellationToken;
        TestHandler h = new TestHandler();
        Socket s = NewSocket();
        IChannel sc = null;

        try
        {
            sc = await sb.ChildHandler(h).ChildOption(ChannelOption.ALLOW_HALF_CLOSURE, true).BindAsync();

            await ConnectAsync(s, sc.LocalAddress);
            Write(s, 1);

            Assert.Equal(1, (int) await h.queue.Reader.ReadAsync(cancellation));

            Assert.True(h.ch.IsOpen);
            Assert.True(h.ch.IsActive);
            Assert.False(h.ch.IsInputShutdown);
            Assert.False(h.ch.IsOutputShutdown);

            ShutdownOutput(s);

            await h.halfClosure.Task.WaitAsync(cancellation);

            Assert.True(h.ch.IsOpen);
            Assert.True(h.ch.IsActive);
            Assert.True(h.ch.IsInputShutdown);
            Assert.False(h.ch.IsOutputShutdown);

            while (h.closure.Task.IsCompleted && Volatile.Read(ref h.halfClosureCount) != 1)
            {
                await Task.Delay(100, cancellation);
            }
        }
        finally
        {
            if (sc != null)
            {
                _ = sc.CloseAsync();
            }

            Close(s);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestShutdownOutputWithoutOption()
    {
        await Run(TestContext.Current, TestShutdownOutputWithoutOption0);
    }

    // CLR: xUnit1024 forbids overloading the TestShutdownOutputWithoutOption test name.
    private async Task TestShutdownOutputWithoutOption0(ServerBootstrap sb)
    {
        var cancellation = TestContext.Current.CancellationToken;
        TestHandler h = new TestHandler();
        Socket s = NewSocket();
        IChannel sc = null;

        try
        {
            sc = await sb.ChildHandler(h).BindAsync();

            await ConnectAsync(s, sc.LocalAddress);
            Write(s, 1);

            Assert.Equal(1, (int) await h.queue.Reader.ReadAsync(cancellation));

            Assert.True(h.ch.IsOpen);
            Assert.True(h.ch.IsActive);
            Assert.False(h.ch.IsInputShutdown);
            Assert.False(h.ch.IsOutputShutdown);

            ShutdownOutput(s);

            await h.closure.Task.WaitAsync(cancellation);

            Assert.False(h.ch.IsOpen);
            Assert.False(h.ch.IsActive);
            Assert.True(h.ch.IsInputShutdown);
            Assert.True(h.ch.IsOutputShutdown);

            while (h.halfClosure.Task.IsCompleted && Volatile.Read(ref h.halfClosureCount) != 0)
            {
                await Task.Delay(100, cancellation);
            }
        }
        finally
        {
            if (sc != null)
            {
                _ = sc.CloseAsync();
            }

            Close(s);
        }
    }

    // CLR: the DomainSocketShutdownOutputByPeerSourceTest subclass is the EpollDomainSocketShutdownOutputByPeerTest
    // counterpart; Java overrides newFactories()/newSocketAddress() and the five peer operations there, this port
    // selects the same overrides through the Domain hook.
    protected virtual bool Domain => false;

    protected override List<TestsuitePermutation.IBootstrapFactory<ServerBootstrap>> NewFactories()
    {
        if (!Domain)
        {
            return base.NewFactories();
        }

        DomainSocketSourceTest.RequireDomainSockets();

        return DomainSocketTestPermutation.INSTANCE.ServerDomainSocket();
    }

    protected override EndPoint NewSocketAddress()
    {
        return Domain ? DomainSocketTestPermutation.NewDomainSocketAddress() : base.NewSocketAddress();
    }

    // CLR: the source's abstract peer operations are virtual methods with the bodies of their overrides in the
    // concrete SocketShutdownOutputByPeerTest, whose source file carries this license header:
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
    // Each Domain branch is the EpollDomainSocketShutdownOutputByPeerTest (LinuxSocket) override.
    protected virtual void ShutdownOutput(Socket s)
    {
        // CLR: Socket.shutdownOutput() and LinuxSocket.shutdown(false, true) both shut down the send direction.
        s.Shutdown(SocketShutdown.Send);
    }

    protected virtual async Task ConnectAsync(Socket s, EndPoint address)
    {
        if (Domain)
        {
            await s.ConnectAsync(address);
            return;
        }

        // CLR: SocketUtils.connect(s, address, 10000) connects with a 10000 ms timeout; the CLR connect observes a
        // CancellationToken that a CancellationTokenSource cancels after 10000 ms instead.
        using CancellationTokenSource timeout = new CancellationTokenSource(10000);
        await s.ConnectAsync(address, timeout.Token);
    }

    protected virtual void Close(Socket s)
    {
        s.Close();
    }

    protected virtual void Write(Socket s, int data)
    {
        if (Domain)
        {
            // CLR: the epoll override puts data into a four-byte direct buffer in native order and sends it.
            // BitConverter.GetBytes(int) uses the machine byte order: "The order of bytes in the array returned by the
            // GetBytes method overloads ... depends on whether the computer architecture is little-endian or
            // big-endian" (https://learn.microsoft.com/en-us/dotnet/api/system.bitconverter).
            s.Send(BitConverter.GetBytes(data));
            return;
        }

        // CLR: OutputStream.write(int) writes the low-order eight bits of data as one byte.
        s.Send(new byte[] { (byte) data });
    }

    protected virtual Socket NewSocket()
    {
        if (Domain)
        {
            // CLR: LinuxSocket.newSocketDomain() is an AF_UNIX stream socket.
            return DomainSocketSourceTest.NewSocket();
        }

        // CLR: new java.net.Socket() is unconnected and takes its address family from the connect address. "If the
        // operating system supports IPv6, this constructor creates a dual-mode socket; otherwise, it creates an IPv4
        // socket" (Socket(SocketType, ProtocolType), https://learn.microsoft.com/en-us/dotnet/api/
        // system.net.sockets.socket.-ctor), so it connects to IPv4 and IPv6 addresses alike.
        return new Socket(SocketType.Stream, ProtocolType.Tcp);
    }

    private sealed class TestHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        internal volatile IDuplexChannel ch;
        // CLR: the LinkedBlockingQueue<Byte> is an unbounded System.Threading.Channels channel; take() is
        // ChannelReader<T>.ReadAsync, which "Asynchronously reads an item from the channel"
        // (https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).
        internal readonly System.Threading.Channels.Channel<byte> queue =
                System.Threading.Channels.Channel.CreateUnbounded<byte>();
        // CLR: each CountDownLatch(1) is a TaskCompletionSource, "the producer side of a Task unbound to a delegate"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource): countDown() is
        // TrySetResult(), await() awaits its Task and getCount() != 1 is Task.IsCompleted.
        internal readonly TaskCompletionSource halfClosure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource closure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // CLR: the AtomicInteger is an int field updated with Interlocked.Increment and read with Volatile.Read.
        internal int halfClosureCount;

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ch = (IDuplexChannel) ctx.Channel;
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            closure.TrySetResult();
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            queue.Writer.TryWrite(msg.ReadByte());
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is ChannelInputShutdownEvent)
            {
                Interlocked.Increment(ref halfClosureCount);
                halfClosure.TrySetResult();
            }
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
