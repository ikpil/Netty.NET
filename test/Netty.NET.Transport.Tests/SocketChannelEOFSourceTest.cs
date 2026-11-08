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
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent producer, read timer and channel cleanup.
#pragma warning disable CS0612 // Keep the original fixed-size receive allocator.
public class SocketChannelEOFSourceTest
{
    private const long PayloadBytes = 32L * 1024 * 1024;
    private const int ChunkBytes = 8 * 1024;
    private const long PauseAfterBytes = 128L * 1024;
    private const int PauseMillis = 750;
    private static readonly byte[] Chunk = CreateChunk();
    private static readonly byte[] ExpectedDigest = Digest(PayloadBytes);

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAllPendingOnEOF(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        await using var server = new PayloadServer(PayloadBytes);
        using var handler = new ReadHandler(PayloadBytes, PauseAfterBytes, PauseMillis);
        IChannel channel = null;
        try
        {
            EndPoint address = server.BindAndAccept();
            channel = await fixture.Client.Option(ChannelOption.AUTO_READ, false).Option(ChannelOption.TCP_NODELAY, true)
                .Option(ChannelOption.RCVBUF_ALLOCATOR, new FixedRecvByteBufAllocator(ChunkBytes)).Handler(handler).ConnectAsync(address);
            await handler.Result.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await server.Result.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally { if (channel != null) await channel.CloseAsync(); }
    }
    private static byte[] CreateChunk()
    {
        var bytes = new byte[ChunkBytes];
        for (int index = 0; index < bytes.Length; index++) bytes[index] = (byte)((index * 31 + 7) & 0xff);
        return bytes;
    }
    private static byte[] Digest(long length)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (long remaining = length; remaining > 0; remaining -= Chunk.Length)
            digest.AppendData(Chunk, 0, (int)Math.Min(remaining, Chunk.Length));
        return digest.GetHashAndReset();
    }
    private sealed class ReadHandler(long expectedBytes, long pauseAfterBytes, int pauseMillis) : ChannelInboundHandlerAdapter, IDisposable
    {
        private readonly IncrementalHash _digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly CancellationTokenSource _timerCancellation = new();
        internal readonly TaskCompletionSource Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task _readFuture;
        private long _receivedBytes;
        private Exception _failure;
        public override void ChannelActive(ChannelHandlerContext context) => RequestRead(context);
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            try
            {
                var bytes = (ByteBuf)message;
                _receivedBytes += bytes.ReadableBytes;
                foreach (var segment in bytes.ReadableSequence) _digest.AppendData(segment.Span);
            }
            finally { ReferenceCountUtil.Release(message); }
        }
        public override void ChannelReadComplete(ChannelHandlerContext context)
        {
            if (_receivedBytes < expectedBytes && _receivedBytes >= pauseAfterBytes && pauseMillis > 0 && _readFuture == null)
                _readFuture = context.Executor.ScheduleAsync(() => RequestRead(context), TimeSpan.FromMilliseconds(pauseMillis), _timerCancellation.Token);
            else RequestRead(context);
        }
        public override void ChannelInactive(ChannelHandlerContext context)
        {
            try
            {
                Assert.Null(_failure);
                Assert.Equal(PayloadBytes, _receivedBytes);
                Assert.Equal(ExpectedDigest, _digest.GetHashAndReset());
                Result.TrySetResult();
            }
            catch (Exception error) { Result.TrySetException(error); }
        }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
        {
            // CLR closed-channel IOException has no JVM ClosedChannelException subtype.
            // Remember an actual receive failure; orderly native EOF is handled by ChannelInactive.
            _failure = cause;
            _ = context.CloseAsync();
        }
        public override void HandlerRemoved(ChannelHandlerContext context) => _timerCancellation.Cancel();
        private static void RequestRead(ChannelHandlerContext context) { if (context.Channel.IsActive) context.Read(); }
        public void Dispose() { _timerCancellation.Cancel(); _timerCancellation.Dispose(); _digest.Dispose(); }
    }
    private sealed class PayloadServer : IAsyncDisposable
    {
        private readonly long _sendBytes;
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private TcpClient _accepted;
        internal Task Result { get; private set; }
        internal PayloadServer(long sendBytes) { _sendBytes = sendBytes; _listener.Start(1); }
        internal EndPoint BindAndAccept()
        {
            Result = SendAsync();
            return _listener.LocalEndpoint;
        }
        private async Task SendAsync()
        {
            TcpClient socket = await _listener.AcceptTcpClientAsync(_stop.Token);
            Interlocked.Exchange(ref _accepted, socket);
            socket.NoDelay = true;
            long sent = 0;
            for (long remaining = _sendBytes; remaining > 0; remaining -= Chunk.Length)
            {
                int length = (int)Math.Min(remaining, Chunk.Length);
                await socket.GetStream().WriteAsync(Chunk.AsMemory(0, length), _stop.Token);
                sent += length;
            }
            await socket.GetStream().FlushAsync(_stop.Token);
            socket.Client.Shutdown(SocketShutdown.Send);
            Assert.Equal(PayloadBytes, sent);
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            Interlocked.Exchange(ref _accepted, null)?.Dispose();
            try
            {
                if (Result != null) await Result.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
            finally { Interlocked.Exchange(ref _accepted, null)?.Dispose(); }
            _stop.Dispose();
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore CS0612
