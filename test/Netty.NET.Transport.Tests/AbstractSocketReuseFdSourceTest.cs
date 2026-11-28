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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, which has no other cancellable wait.
#pragma warning disable xUnit1051 // The source waits for and closes its channels independently of the test cancellation.
public abstract class AbstractSocketReuseFdSourceTest : AbstractSocketTest
{
    // CLR: the source re-declares newSocketAddress() and newFactories() abstract, and its concrete fixture,
    // transport-native-epoll's EpollDomainSocketReuseFdTest, implements them with
    // EpollSocketTestPermutation.newDomainSocketAddress() and EpollSocketTestPermutation.INSTANCE.domainSocket().
    // That fixture's counterpart, DomainSocketReuseFdSourceTest, declares no members, so an abstract override here
    // would leave it uncompilable; this class therefore supplies those domain-socket implementations (through
    // DomainSocketTestPermutation) as ordinary overrides, and the class stays abstract as in the source.
    protected override EndPoint NewSocketAddress()
    {
        return DomainSocketTestPermutation.NewDomainSocketAddress();
    }

    protected override List<TestsuitePermutation.IBootstrapComboFactory<ServerBootstrap, Bootstrap>> NewFactories()
    {
        DomainSocketSourceTest.RequireDomainSockets();

        return DomainSocketTestPermutation.INSTANCE.DomainSocket();
    }

    [Fact(Timeout = 60000)]
    public async Task TestReuseFd()
    {
        await Run(TestContext.Current, TestReuseFd0);
    }

    // CLR: xUnit1024 forbids overloading the TestReuseFd test name.
    private async Task TestReuseFd0(ServerBootstrap sb, Bootstrap cb)
    {
        sb.ChildOption(ChannelOption.AUTO_READ, true);
        cb.Option(ChannelOption.AUTO_READ, true);

        // Use a number which will typically not exceed /proc/sys/net/core/somaxconn (which is 128 on linux by default
        // often).
        int numChannels = 100;
        // CLR: AtomicReference<Throwable> and AtomicInteger are StrongBox fields updated with Interlocked, and
        // ImmediateEventExecutor.INSTANCE.newPromise() is a TaskCompletionSource (IChannel.cs: "Tasks replace
        // ChannelFuture/Promise").
        StrongBox<Exception> globalException = new StrongBox<Exception>();
        StrongBox<int> serverRemaining = new StrongBox<int>(numChannels);
        StrongBox<int> clientRemaining = new StrongBox<int>(numChannels);
        TaskCompletionSource serverDonePromise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource clientDonePromise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // CLR: ActionChannelInitializer carries the initChannel body of each anonymous ChannelInitializer.
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(sch =>
        {
            ReuseFdHandler sh = new ReuseFdHandler(
                false,
                globalException,
                serverRemaining,
                serverDonePromise);
            sch.Pipeline.AddLast("handler", sh);
        }));

        cb.Handler(new ActionChannelInitializer<IChannel>(sch =>
        {
            ReuseFdHandler ch = new ReuseFdHandler(
                true,
                globalException,
                clientRemaining,
                clientDonePromise);
            sch.Pipeline.AddLast("handler", ch);
        }));

        // CLR: the ChannelFutureListener becomes a continuation of each connect Task (this port's outbound operations
        // return Tasks, IChannel.cs).
        Action<Task> listener = future =>
        {
            if (!future.IsCompletedSuccessfully)
            {
                clientDonePromise.TrySetException(Cause(future));
            }
        };

        IChannel sc = await sb.BindAsync();

        for (int i = 0; i < numChannels; i++)
        {
            _ = cb.ConnectAsync(sc.LocalAddress).ContinueWith(listener, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        await clientDonePromise.Task;
        await serverDonePromise.Task;
        await sc.CloseAsync();

        if (Volatile.Read(ref globalException.Value) != null && !IsIOException(globalException.Value))
        {
            throw globalException.Value;
        }
    }

    // CLR: java.io.IOException covers the JDK's socket I/O failures, but a CLR SocketException derives from
    // Win32Exception, not IOException (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketexception),
    // so both types take the source's IOException branch, as AbstractNioByteChannel's read-error handling does.
    private static bool IsIOException(Exception cause)
    {
        return cause is IOException || cause is SocketException;
    }

    // CLR: Future.cause() of a failed or cancelled future. A faulted Task wraps its cause in an AggregateException; a
    // cancelled Task has no exception, so it is represented by a TaskCanceledException, as a cancelled Netty future's
    // cause is a CancellationException.
    private static Exception Cause(Task future)
    {
        return future.IsCanceled ? new TaskCanceledException(future) : future.Exception?.InnerException;
    }

    internal class ReuseFdHandler : ChannelInboundHandlerAdapter
    {
        private const string EXPECTED_PAYLOAD = "payload";

        private readonly TaskCompletionSource donePromise;
        private readonly StrongBox<int> remaining;
        private readonly bool client;
        internal volatile IChannel channel;
        internal readonly StrongBox<Exception> globalException;
        // CLR: AtomicReference<Throwable>.compareAndSet(null, cause) is Interlocked.CompareExchange on this field.
        internal Exception exception;
        internal readonly StringBuilder received = new StringBuilder();

        internal ReuseFdHandler(
            bool client,
            StrongBox<Exception> globalException,
            StrongBox<int> remaining,
            TaskCompletionSource donePromise)
        {
            this.client = client;
            this.globalException = globalException;
            this.remaining = remaining;
            this.donePromise = donePromise;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            channel = ctx.Channel;

            if (client)
            {
                FailOnError(ctx.WriteAndFlushAsync(Unpooled.CopiedBuffer(EXPECTED_PAYLOAD, Encoding.ASCII)));
            }
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is ByteBuf)
            {
                ByteBuf buf = (ByteBuf) msg;
                received.Append(buf.ToString(Encoding.ASCII));
                buf.Release();

                if (received.ToString().Equals(EXPECTED_PAYLOAD))
                {
                    if (client)
                    {
                        _ = ctx.CloseAsync();
                    }
                    else
                    {
                        FailOnError(ctx.WriteAndFlushAsync(Unpooled.CopiedBuffer(EXPECTED_PAYLOAD, Encoding.ASCII)));
                    }
                }
            }
        }

        // CLR: the write listener f -> { if (!f.isSuccess()) donePromise.tryFailure(f.cause()); } becomes a
        // continuation of the write Task.
        private void FailOnError(Task future)
        {
            _ = future.ContinueWith(f =>
            {
                if (!f.IsCompletedSuccessfully)
                {
                    donePromise.TrySetException(Cause(f));
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (Interlocked.CompareExchange(ref exception, cause, null) == null)
            {
                // CLR: InvalidOperationException ("thrown when a method call is invalid for the object's current
                // state", https://learn.microsoft.com/en-us/dotnet/api/system.invalidoperationexception) is the
                // IllegalStateException counterpart.
                donePromise.TrySetException(new InvalidOperationException("exceptionCaught: " + ctx.Channel, cause));
                _ = ctx.CloseAsync();
            }

            Interlocked.CompareExchange(ref globalException.Value, cause, null);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            if (Interlocked.Decrement(ref remaining.Value) == 0)
            {
                if (received.ToString().Equals(EXPECTED_PAYLOAD))
                {
                    // CLR: SetResult throws when already completed, as Promise.setSuccess(null) does.
                    donePromise.SetResult();
                }
                else
                {
                    donePromise.TrySetException(new Exception("Unexpected payload:" + received));
                }
            }
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
