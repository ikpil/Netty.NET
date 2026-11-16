/*
 * Copyright 2013 The Netty Project
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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.SocketTestPermutation;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, which has no other cancellable wait.
#pragma warning disable xUnit1051 // The source waits for and closes its channels independently of the test cancellation.
public class SocketConnectionAttemptSourceTest : AbstractClientSocketTest
{
    [Fact(Timeout = 30000)]
    public async Task TestConnectTimeout()
    {
        await Run(TestContext.Current, TestConnectTimeout0);
    }

    // CLR: xUnit1024 forbids overloading the TestConnectTimeout test name.
    private async Task TestConnectTimeout0(Bootstrap cb)
    {
        IChannel channel = null;
        HandlerCapturingChannel(cb, new TestHandler(), created => channel = created)
                .Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 2000);
        Task<IChannel> future = cb.ConnectAsync(BAD_HOST, BAD_PORT);
        try
        {
            Assert.True(await AwaitAsync(future, 3000));
        }
        finally
        {
            _ = channel?.CloseAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestConnectRefused()
    {
        await Run(TestContext.Current, TestConnectRefused0);
    }

    // CLR: xUnit1024 forbids overloading the TestConnectRefused test name.
    private async Task TestConnectRefused0(Bootstrap cb)
    {
        await TestConnectRefused0(cb, false);
    }

    [Fact(Timeout = 30000)]
    public async Task TestConnectRefusedHalfClosure()
    {
        await Run(TestContext.Current, TestConnectRefusedHalfClosure0);
    }

    // CLR: xUnit1024 forbids overloading the TestConnectRefusedHalfClosure test name.
    private async Task TestConnectRefusedHalfClosure0(Bootstrap cb)
    {
        await TestConnectRefused0(cb, true);
    }

    private static async Task TestConnectRefused0(Bootstrap cb, bool halfClosure)
    {
        // CLR: GlobalEventExecutor.INSTANCE.newPromise() is a TaskCompletionSource; this port completes Tasks instead
        // of exposing promises (IChannel.cs: "Tasks replace ChannelFuture/Promise").
        TaskCompletionSource errorPromise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannelHandler handler = new ChannelActiveFailureHandler(errorPromise);

        cb.Handler(handler);
        cb.Option(ChannelOption.ALLOW_HALF_CLOSURE, halfClosure);
        // CLR: awaitUninterruptibly() waits without rethrowing; awaiting Task.WhenAny does the same because its Task
        // "always ends in the RanToCompletion state"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.whenany).
        Task<IChannel> future = cb.ConnectAsync(NetUtil.LOCALHOST, UNASSIGNED_PORT);
        await Task.WhenAny(future);
        // CLR: java.net.ConnectException "Signals that an error occurred while attempting to connect a socket to a
        // remote address and port. Typically, the connection was refused remotely"
        // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/net/ConnectException.html). The CLR
        // socket reports it as a SocketException whose SocketErrorCode is ConnectionRefused, "The remote host is
        // actively refusing a connection" (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketerror).
        // Task.Exception wraps the cause in an AggregateException.
        SocketException cause = Assert.IsAssignableFrom<SocketException>(future.Exception?.InnerException);
        Assert.Equal(SocketError.ConnectionRefused, cause.SocketErrorCode);
        Assert.Null(errorPromise.Task.Exception);
    }

    [Fact]
    public async Task TestConnectCancellation()
    {
        // Check if the test can be executed or should be skipped because of no network/internet connection
        // See https://github.com/netty/netty/issues/1474
        bool badHostTimedOut = true;
        // CLR: new Socket() is the parameterless java.net.Socket; Socket(SocketType, ProtocolType) "creates a
        // dual-mode socket" when the OS supports IPv6 (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.-ctor),
        // so the family follows the target address as with the JDK socket.
        Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            // CLR: SocketUtils.socketAddress(host, port) is new InetSocketAddress(host, port), which resolves the
            // host to its first address, and SocketUtils.connect(socket, address, 10) is a connect bounded to 10 ms;
            // Dns.GetHostAddresses and a connect cancelled after 10 ms take their place (the cancellation surfaces as
            // an OperationCanceledException, where the JDK throws SocketTimeoutException, and both are ignored).
            using CancellationTokenSource connectTimeout = new CancellationTokenSource(10);
            await socket.ConnectAsync(new IPEndPoint(Dns.GetHostAddresses(BAD_HOST)[0], BAD_PORT), connectTimeout.Token);
        }
        // CLR: ConnectException maps to the SocketError codes the CLR socket reports for a refused connect and for no
        // route to the host (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketerror).
        catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionRefused
                or SocketError.HostUnreachable or SocketError.NetworkUnreachable)
        {
            badHostTimedOut = false;
            // is thrown for no route to host when using Socket connect
        }
        catch (Exception)
        {
            // ignore
        }
        finally
        {
            try
            {
                socket.Close();
            }
            catch (IOException)
            {
                // ignore
            }
        }

        // CLR: xUnit v3's Assert.SkipUnless "will skip the test if condition is false"
        // (https://xunit.net/docs/getting-started/v3/whats-new), as assumeTrue(...) aborts it.
        Assert.SkipUnless(badHostTimedOut, "The connection attempt to " + BAD_HOST + " does not time out.");

        await Run(TestContext.Current, TestConnectCancellation0);
    }

    // CLR: xUnit1024 forbids overloading the TestConnectCancellation test name.
    private async Task TestConnectCancellation0(Bootstrap cb)
    {
        IChannel channel = null;
        HandlerCapturingChannel(cb, new TestHandler(), created => channel = created)
                .Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 4000);
        // CLR: ChannelFuture.cancel(true) becomes cancelling the token passed to ConnectAsync. Task cancellation is
        // cooperative: a task that acknowledges the token "transitions to the Canceled state"
        // (https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-cancellation).
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        Task<IChannel> future = cb.ConnectAsync(BAD_HOST, BAD_PORT, cancellation.Token);
        try
        {
            if (await AwaitAsync(future, 1000))
            {
                if (future.IsCompletedSuccessfully)
                {
                    Assert.Fail("A connection attempt to " + BAD_HOST + " must not succeed.");
                }
                else
                {
                    // CLR: awaiting the failed Task rethrows future.cause().
                    await future;
                }
            }

            // CLR: CancellationTokenSource.Cancel() has no result. cancel(true) succeeds while the future is not done,
            // so its result is whether the connect Task was still incomplete when cancellation was requested. The
            // Task reports IsCanceled once the connect has acknowledged the token, so the Task is awaited (without
            // rethrowing) before isCancelled() is checked.
            bool cancelled = !future.IsCompleted;
            cancellation.Cancel();
            if (cancelled)
            {
                Assert.True(await AwaitAsync(channel.CloseFuture, 500));
                await Task.WhenAny(future);
                Assert.True(future.IsCanceled);
            }
            else
            {
                // Check if cancellation is supported or not.
                Assert.False(IsConnectCancellationSupported(channel), channel.GetType() +
                        " should support connect cancellation");
            }
        }
        finally
        {
            _ = channel?.CloseAsync();
        }
    }

    // CLR: OioSocketChannel is deprecated-not-ported and the harness adds no OIO permutation, so
    // !(channel instanceof OioSocketChannel) is always true.
    protected virtual bool IsConnectCancellationSupported(IChannel channel)
    {
        return true;
    }

    // CLR: Bootstrap.connect(...) returns a ChannelFuture whose channel() exists even when the connect fails or is
    // cancelled, while ConnectAsync's Task<IChannel> only yields the channel on success, and the harness has already
    // set the channel factory (Bootstrap.ChannelFactory refuses a second one, as in the source). The source's
    // cb.handler(handler) therefore becomes an ActionChannelInitializer that records the channel when it is registered,
    // before the connect starts, and adds the same handler, so the pipeline ends up as in the source.
    private static Bootstrap HandlerCapturingChannel(Bootstrap cb, IChannelHandler handler, Action<IChannel> created)
    {
        return cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
        {
            created(ch);
            ch.Pipeline.AddLast(handler);
        }));
    }

    // CLR: Future.await(long timeoutMillis) waits without rethrowing the cause and reports whether the future
    // completed in time. Task.WhenAny "always ends in the RanToCompletion state with its Result set to the first task
    // to complete" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.whenany), so racing the
    // Task against a delay gives the same answer; the delay is cancelled once the race is decided.
    private static async Task<bool> AwaitAsync(Task future, long timeoutMillis)
    {
        using CancellationTokenSource delayCancellation = new CancellationTokenSource();
        Task completed = await Task.WhenAny(future, Task.Delay(TimeSpan.FromMilliseconds(timeoutMillis), delayCancellation.Token));
        delayCancellation.Cancel();
        return completed == future;
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of testConnectRefused0.
    private sealed class ChannelActiveFailureHandler(TaskCompletionSource errorPromise) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            // CLR: FailException, which Assert.Fail throws, is xUnit's counterpart of java.lang.AssertionError;
            // SetException throws when already completed, as Promise.setFailure does.
            errorPromise.SetException(Xunit.Sdk.FailException.ForFailure("should have never been called"));
        }
    }

    private sealed class TestHandler : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            InternalLoggerFactory.GetInstance(
                    typeof(SocketConnectionAttemptSourceTest)).Warn("Unexpected exception:", cause);
        }
    }

    // CLR: not part of the source. SocketConnectTimingContractTest uses this held-connect channel for its
    // supplemental deterministic connect-timeout and cancellation coverage.
    internal sealed class PendingSocketConnect(AddressFamily family) : NioSocketChannel(family)
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // CLR: holds the non-blocking connect until Release completes; OP_CONNECT stays submitted meanwhile, as for
        // a SocketChannel.connect that returned false.
        internal override bool Connect(Socket socket, EndPoint remoteAddress)
        {
            Started.TrySetResult();
            IEventLoop loop = EventLoop;
            _ = Release.Task.ContinueWith(_ =>
            {
                try
                {
                    loop.Execute(() =>
                    {
                        if (!IsOpen) return;
                        try { BaseConnect(socket, remoteAddress); }
                        catch (SocketException) { _ = CloseAsync(); }
                    });
                }
                catch (RejectedExecutionException) { }
            }, TaskScheduler.Default);
            return false;
        }

        private bool BaseConnect(Socket socket, EndPoint remoteAddress) => base.Connect(socket, remoteAddress);
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
