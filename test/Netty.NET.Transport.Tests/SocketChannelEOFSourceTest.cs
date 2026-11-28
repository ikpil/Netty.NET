/*
 * Copyright 2026 The Netty Project
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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body; the source's ten-second result waits stay fixed.
#pragma warning disable xUnit1051 // The source's connect/close calls and result waits do not observe the test cancellation.
#pragma warning disable CS0612 // The source uses the deprecated ChannelOption.RCVBUF_ALLOCATOR alias.
public class SocketChannelEOFSourceTest : AbstractClientSocketTest
{

    private const long PAYLOAD_BYTES = 32L * 1024 * 1024;
    private const int CHUNK_BYTES = 8 * 1024;
    private const long PAUSE_AFTER_BYTES = 128L * 1024;
    private const long PAUSE_MILLIS = 750;
    private static readonly byte[] CHUNK = CreateChunk();
    private static readonly byte[] EXPECTED_DIGEST = Digest(PAYLOAD_BYTES);

    [Fact(Timeout = 30000)]
    public async Task ReadAllPendingOnEOF()
    {
        await Run(TestContext.Current, async bootstrap =>
        {
            PayloadServer server = new PayloadServer(PAYLOAD_BYTES);
            IChannel ch = null;
            try
            {
                EndPoint address = server.BindAndAccept();
                ReadHandler handler = new ReadHandler(PAYLOAD_BYTES, PAUSE_AFTER_BYTES, PAUSE_MILLIS);
                // CLR: C# has no anonymous classes, so the source's anonymous ChannelInitializer<SocketChannel> body is
                // an ActionChannelInitializer<ISocketChannel> delegate (src/Netty.NET.Transport/Bootstrapping/Bootstrap.cs).
                bootstrap.Option(ChannelOption.AUTO_READ, false)
                    .Option(ChannelOption.TCP_NODELAY, true)
                    .Option(ChannelOption.RCVBUF_ALLOCATOR, new FixedRecvByteBufAllocator(CHUNK_BYTES))
                    .Handler(new ActionChannelInitializer<ISocketChannel>(channel =>
                    {
                        channel.Pipeline.AddLast(handler);
                    }));
                ch = await bootstrap.ConnectAsync(address);
                // CLR: Future.get(10, TimeUnit.SECONDS) is an awaited Task.WaitAsync(TimeSpan), which is "faulted
                // with a TimeoutException if it hasn't otherwise completed" in time and otherwise ends like the result
                // Task (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync).
                await handler.Result().WaitAsync(TimeSpan.FromSeconds(10));
                await server.Result().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                server.Close();
                if (ch != null)
                {
                    await ch.CloseAsync();
                }
            }
        });
    }

    private static byte[] CreateChunk()
    {
        byte[] bytes = new byte[CHUNK_BYTES];
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte) ((index * 31 + 7) & 0xff);
        }
        return bytes;
    }

    // CLR: MessageDigest.getInstance("SHA-256") is IncrementalHash.CreateHash(HashAlgorithmName.SHA256), which
    // "Provides support for computing a hash or HMAC value incrementally across several segments"; update(...) is
    // AppendData(...) and digest() is GetHashAndReset()
    // (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.incrementalhash). SHA-256 is always
    // available, so the source's NoSuchAlgorithmException to IllegalStateException wrapping has no counterpart.
    private static byte[] Digest(long length)
    {
        IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (long remaining = length; remaining > 0; remaining -= CHUNK.Length)
        {
            digest.AppendData(CHUNK, 0, (int) Math.Min(remaining, CHUNK.Length));
        }
        return digest.GetHashAndReset();
    }

    private sealed class ReadHandler : ChannelInboundHandlerAdapter
    {

        private readonly long expectedBytes;
        private readonly long pauseAfterBytes;
        private readonly long pauseMillis;
        private readonly IncrementalHash digest;
        // CLR: ImmediateEventExecutor.INSTANCE.newPromise() is a TaskCompletionSource; setSuccess(null) is
        // SetResult() and setFailure(cause) is SetException(cause).
        private readonly TaskCompletionSource result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // CLR: schedule(...) returns a Task, which cannot be cancelled through the Task itself, so
        // readFuture.cancel(true) cancels the token passed to ScheduleAsync. Before the invocation starts, the
        // cancellation prevents it (src/Netty.NET.Common/Concurrent/EventExecutorSchedulingExtensions.cs).
        private Task readFuture;
        private readonly CancellationTokenSource readFutureCancellation = new CancellationTokenSource();
        private long receivedBytes;
        private Exception failure;

        internal ReadHandler(long expectedBytes, long pauseAfterBytes, long pauseMillis)
        {
            this.expectedBytes = expectedBytes;
            this.pauseAfterBytes = pauseAfterBytes;
            this.pauseMillis = pauseMillis;
            this.digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }

        public override void ChannelActive(IChannelHandlerContext context)
        {
            RequestRead(context);
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            try
            {
                ByteBuf bytes = (ByteBuf) message;
                receivedBytes += bytes.ReadableBytes;
                // CLR: this port's ByteBuf exposes its readable bytes as borrowed ReadOnlySequence segments
                // (ByteBuf.ReadableSequence, src/Netty.NET.Buffer/ByteBuf.cs) instead of nioBuffer(); each
                // segment is appended in order.
                foreach (ReadOnlyMemory<byte> segment in bytes.ReadableSequence)
                {
                    digest.AppendData(segment.Span);
                }
            }
            finally
            {
                ReferenceCountUtil.Release(message);
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext context)
        {
            if (receivedBytes < expectedBytes
                && receivedBytes >= pauseAfterBytes
                && pauseMillis > 0
                && readFuture == null)
            {
                readFuture = context.Executor.ScheduleAsync(() =>
                {
                    RequestRead(context);
                }, TimeSpan.FromMilliseconds(pauseMillis), readFutureCancellation.Token);
            }
            else
            {
                RequestRead(context);
            }
        }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            try
            {
                if (failure != null)
                {
                    Fail(failure);
                }
                Assert.Equal(PAYLOAD_BYTES, receivedBytes);
                Assert.Equal(EXPECTED_DIGEST, digest.GetHashAndReset());
            }
            catch (Exception cause)
            {
                result.SetException(cause);
                return;
            }
            result.SetResult();
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception error)
        {
            if (!(error is ClosedChannelException))
            {
                failure = error;
            }
            _ = context.CloseAsync();
        }

        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            base.HandlerRemoved(ctx);
            if (readFuture != null)
            {
                readFutureCancellation.Cancel();
            }
        }

        private void RequestRead(IChannelHandlerContext context)
        {
            if (context.Channel.IsActive)
            {
                context.Read();
            }
        }

        internal Task Result()
        {
            return result.Task;
        }
    }

    // CLR: JUnit's fail(Throwable cause) fails "the test with the given underlying cause"
    // (https://docs.junit.org/5.10.0/api/org.junit.jupiter.api/org/junit/jupiter/api/Assertions.html). xUnit's
    // Assert.Fail only takes a message, so the cause's ToString(), which includes its message, inner exception and
    // stack trace (https://learn.microsoft.com/en-us/dotnet/api/system.exception.tostring), is that message.
    private static void Fail(Exception cause)
    {
        Assert.Fail(cause.ToString());
    }

    // CLR: AutoCloseable.close() is IDisposable.Dispose(); the source calls close() explicitly, so Close() keeps the
    // source name and Dispose() delegates to it.
    private sealed class PayloadServer : IDisposable
    {
        private readonly long sendBytes;
        private readonly Socket serverSocket;
        private readonly TaskCompletionSource result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AtomicReference<Socket> accepted = new AtomicReference<Socket>();
        private Thread thread;

        internal PayloadServer(long sendBytes)
        {
            this.sendBytes = sendBytes;
            // CLR: java.net.ServerSocket is a listening System.Net.Sockets.Socket. bind(endpoint, 1) is Bind(endpoint)
            // plus Listen(1), whose backlog is "The maximum length of the pending connections queue"
            // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.listen).
            this.serverSocket = new Socket(NetUtil.LOCALHOST.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(new IPEndPoint(NetUtil.LOCALHOST, 0));
            serverSocket.Listen(1);
        }

        internal EndPoint BindAndAccept()
        {
            thread = new Thread(() =>
            {
                long sent = 0;
                Exception failure = null;
                try
                {
                    Socket socket = serverSocket.Accept();
                    accepted.Set(socket);
                    socket.NoDelay = true;
                    // CLR: Socket.getOutputStream() is a NetworkStream over the accepted socket, and shutdownOutput()
                    // is Shutdown(SocketShutdown.Send), "Disable sending on this Socket"
                    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.shutdown).
                    Stream output = new NetworkStream(socket);
                    for (long remaining = sendBytes; remaining > 0; remaining -= CHUNK.Length)
                    {
                        int length = (int) Math.Min(remaining, CHUNK.Length);
                        output.Write(CHUNK, 0, length);
                        sent += length;
                    }
                    output.Flush();
                    socket.Shutdown(SocketShutdown.Send);
                }
                catch (Exception error)
                {
                    failure = error;
                }
                try
                {
                    if (failure != null)
                    {
                        Fail(failure);
                    }
                    Assert.Equal(PAYLOAD_BYTES, sent);
                }
                catch (Exception error)
                {
                    result.SetException(error);
                    return;
                }
                result.SetResult();
            });
            thread.Name = "payload-server";
            // CLR: setDaemon(true) is IsBackground = true: "background threads do not prevent a process from
            // terminating" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.isbackground).
            thread.IsBackground = true;
            thread.Start();
            return serverSocket.LocalEndPoint;
        }

        internal Task Result()
        {
            return result.Task;
        }

        public void Close()
        {
            serverSocket.Close();
            Socket socket = accepted.Get();
            if (socket != null)
            {
                socket.Close();
            }
            if (thread != null)
            {
                thread.Join(TimeSpan.FromSeconds(5));
            }
        }

        public void Dispose()
        {
            Close();
        }
    }
}
#pragma warning restore CS0612
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
