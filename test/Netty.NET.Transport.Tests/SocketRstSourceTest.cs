/*
 * Copyright 2016 The Netty Project
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
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The Runner body observes TestContext.Current.CancellationToken in its latch waits.
#pragma warning disable xUnit1051 // The source closes the accepted channel without waiting, independently of cancellation.
public class SocketRstSourceTest : AbstractSocketTest
{
    // CLR: the source's IOException parameter is SocketException. java.net.SocketException ("Connection reset") is an
    // IOException, while System.Net.Sockets.SocketException derives from Win32Exception, not IOException
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketexception), so the CLR reset is a
    // SocketException whose SocketErrorCode is ConnectionReset, "The connection was reset by the remote peer"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketerror). That code is asserted because the
    // source's message check below only runs for the US/UK locales and CLR socket messages follow the OS language.
    // Locale.getDefault() is CultureInfo.CurrentCulture, and Locale.US/Locale.UK are the en-US/en-GB cultures.
    protected virtual void AssertRstOnCloseException(SocketException cause, IChannel clientChannel)
    {
        Assert.Equal(SocketError.ConnectionReset, cause.SocketErrorCode);

        if (CultureInfo.CurrentCulture.Name == "en-US" || CultureInfo.CurrentCulture.Name == "en-GB")
        {
            Assert.True(cause.Message.Contains("reset") || cause.Message.Contains("closed"),
                "actual message: " + cause.Message);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestSoLingerZeroCausesOnlyRstOnClose()
    {
        await Run(TestContext.Current, TestSoLingerZeroCausesOnlyRstOnClose0);
    }

    // CLR: xUnit1024 forbids overloading the TestSoLingerZeroCausesOnlyRstOnClose test name.
    private async Task TestSoLingerZeroCausesOnlyRstOnClose0(ServerBootstrap sb, Bootstrap cb)
    {
        var cancellation = TestContext.Current.CancellationToken;
        // CLR: AtomicReference is a StrongBox ("Holds a reference to a value",
        // https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.strongbox-1); compareAndSet(null, x)
        // is Interlocked.CompareExchange and get() is Volatile.Read. Each CountDownLatch(1) is a TaskCompletionSource,
        // "the producer side of a Task unbound to a delegate"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource): countDown() is
        // TrySetResult() and await() awaits its Task.
        StrongBox<IChannel> serverChannelRef = new StrongBox<IChannel>();
        StrongBox<Exception> throwableRef = new StrongBox<Exception>();
        TaskCompletionSource latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource latch2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // SO_LINGER=0 means that we must send ONLY a RST when closing (not a FIN + RST).
        sb.ChildOption(ChannelOption.SO_LINGER, 0);
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
        {
            Interlocked.CompareExchange(ref serverChannelRef.Value, ch, null);
            latch.TrySetResult();
        }));
        cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
        {
            ch.Pipeline.AddLast(new ClientHandler(throwableRef, latch2));
        }));
        IChannel sc = await sb.BindAsync();
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);

        // Wait for the server to get setup.
        await latch.Task.WaitAsync(cancellation);

        // The server has SO_LINGER=0 and so it must send a RST when close is called.
        _ = Volatile.Read(ref serverChannelRef.Value).CloseAsync();

        // Wait for the client to get channelInactive.
        await latch2.Task.WaitAsync(cancellation);

        // Verify the client received a RST.
        Exception cause = Volatile.Read(ref throwableRef.Value);
        // CLR: assertInstanceOf(IOException.class, cause, ...) is Assert.IsAssignableFrom, which "Verifies that an
        // object is of the given type or a derived type" (xunit.v3.assert 4.0.2) and reports the actual type.
        SocketException socketCause = Assert.IsAssignableFrom<SocketException>(cause);

        AssertRstOnCloseException(socketCause, cc);
    }

    [Fact(Timeout = 30000)]
    public async Task TestNoRstIfSoLingerOnClose()
    {
        await Run(TestContext.Current, TestNoRstIfSoLingerOnClose0);
    }

    // CLR: xUnit1024 forbids overloading the TestNoRstIfSoLingerOnClose test name.
    private async Task TestNoRstIfSoLingerOnClose0(ServerBootstrap sb, Bootstrap cb)
    {
        var cancellation = TestContext.Current.CancellationToken;
        StrongBox<IChannel> serverChannelRef = new StrongBox<IChannel>();
        StrongBox<Exception> throwableRef = new StrongBox<Exception>();
        TaskCompletionSource latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource latch2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
        {
            Interlocked.CompareExchange(ref serverChannelRef.Value, ch, null);
            latch.TrySetResult();
        }));
        cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
        {
            ch.Pipeline.AddLast(new ClientHandler(throwableRef, latch2));
        }));
        IChannel sc = await sb.BindAsync();
        await cb.ConnectAsync(sc.LocalAddress);

        // Wait for the server to get setup.
        await latch.Task.WaitAsync(cancellation);

        // The server has SO_LINGER=0 and so it must send a RST when close is called.
        // CLR: the source repeats this comment although this scenario keeps the default SO_LINGER; no option is added.
        _ = Volatile.Read(ref serverChannelRef.Value).CloseAsync();

        // Wait for the client to get channelInactive.
        await latch2.Task.WaitAsync(cancellation);

        // Verify the client did not received a RST.
        Assert.Null(Volatile.Read(ref throwableRef.Value));
    }

    // CLR: one named class carries the bodies of the source's two identical anonymous handlers; C# anonymous types
    // "can't add methods, events, or custom operators"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/anonymous-types).
    private sealed class ClientHandler(StrongBox<Exception> throwableRef, TaskCompletionSource latch2)
            : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            Interlocked.CompareExchange(ref throwableRef.Value, cause, null);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            latch2.TrySetResult();
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
