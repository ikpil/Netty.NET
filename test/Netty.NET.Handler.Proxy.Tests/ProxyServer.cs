/*
 * Copyright 2014 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Proxy.Tests;

// CLR: SocketAddress is EndPoint and InetSocketAddress is IPEndPoint or, unresolved, DnsEndPoint; getHostString() is
// NetUtil.GetHostname and getPort() the endpoint's Port. SocketUtils.socketAddress(host, port) of the literal hosts the
// intermediaries receive (the next proxy's numeric address) is new IPEndPoint(IPAddress.Parse(host), port), as in the
// handler and codec ports (docs/handler-porting.md, Addresses). LinkedBlockingQueue is ConcurrentQueue and ArrayDeque
// is Queue. The anonymous ChannelInitializer is the nested ServerInitializer, and syncUninterruptibly() blocks on the
// bind Task. PlatformDependent.throwException(t) rethrows the recorded exception with its stack trace
// (ExceptionDispatchInfo). The listeners on the backend connect and the terminal write run on the event loop of the
// frontend channel through Common's EventExecutorTaskScheduler (DefaultPromise.notifyListeners), and
// ChannelFutureListener.CLOSE closes future.channel() when the write completes. CharsetUtil.US_ASCII is Encoding.ASCII.
// The inner (non-static) classes IntermediaryHandler and TerminalHandler take the enclosing ProxyServer as a
// constructor argument, as a C# nested class has no enclosing instance. IllegalStateException is
// InvalidOperationException.
internal abstract class ProxyServer
{
    protected readonly IInternalLogger logger;

    private readonly IServerSocketChannel ch;
    private readonly ConcurrentQueue<Exception> recordedExceptions = new ConcurrentQueue<Exception>();
    protected readonly TestMode testMode;
    protected readonly string username;
    protected readonly string password;
    protected readonly EndPoint destination;

    /**
     * Starts a new proxy server with disabled authentication for testing purpose.
     *
     * @param useSsl {@code true} if and only if implicit SSL is enabled
     * @param testMode the test mode
     * @param destination the expected destination. If the client requests proxying to a different destination, this
     * server will reject the connection request.
     */
    protected ProxyServer(bool useSsl, TestMode testMode, EndPoint destination)
        : this(useSsl, testMode, destination, null, null)
    {
    }

    /**
     * Starts a new proxy server with disabled authentication for testing purpose.
     *
     * @param useSsl {@code true} if and only if implicit SSL is enabled
     * @param testMode the test mode
     * @param username the expected username. If the client tries to authenticate with a different username, this server
     * will fail the authentication request.
     * @param password the expected password. If the client tries to authenticate with a different password, this server
     * will fail the authentication request.
     * @param destination the expected destination. If the client requests proxying to a different destination, this
     * server will reject the connection request.
     */
    protected ProxyServer(
            bool useSsl, TestMode testMode,
            EndPoint destination, string username, string password)
    {
        logger = InternalLoggerFactory.GetInstance(GetType());

        this.testMode = testMode;
        this.destination = destination;
        this.username = username;
        this.password = password;

        ServerBootstrap b = new ServerBootstrap();
        b.Channel<NioServerSocketChannel>();
        b.Group(ProxyHandlerTest.group);
        b.ChildHandler(new ServerInitializer(this, useSsl));

        ch = (IServerSocketChannel) b.BindAsync(NetUtil.LOCALHOST, 0).GetAwaiter().GetResult();
    }

    private sealed class ServerInitializer : ChannelInitializer<ISocketChannel>
    {
        private readonly ProxyServer server;
        private readonly bool useSsl;

        internal ServerInitializer(ProxyServer server, bool useSsl)
        {
            this.server = server;
            this.useSsl = useSsl;
        }

        protected override void InitChannel(ISocketChannel ch)
        {
            IChannelPipeline p = ch.Pipeline;

            if (useSsl)
            {
                p.AddLast(ProxyHandlerTest.serverSslCtx.NewHandler(ch.Alloc()));
            }

            server.Configure(ch);
        }
    }

    public IPEndPoint Address()
    {
        return new IPEndPoint(NetUtil.LOCALHOST, ((IPEndPoint) ch.LocalAddress).Port);
    }

    protected abstract void Configure(ISocketChannel ch);

    internal void RecordException(Exception t)
    {
        logger.Warn("Unexpected exception from proxy server:", t);
        recordedExceptions.Enqueue(t);
    }

    /**
     * Clears all recorded exceptions.
     */
    public void ClearExceptions()
    {
        recordedExceptions.Clear();
    }

    /**
     * Logs all recorded exceptions and raises the last one so that the caller can fail.
     */
    // CLR: poll() is TryDequeue, null when the queue is empty. As in the source, the loop only ends when t is null, so
    // the final check never raises; the recorded exceptions are logged.
    public void CheckExceptions()
    {
        Exception t;

        for (;;)
        {
            t = recordedExceptions.TryDequeue(out Exception polled) ? polled : null;
            if (t == null)
            {
                break;
            }

            logger.Warn("Unexpected exception:", t);
        }

        if (t != null)
        {
            ExceptionDispatchInfo.Throw(t);
        }
    }

    public void Stop()
    {
        _ = ch.CloseAsync();
    }

    // CLR: getHostString() of an InetSocketAddress.
    internal static string GetHostString(EndPoint address)
    {
        return NetUtil.GetHostname(address);
    }

    // CLR: getPort() of an InetSocketAddress.
    internal static int GetPort(EndPoint address)
    {
        return address is DnsEndPoint unresolved ? unresolved.Port : ((IPEndPoint) address).Port;
    }

    // CLR: SocketUtils.socketAddress(hostname, port) of a literal host (see the class comment).
    internal static EndPoint SocketAddress(string hostname, int port)
    {
        return new IPEndPoint(IPAddress.Parse(hostname), port);
    }

    protected abstract class IntermediaryHandler : SimpleChannelInboundHandler<object>
    {
        private readonly ProxyServer server;
        private readonly Queue<object> received = new Queue<object>();

        private bool finished;
        private IChannel backend;

        protected IntermediaryHandler(ProxyServer server)
        {
            this.server = server;
        }

        // CLR: the field is this.finished, because the method also declares a local finished, which C# does not let
        // the field's simple name precede (CS0844).
        protected sealed override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            if (this.finished)
            {
                received.Enqueue(ReferenceCountUtil.Retain(msg));
                Flush();
                return;
            }

            bool finished = HandleProxyProtocol(ctx, msg);
            if (finished)
            {
                this.finished = true;
                Task<IChannel> f = ConnectToDestination(ctx.Channel.EventLoop, new BackendHandler(server, ctx));
                _ = f.ContinueWith(future =>
                {
                    if (!future.IsCompletedSuccessfully)
                    {
                        server.RecordException(future.Exception != null
                                ? future.Exception.InnerException
                                : new TaskCanceledException(future));
                        _ = ctx.CloseAsync();
                    }
                    else
                    {
                        backend = future.Result;
                        Flush();
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                        new EventExecutorTaskScheduler(() => ctx.Executor));
            }
        }

        private void Flush()
        {
            if (backend != null)
            {
                bool wrote = false;

                for (;;)
                {
                    if (!received.TryDequeue(out object msg))
                    {
                        break;
                    }

                    _ = backend.WriteAsync(msg);
                    wrote = true;
                }

                if (wrote)
                {
                    backend.Flush();
                }
            }
        }

        protected abstract bool HandleProxyProtocol(IChannelHandlerContext ctx, object msg);

        protected abstract EndPoint IntermediaryDestination();

        private Task<IChannel> ConnectToDestination(IEventLoop loop, IChannelHandler handler)
        {
            Bootstrap b = new Bootstrap();
            b.Channel<NioSocketChannel>();
            b.Group(loop);
            b.Handler(handler);

            return b.ConnectAsync(IntermediaryDestination());
        }

        public sealed override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            ctx.Flush();
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            if (backend != null)
            {
                _ = backend.CloseAsync();
            }
        }

        public sealed override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            server.RecordException(cause);
            _ = ctx.CloseAsync();
        }

        private sealed class BackendHandler : ChannelInboundHandlerAdapter
        {
            private readonly ProxyServer server;
            private readonly IChannelHandlerContext frontend;

            internal BackendHandler(ProxyServer server, IChannelHandlerContext frontend)
            {
                this.server = server;
                this.frontend = frontend;
            }

            public override void ChannelRead(IChannelHandlerContext ctx, object msg)
            {
                _ = frontend.WriteAsync(msg);
            }

            public override void ChannelReadComplete(IChannelHandlerContext ctx)
            {
                frontend.Flush();
            }

            public override void ChannelInactive(IChannelHandlerContext ctx)
            {
                _ = frontend.CloseAsync();
            }

            public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
            {
                server.RecordException(cause);
                _ = ctx.CloseAsync();
            }
        }
    }

    protected abstract class TerminalHandler : SimpleChannelInboundHandler<object>
    {
        private readonly ProxyServer server;

        private bool finished;

        protected TerminalHandler(ProxyServer server)
        {
            this.server = server;
        }

        // CLR: the field is this.finished, because the method also declares a local finished, which C# does not let
        // the field's simple name precede (CS0844).
        protected sealed override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            if (this.finished)
            {
                string str = ((ByteBuf) msg).ToString(Encoding.ASCII);
                if ("A\n".Equals(str))
                {
                    _ = ctx.WriteAsync(Unpooled.CopiedBuffer("1\n", Encoding.ASCII));
                }
                else if ("B\n".Equals(str))
                {
                    _ = ctx.WriteAsync(Unpooled.CopiedBuffer("2\n", Encoding.ASCII));
                }
                else if ("C\n".Equals(str))
                {
                    _ = ctx.WriteAsync(Unpooled.CopiedBuffer("3\n", Encoding.ASCII))
                       .ContinueWith(future => ctx.Channel.CloseAsync(), CancellationToken.None,
                               TaskContinuationOptions.ExecuteSynchronously,
                               new EventExecutorTaskScheduler(() => ctx.Executor));
                }
                else
                {
                    throw new InvalidOperationException("unexpected message: " + str);
                }

                return;
            }

            bool finished = HandleProxyProtocol(ctx, msg);
            if (finished)
            {
                this.finished = finished;
            }
        }

        protected abstract bool HandleProxyProtocol(IChannelHandlerContext ctx, object msg);

        public sealed override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            ctx.Flush();
        }

        public sealed override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            server.RecordException(cause);
            _ = ctx.CloseAsync();
        }
    }
}
