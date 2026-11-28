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

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Stream;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Tests.Stream;

// CLR: ChannelFuture/ChannelPromise are Task : ch.write/writeAndFlush are WriteAsync/ WriteAndFlushAsync,
// isDone()/isSuccess() are IsCompleted/IsCompletedSuccessfully and cause() is the Task's inner exception;
// assertInstanceOf is Assert.IsAssignableFrom (JUnit accepts subtypes) and RuntimeException is Exception.
// syncUninterruptibly() is an await of the Task (which rethrows a failure), awaitUninterruptibly() an await with
// ConfigureAwaitOptions.SuppressThrowing (with ContinueOnCapturedContext, as xUnit1030 requires in test methods),
// addListener a ContinueWith continuation and CountDownLatch.await(10, SECONDS) a Task.WhenAny of the latch's
// TaskCompletionSource and a 10 second delay, as in the Transport and codec tests. ch.newPromise() and
// promise.cancel(false) are a CancellationTokenSource whose token is passed to the write and its Cancel(), after
// which the write's Task is cancelled. AtomicInteger/AtomicBoolean locals are StrongBox<int>/StrongBox<bool>
// updated with Interlocked/Volatile (as the Transport tests use StrongBox), and the AtomicInteger/AtomicBoolean
// fields of the test inputs are int/bool fields updated the same way. The @Deprecated
// readChunk(ChannelHandlerContext) overrides are not ported with the interface member (see IChunkedInput). The
// test methods pass TestContext.Current.CancellationToken to the channel operations (xUnit1051), which the source
// calls without a promise.
public class ChunkedWriteHandlerTest
{
    private static readonly byte[] BYTES = new byte[1024 * 64];
    private static readonly FileInfo TMP;

    static ChunkedWriteHandlerTest()
    {
        for (int i = 0; i < BYTES.Length; i++)
        {
            BYTES[i] = (byte) i;
        }

        try
        {
            TMP = PlatformDependent.CreateTempFile("netty-chunk-", ".tmp", null);
            DeleteOnExit(TMP);

            using (FileStream @out = new FileStream(TMP.FullName, FileMode.Create, FileAccess.Write))
            {
                @out.Write(BYTES);
                @out.Flush();
            }
        }
        catch (IOException e)
        {
            throw new Exception(e.Message, e);
        }
    }

    // See #310
    [Fact]
    public void TestChunkedStream()
    {
        Check(new ChunkedStream(new MemoryStream(BYTES)));

        Check(new ChunkedStream(new MemoryStream(BYTES)),
                new ChunkedStream(new MemoryStream(BYTES)),
                new ChunkedStream(new MemoryStream(BYTES)));
    }

    [Fact]
    public void TestChunkedNioStream()
    {
        Check(new ChunkedNioStream(new MemoryStream(BYTES)));

        Check(new ChunkedNioStream(new MemoryStream(BYTES)),
                new ChunkedNioStream(new MemoryStream(BYTES)),
                new ChunkedNioStream(new MemoryStream(BYTES)));
    }

    [Fact]
    public void TestChunkedFile()
    {
        Check(new ChunkedFile(TMP));

        Check(new ChunkedFile(TMP), new ChunkedFile(TMP), new ChunkedFile(TMP));
    }

    [Fact]
    public void TestChunkedNioFile()
    {
        Check(new ChunkedNioFile(TMP));

        Check(new ChunkedNioFile(TMP), new ChunkedNioFile(TMP), new ChunkedNioFile(TMP));
    }

    [Fact]
    public void TestChunkedNioFileLeftPositionUnchanged()
    {
        FileStream @in = null;
        const long expectedPosition = 10;

        try
        {
            @in = OpenRead(TMP);
            @in.Position = expectedPosition;
            Check(new NoCloseChunkedNioFile(@in));
            Assert.True(@in.CanRead);
            Assert.Equal(expectedPosition, @in.Position);
        }
        finally
        {
            if (@in != null)
            {
                @in.Close();
            }
        }
    }

    [Fact]
    public void TestChunkedNioFileFailOnClosedFileChannel()
    {
        FileStream @in = OpenRead(TMP);
        @in.Close();

        Assert.Throws<ClosedChannelException>(() =>
        {
            Check(new NoCloseChunkedNioFile(@in));
        });
    }

    [Theory(DisplayName = "nio = {0}")]
    [InlineData(false)]
    [InlineData(true)]
    public void TestChunkedFileFailsWhenFileWasTruncated(bool nio)
    {
        FileInfo file = PlatformDependent.CreateTempFile("netty-chunk-truncated-", ".tmp", null);
        DeleteOnExit(file);

        // CLR: new RandomAccessFile(file, "rw") is a read/write FileStream; raf.setLength(512) is SetLength(512).
        using (FileStream raf = new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite))
        {
            raf.Write(BYTES, 0, 1024);
            IChunkedInput<ByteBuf> input = nio ? new ChunkedNioFile(raf, 0, 1024, 100) :
                    new ChunkedFile(raf, 0, 1024, 100);
            // The file is truncated after the input was created, for example by another process.
            raf.SetLength(512);

            StrongBox<int> writes = new StrongBox<int>();
            EmbeddedChannel ch = new EmbeddedChannel(new TooManyWritesHandler(writes), new ChunkedWriteHandler());

            Task future = ch.WriteAndFlushAsync(input, TestContext.Current.CancellationToken);
            Assert.True(future.IsCompleted);
            Assert.IsAssignableFrom<EndOfStreamException>(future.Exception.InnerException);
            Assert.Equal(5, Volatile.Read(ref writes.Value));

            int read = 0;

            for (;;)
            {
                ByteBuf buffer = ch.ReadOutbound<ByteBuf>();
                if (buffer == null)
                {
                    break;
                }

                read += buffer.ReadableBytes;
                buffer.Release();
            }

            Assert.Equal(500, read);
            Assert.False(ch.Finish());
        }
    }

    [Fact]
    public void TestUnchunkedData()
    {
        Check(Unpooled.WrappedBuffer(BYTES));

        Check(Unpooled.WrappedBuffer(BYTES), Unpooled.WrappedBuffer(BYTES), Unpooled.WrappedBuffer(BYTES));
    }

    // Test case which shows that there is not a bug like stated here:
    // https://stackoverflow.com/a/10426305
    [Fact]
    public async Task TestListenerNotifiedWhenIsEnd()
    {
        ByteBuf buffer = Unpooled.CopiedBuffer("Test", Encoding.Latin1);

        IChunkedInput<ByteBuf> input = new SingleChunkInput(null);

        StrongBox<bool> listenerNotified = new StrongBox<bool>(false);
        Action<Task> listener = future => Volatile.Write(ref listenerNotified.Value, true);

        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());
        // CLR: addListener(listener).syncUninterruptibly(): the continuation is awaited, so the listener has run as a
        // listener added to a done Java future has, and then the write itself is awaited.
        Task future = ch.WriteAndFlushAsync(input, TestContext.Current.CancellationToken);
        await future.ContinueWith(listener, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        await future;
        Assert.True(ch.Finish());

        // the listener should have been notified
        Assert.True(Volatile.Read(ref listenerNotified.Value));

        ByteBuf buffer2 = ch.ReadOutbound<ByteBuf>();
        Assert.Equal(buffer, buffer2);
        Assert.Null(ch.ReadOutbound());

        buffer.Release();
        buffer2.Release();
    }

    [Fact]
    public async Task TestChunkedMessageInput()
    {

        IChunkedInput<object> input = new MessageInput();

        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());
        await ch.WriteAndFlushAsync(input, TestContext.Current.CancellationToken);
        Assert.True(ch.Finish());

        Assert.Equal(0, ch.ReadOutbound<int>());
        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestWriteFailureChunkedStream()
    {
        CheckFirstFailed(new ChunkedStream(new MemoryStream(BYTES)));
    }

    [Fact]
    public void TestWriteFailureChunkedNioStream()
    {
        CheckFirstFailed(new ChunkedNioStream(new MemoryStream(BYTES)));
    }

    [Fact]
    public void TestWriteFailureChunkedFile()
    {
        CheckFirstFailed(new ChunkedFile(TMP));
    }

    [Fact]
    public void TestWriteFailureChunkedNioFile()
    {
        CheckFirstFailed(new ChunkedNioFile(TMP));
    }

    [Fact]
    public void TestWriteFailureUnchunkedData()
    {
        CheckFirstFailed(Unpooled.WrappedBuffer(BYTES));
    }

    [Fact]
    public async Task TestSkipAfterFailedChunkedStream()
    {
        await CheckSkipFailed(new ChunkedStream(new MemoryStream(BYTES)),
                        new ChunkedStream(new MemoryStream(BYTES)));
    }

    [Fact]
    public async Task TestSkipAfterFailedChunkedNioStream()
    {
        await CheckSkipFailed(new ChunkedNioStream(new MemoryStream(BYTES)),
                        new ChunkedNioStream(new MemoryStream(BYTES)));
    }

    [Fact]
    public async Task TestSkipAfterFailedChunkedFile()
    {
        await CheckSkipFailed(new ChunkedFile(TMP), new ChunkedFile(TMP));
    }

    [Fact]
    public async Task TestSkipAfterFailedChunkedNioFile()
    {
        await CheckSkipFailed(new ChunkedNioFile(TMP), new ChunkedFile(TMP));
    }

    // See https://github.com/netty/netty/issues/8700.
    [Fact]
    public void TestFailureWhenLastChunkFailed()
    {
        ChannelOutboundHandlerAdapter failLast = new FailLastHandler();

        EmbeddedChannel ch = new EmbeddedChannel(failLast, new ChunkedWriteHandler());
        Task r = ch.WriteAndFlushAsync(new ChunkedFile(TMP, 1024 * 16),
                TestContext.Current.CancellationToken); // 4 chunks
        Assert.True(ch.Finish());

        Assert.False(r.IsCompletedSuccessfully);
        Assert.IsAssignableFrom<Exception>(r.Exception.InnerException);

        // 3 out of 4 chunks were already written
        int read = 0;

        for (;;)
        {
            ByteBuf buffer = ch.ReadOutbound<ByteBuf>();
            if (buffer == null)
            {
                break;
            }

            read += buffer.ReadableBytes;
            buffer.Release();
        }

        Assert.Equal(1024 * 16 * 3, read);
    }

    [Fact]
    public void TestDiscardPendingWritesOnInactive()
    {

        StrongBox<bool> closeWasCalled = new StrongBox<bool>(false);

        IChunkedInput<ByteBuf> notifiableInput = new SingleChunkInput(closeWasCalled);

        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());

        // Write 3 messages and close channel before flushing
        Task r1 = ch.WriteAsync(new ChunkedFile(TMP), TestContext.Current.CancellationToken);
        Task r2 = ch.WriteAsync(new ChunkedNioFile(TMP), TestContext.Current.CancellationToken);
        _ = ch.WriteAsync(notifiableInput, TestContext.Current.CancellationToken);

        // Should be `false` as we do not expect any messages to be written
        Assert.False(ch.Finish());

        Assert.False(r1.IsCompletedSuccessfully);
        Assert.False(r2.IsCompletedSuccessfully);
        Assert.True(Volatile.Read(ref closeWasCalled.Value));
    }

    // See https://github.com/netty/netty/issues/8700.
    [Fact]
    public async Task TestStopConsumingChunksWhenFailed()
    {
        ByteBuf buffer = Unpooled.CopiedBuffer("Test", Encoding.Latin1);
        StrongBox<int> chunks = new StrongBox<int>(0);

        IChunkedInput<ByteBuf> nonClosableInput = new NonClosableInput(buffer, chunks);

        ChannelOutboundHandlerAdapter noOpWrites = new NoOpWritesHandler();

        EmbeddedChannel ch = new EmbeddedChannel(noOpWrites, new ChunkedWriteHandler());
        await ch.WriteAndFlushAsync(nonClosableInput, TestContext.Current.CancellationToken).ConfigureAwait(
                ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        // Should be `false` as we do not expect any messages to be written
        Assert.False(ch.Finish());
        buffer.Release();

        // We should expect only single chunked being read from the input.
        // It's possible to get a race condition here between resolving a promise and
        // allocating a new chunk, but should be fine when working with embedded channels.
        Assert.Equal(1, Volatile.Read(ref chunks.Value));
    }

    [Fact]
    public void TestCloseSuccessfulChunkedInput()
    {
        int chunks = 10;
        TestChunkedInput input = new TestChunkedInput(chunks);
        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());

        Assert.True(ch.WriteOutbound(input));

        for (int i = 0; i < chunks; i++)
        {
            ByteBuf buf = ch.ReadOutbound<ByteBuf>();
            Assert.Equal(i, buf.ReadInt());
            buf.Release();
        }

        Assert.True(input.IsClosed());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestCloseFailedChunkedInput()
    {
        Exception error = new Exception("Unable to produce a chunk");
        ThrowingChunkedInput input = new ThrowingChunkedInput(error);
        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());

        Exception e = Assert.ThrowsAny<Exception>(() =>
        {
            ch.WriteOutbound(input);
        });
        Assert.Equal(error, e);

        Assert.True(input.IsClosed());
        Assert.False(ch.Finish());
    }

    [Fact]
    public async Task TestWriteListenerInvokedAfterSuccessfulChunkedInputClosed()
    {
        TestChunkedInput input = new TestChunkedInput(2);
        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());

        StrongBox<bool> inputClosedWhenListenerInvoked = new StrongBox<bool>();
        TaskCompletionSource listenerInvoked =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task writeFuture = ch.WriteAsync(input, TestContext.Current.CancellationToken);
        _ = writeFuture.ContinueWith(future =>
        {
            Volatile.Write(ref inputClosedWhenListenerInvoked.Value, input.IsClosed());
            listenerInvoked.TrySetResult();
        }, TaskScheduler.Default);
        ch.Flush();

        Assert.True(await AwaitLatch(listenerInvoked));
        Assert.True(writeFuture.IsCompletedSuccessfully);
        Assert.True(Volatile.Read(ref inputClosedWhenListenerInvoked.Value));
        Assert.True(ch.FinishAndReleaseAll());
    }

    [Fact]
    public async Task TestWriteListenerInvokedAfterFailedChunkedInputClosed()
    {
        ThrowingChunkedInput input = new ThrowingChunkedInput(new Exception());
        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());

        StrongBox<bool> inputClosedWhenListenerInvoked = new StrongBox<bool>();
        TaskCompletionSource listenerInvoked =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task writeFuture = ch.WriteAsync(input, TestContext.Current.CancellationToken);
        _ = writeFuture.ContinueWith(future =>
        {
            Volatile.Write(ref inputClosedWhenListenerInvoked.Value, input.IsClosed());
            listenerInvoked.TrySetResult();
        }, TaskScheduler.Default);
        ch.Flush();

        Assert.True(await AwaitLatch(listenerInvoked));
        Assert.False(writeFuture.IsCompletedSuccessfully);
        Assert.True(Volatile.Read(ref inputClosedWhenListenerInvoked.Value));
        Assert.False(ch.Finish());
    }

    [Fact]
    public async Task TestWriteListenerInvokedAfterChannelClosedAndInputFullyConsumed()
    {
        // use empty input which has endOfInput = true
        TestChunkedInput input = new TestChunkedInput(0);
        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());

        StrongBox<bool> inputClosedWhenListenerInvoked = new StrongBox<bool>();
        TaskCompletionSource listenerInvoked =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task writeFuture = ch.WriteAsync(input, TestContext.Current.CancellationToken);
        _ = writeFuture.ContinueWith(future =>
        {
            Volatile.Write(ref inputClosedWhenListenerInvoked.Value, input.IsClosed());
            listenerInvoked.TrySetResult();
        }, TaskScheduler.Default);
        // close channel to make handler discard the input on subsequent flush
        _ = ch.CloseAsync(TestContext.Current.CancellationToken);
        ch.Flush();

        Assert.True(await AwaitLatch(listenerInvoked));
        Assert.True(writeFuture.IsCompletedSuccessfully);
        Assert.True(Volatile.Read(ref inputClosedWhenListenerInvoked.Value));
        Assert.False(ch.Finish());
    }

    [Fact]
    public async Task TestEndOfInputWhenChannelIsClosedwhenWrite()
    {
        IChunkedInput<ByteBuf> input = new EndOfInput();

        EmbeddedChannel ch = new EmbeddedChannel(new CloseOnWriteHandler(), new ChunkedWriteHandler());

        await ch.WriteAndFlushAsync(input, TestContext.Current.CancellationToken);
        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public async Task TestWriteListenerInvokedAfterChannelClosedAndInputNotFullyConsumed()
    {
        // use non-empty input which has endOfInput = false
        TestChunkedInput input = new TestChunkedInput(42);
        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());

        StrongBox<bool> inputClosedWhenListenerInvoked = new StrongBox<bool>();
        TaskCompletionSource listenerInvoked =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task writeFuture = ch.WriteAsync(input, TestContext.Current.CancellationToken);
        _ = writeFuture.ContinueWith(future =>
        {
            Volatile.Write(ref inputClosedWhenListenerInvoked.Value, input.IsClosed());
            listenerInvoked.TrySetResult();
        }, TaskScheduler.Default);
        // close channel to make handler discard the input on subsequent flush
        _ = ch.CloseAsync(TestContext.Current.CancellationToken);
        ch.Flush();

        Assert.True(await AwaitLatch(listenerInvoked));
        Assert.False(writeFuture.IsCompletedSuccessfully);
        Assert.True(Volatile.Read(ref inputClosedWhenListenerInvoked.Value));
        Assert.False(ch.Finish());
    }

    private static void Check(params object[] inputs)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());

        foreach (object input in inputs)
        {
            ch.WriteOutbound(input);
        }

        Assert.True(ch.Finish());

        int i = 0;
        int read = 0;

        for (;;)
        {
            ByteBuf buffer = ch.ReadOutbound<ByteBuf>();
            if (buffer == null)
            {
                break;
            }

            while (buffer.IsReadable())
            {
                Assert.Equal(BYTES[i++], buffer.ReadByte());
                read++;

                if (i == BYTES.Length)
                {
                    i = 0;
                }
            }

            buffer.Release();
        }

        Assert.Equal(BYTES.Length * inputs.Length, read);
    }

    private static void CheckFirstFailed(object input)
    {
        ChannelOutboundHandlerAdapter noOpWrites = new NoOpWritesHandler();

        EmbeddedChannel ch = new EmbeddedChannel(noOpWrites, new ChunkedWriteHandler());
        Task r = ch.WriteAndFlushAsync(input);

        // Should be `false` as we do not expect any messages to be written
        Assert.False(ch.Finish());
        Assert.IsAssignableFrom<Exception>(r.Exception.InnerException);
    }

    private static async Task CheckSkipFailed(object input1, object input2)
    {
        ChannelOutboundHandlerAdapter failFirst = new FailFirstHandler();

        EmbeddedChannel ch = new EmbeddedChannel(failFirst, new ChunkedWriteHandler());
        Task r1 = ch.WriteAsync(input1);
        Task r2 = ch.WriteAndFlushAsync(input2);
        await r2.ConfigureAwait(
                ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        Assert.True(ch.Finish());

        Assert.IsAssignableFrom<Exception>(r1.Exception.InnerException);
        Assert.True(r2.IsCompletedSuccessfully);

        // note, that after we've "skipped" the first write,
        // we expect to see the second message, chunk by chunk
        int i = 0;
        int read = 0;

        for (;;)
        {
            ByteBuf buffer = ch.ReadOutbound<ByteBuf>();
            if (buffer == null)
            {
                break;
            }

            while (buffer.IsReadable())
            {
                Assert.Equal(BYTES[i++], buffer.ReadByte());
                read++;

                if (i == BYTES.Length)
                {
                    i = 0;
                }
            }

            buffer.Release();
        }

        Assert.Equal(BYTES.Length, read);
    }

    // CLR: CountDownLatch.await(10, SECONDS) for a latch of one count.
    private static async Task<bool> AwaitLatch(TaskCompletionSource latch)
    {
        return await Task.WhenAny(latch.Task,
                Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)) == latch.Task;
    }

    // CLR: new RandomAccessFile(file, "r").getChannel() is a read-only FileStream, the channel type ChunkedNioFile
    // takes in this port.
    private static FileStream OpenRead(FileInfo file)
    {
        return new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
    }

    // CLR: File.deleteOnExit() "Requests that the file or directory denoted by this abstract pathname be deleted when
    // the virtual machine terminates" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/File.html);
    // the BCL has no such method, so an AppDomain.ProcessExit handler, which "can perform termination activities, such
    // as closing files" (https://learn.microsoft.com/en-us/dotnet/api/system.appdomain.processexit?view=net-10.0),
    // deletes the file when the test host exits, as the Transport SocketFileRegionSourceTest does. Like deleteOnExit, a
    // failed deletion is ignored.
    private static void DeleteOnExit(FileInfo file)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        };
    }

    private sealed class NoCloseChunkedNioFile : ChunkedNioFile
    {
        internal NoCloseChunkedNioFile(FileStream @in) : base(@in)
        {
        }

        public override void Close()
        {
            //no op
        }
    }

    private sealed class TooManyWritesHandler : ChannelOutboundHandlerAdapter
    {
        private readonly StrongBox<int> writes;

        internal TooManyWritesHandler(StrongBox<int> writes)
        {
            this.writes = writes;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref writes.Value) > 100)
            {
                // Stop ChunkedWriteHandler if it keeps writing empty chunks.
                ReferenceCountUtil.Release(msg);
                Task failed = Task.FromException(new InvalidOperationException("too many writes"));
                _ = ctx.CloseAsync();
                return failed;
            }

            return ctx.WriteAsync(msg, cancellationToken);
        }
    }

    private sealed class SingleChunkInput : IChunkedInput<ByteBuf>
    {
        private readonly StrongBox<bool> closeWasCalled;
        private bool done;
        private readonly ByteBuf buffer = Unpooled.CopiedBuffer("Test", Encoding.Latin1);

        internal SingleChunkInput(StrongBox<bool> closeWasCalled)
        {
            this.closeWasCalled = closeWasCalled;
        }

        public bool IsEndOfInput()
        {
            return done;
        }

        public void Close()
        {
            buffer.Release();

            if (closeWasCalled != null)
            {
                Volatile.Write(ref closeWasCalled.Value, true);
            }
        }

        public ByteBuf ReadChunk(IByteBufAllocator allocator)
        {
            if (done)
            {
                return null;
            }

            done = true;
            return buffer.RetainedDuplicate();
        }

        public long Length()
        {
            return -1;
        }

        public long Progress()
        {
            return 1;
        }
    }

    // CLR: The Integer 0 and the Boolean false are boxed.
    private sealed class MessageInput : IChunkedInput<object>
    {
        private bool done;

        public bool IsEndOfInput()
        {
            return done;
        }

        public void Close()
        {
            // NOOP
        }

        public object ReadChunk(IByteBufAllocator ctx)
        {
            if (done)
            {
                return false;
            }

            done = true;
            return 0;
        }

        public long Length()
        {
            return -1;
        }

        public long Progress()
        {
            return 1;
        }
    }

    private sealed class NonClosableInput : IChunkedInput<ByteBuf>
    {
        private readonly ByteBuf buffer;
        private readonly StrongBox<int> chunks;

        internal NonClosableInput(ByteBuf buffer, StrongBox<int> chunks)
        {
            this.buffer = buffer;
            this.chunks = chunks;
        }

        public bool IsEndOfInput()
        {
            return Volatile.Read(ref chunks.Value) >= 5;
        }

        public void Close()
        {
            // no-op
        }

        public ByteBuf ReadChunk(IByteBufAllocator allocator)
        {
            Interlocked.Increment(ref chunks.Value);

            return buffer.RetainedDuplicate();
        }

        public long Length()
        {
            return -1;
        }

        public long Progress()
        {
            return 1;
        }
    }

    private sealed class EndOfInput : IChunkedInput<ByteBuf>
    {

        public bool IsEndOfInput()
        {
            return true;
        }

        public void Close()
        {
        }

        public ByteBuf ReadChunk(IByteBufAllocator allocator)
        {
            return null;
        }

        public long Length()
        {
            return -1;
        }

        public long Progress()
        {
            return 1;
        }
    }

    private sealed class CloseOnWriteHandler : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            ReferenceCountUtil.Release(msg);
            // Calling close so we will drop all queued messages in the ChunkedWriteHandler.
            _ = ctx.CloseAsync();
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpWritesHandler : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            ReferenceCountUtil.Release(msg);

            return Task.FromException(new Exception());
        }
    }

    private sealed class FailLastHandler : ChannelOutboundHandlerAdapter
    {
        private int passedWrites;

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            if (++this.passedWrites < 4)
            {
                return ctx.WriteAsync(msg, cancellationToken);
            }
            else
            {
                ReferenceCountUtil.Release(msg);

                return Task.FromException(new Exception());
            }
        }
    }

    private sealed class FailFirstHandler : ChannelOutboundHandlerAdapter
    {
        private bool alreadyFailed;

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            if (alreadyFailed)
            {
                return ctx.WriteAsync(msg, cancellationToken);
            }
            else
            {
                this.alreadyFailed = true;
                ReferenceCountUtil.Release(msg);

                return Task.FromException(new Exception());
            }
        }
    }

    private sealed class TestChunkedInput : IChunkedInput<ByteBuf>
    {
        private readonly int chunksToProduce;

        private int chunksProduced;
        private volatile bool closed;

        internal TestChunkedInput(int chunksToProduce)
        {
            this.chunksToProduce = chunksToProduce;
        }

        public bool IsEndOfInput()
        {
            return chunksProduced >= chunksToProduce;
        }

        public void Close()
        {
            closed = true;
        }

        public ByteBuf ReadChunk(IByteBufAllocator allocator)
        {
            ByteBuf buf = allocator.Buffer();
            buf.WriteInt(chunksProduced);
            chunksProduced++;
            return buf;
        }

        public long Length()
        {
            return chunksToProduce;
        }

        public long Progress()
        {
            return chunksProduced;
        }

        internal bool IsClosed()
        {
            return closed;
        }
    }

    private sealed class ThrowingChunkedInput : IChunkedInput<ByteBuf>
    {
        private readonly Exception error;

        private volatile bool closed;

        internal ThrowingChunkedInput(Exception error)
        {
            this.error = error;
        }

        public bool IsEndOfInput()
        {
            return false;
        }

        public void Close()
        {
            closed = true;
        }

        public ByteBuf ReadChunk(IByteBufAllocator allocator)
        {
            throw error;
        }

        public long Length()
        {
            return -1;
        }

        public long Progress()
        {
            return -1;
        }

        internal bool IsClosed()
        {
            return closed;
        }
    }

    [Fact]
    public void TestResumeTransferReenteredFromReadChunkMustNotBeLostWhenSuspending()
    {
        ChunkedWriteHandler streamer = new ChunkedWriteHandler();
        EmbeddedChannel ch = new EmbeddedChannel(streamer);
        SuspendResumeChunkedInput input = new SuspendResumeChunkedInput(streamer);
        Task future = ch.WriteAsync(input, TestContext.Current.CancellationToken);
        ch.Flush();

        // The input suspends (first readChunk(...) returns null) after its implementation has
        // already produced data and synchronously called resumeTransfer() from within
        // readChunk(...). A plain re-entrancy guard would drop that nested call and leave the
        // transfer suspended forever; the guard must re-run the drain loop instead.
        ByteBuf chunk = ch.ReadOutbound<ByteBuf>();
        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.FinishAndReleaseAll());

        Assert.Equal(42, chunk.ReadInt());
        chunk.Release();
        Assert.Equal(2, input.ReadChunkCalls());
        Assert.True(input.IsClosed());
        Assert.True(future.IsCompletedSuccessfully);
    }

    private sealed class SuspendResumeChunkedInput : IChunkedInput<ByteBuf>
    {
        private readonly ChunkedWriteHandler streamer;
        private int readChunkCalls;
        private int closed;

        internal SuspendResumeChunkedInput(ChunkedWriteHandler streamer)
        {
            this.streamer = streamer;
        }

        internal int ReadChunkCalls()
        {
            return Volatile.Read(ref readChunkCalls);
        }

        internal bool IsClosed()
        {
            return Volatile.Read(ref closed) != 0;
        }

        public bool IsEndOfInput()
        {
            return Volatile.Read(ref readChunkCalls) >= 2;
        }

        public void Close()
        {
            Volatile.Write(ref closed, 1);
        }

        public ByteBuf ReadChunk(IByteBufAllocator allocator)
        {
            if (Interlocked.Increment(ref readChunkCalls) == 1)
            {
                // Data becomes available "now", so the producer resumes synchronously, but this
                // call still reports "no chunk yet": doFlush(...) is about to suspend when the
                // nested resumeTransfer() arrives, and that call must not be dropped.
                streamer.ResumeTransfer();
                return null;
            }

            ByteBuf buffer = allocator.Buffer(4);
            buffer.WriteInt(42);
            return buffer;
        }

        public long Length()
        {
            return -1;
        }

        public long Progress()
        {
            return 0;
        }
    }

    [Fact]
    public void TestFlushReenteredFromReadChunkMustNotConsumeQueueEntryTwice()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());
        ReentrantFlushChunkedInput input = new ReentrantFlushChunkedInput(ch);
        Task future = ch.WriteAsync(input, TestContext.Current.CancellationToken);
        ch.Flush();

        ByteBuf first = ch.ReadOutbound<ByteBuf>();
        ByteBuf second = ch.ReadOutbound<ByteBuf>();
        Assert.Null(ch.ReadOutbound());
        // Before the re-entrancy guard, the nested flush() consumed the queue entry and both
        // invocations read from the same input; the outer doFlush(...) then failed with
        // NoSuchElementException from ArrayDeque.remove() on the already-empty queue.
        // Both chunks were read above, so no message must be left in the buffers.
        Assert.False(ch.FinishAndReleaseAll());

        Assert.Equal(1, first.ReadInt());
        first.Release();
        Assert.Equal(2, second.ReadInt());
        second.Release();
        // The input must have been read exactly twice, and closed exactly once, after the last
        // read: before the guard, close() happened from the nested invocation while the outer
        // readChunk(...) call had not even returned yet.
        Assert.Equal(2, input.ReadChunkCalls());
        Assert.True(input.IsClosed());
        Assert.Equal(2, input.ReadChunkCallsAtClose());
        Assert.True(future.IsCompletedSuccessfully);
    }

    private sealed class ReentrantFlushChunkedInput : IChunkedInput<ByteBuf>
    {
        private readonly IChannel channel;
        private int readChunkCalls;
        private int readChunkCallsAtClose = -1;
        private int closed;
        private int chunkNo;

        internal ReentrantFlushChunkedInput(IChannel channel)
        {
            this.channel = channel;
        }

        internal int ReadChunkCalls()
        {
            return Volatile.Read(ref readChunkCalls);
        }

        internal int ReadChunkCallsAtClose()
        {
            return Volatile.Read(ref readChunkCallsAtClose);
        }

        internal bool IsClosed()
        {
            return Volatile.Read(ref closed) != 0;
        }

        public bool IsEndOfInput()
        {
            return chunkNo >= 2;
        }

        public void Close()
        {
            Volatile.Write(ref closed, 1);
            Volatile.Write(ref readChunkCallsAtClose, Volatile.Read(ref readChunkCalls));
        }

        public ByteBuf ReadChunk(IByteBufAllocator allocator)
        {
            int call = Interlocked.Increment(ref readChunkCalls);
            if (call == 1)
            {
                // Runs between queue.peek() and queue.remove() in doFlush(...). Re-enter the
                // handler synchronously, like incidental pipeline activity triggered from user
                // code (a flush, a writability change, a close notification) would.
                channel.Flush();
            }

            chunkNo++;
            ByteBuf buffer = allocator.Buffer(4);
            buffer.WriteInt(chunkNo);
            return buffer;
        }

        public long Length()
        {
            return -1;
        }

        public long Progress()
        {
            return 0;
        }
    }

    /**
     * A {@link ChunkedInput} that has no data available yet (the producer has not called resumeTransfer()).
     */
    private sealed class PendingInput : IChunkedInput<ByteBuf>
    {
        internal bool closed;

        public bool IsEndOfInput()
        {
            return false;
        }

        public void Close()
        {
            closed = true;
        }

        public ByteBuf ReadChunk(IByteBufAllocator allocator)
        {
            return null;
        }

        public long Length()
        {
            return -1;
        }

        public long Progress()
        {
            return 0;
        }
    }

    [Fact]
    public void TestPendingWritesFailedWhenHandlerRemoved()
    {
        ChunkedWriteHandler handler = new ChunkedWriteHandler();
        EmbeddedChannel ch = new EmbeddedChannel(handler);
        PendingInput input = new PendingInput();
        Task inputFuture = ch.WriteAndFlushAsync(input, TestContext.Current.CancellationToken);
        ByteBuf buffer = Unpooled.CopiedBuffer("queued", Encoding.ASCII);
        Task bufferFuture = ch.WriteAndFlushAsync(buffer, TestContext.Current.CancellationToken);
        Assert.False(inputFuture.IsCompleted);
        Assert.False(bufferFuture.IsCompleted);

        ch.Pipeline.Remove(handler);

        Assert.True(inputFuture.IsCompleted);
        Assert.IsAssignableFrom<ChannelException>(inputFuture.Exception.InnerException);
        Assert.True(input.closed);
        Assert.True(bufferFuture.IsCompleted);
        Assert.IsAssignableFrom<ChannelException>(bufferFuture.Exception.InnerException);
        Assert.Equal(0, buffer.RefCnt);
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestCancelledWritesAreClosedAndReleased()
    {
        ChunkedWriteHandler handler = new ChunkedWriteHandler();
        EmbeddedChannel ch = new EmbeddedChannel(handler);
        PendingInput input = new PendingInput();
        using CancellationTokenSource inputPromise = new CancellationTokenSource();
        Task inputFuture = ch.WriteAndFlushAsync(input, inputPromise.Token);
        ByteBuf buffer = Unpooled.CopiedBuffer("queued", Encoding.ASCII);
        using CancellationTokenSource bufferPromise = new CancellationTokenSource();
        Task bufferFuture = ch.WriteAndFlushAsync(buffer, bufferPromise.Token);

        inputPromise.Cancel();
        Assert.True(inputFuture.IsCanceled);
        bufferPromise.Cancel();
        Assert.True(bufferFuture.IsCanceled);
        handler.ResumeTransfer();

        Assert.True(input.closed);
        Assert.Equal(0, buffer.RefCnt);
        Assert.False(ch.Finish());
    }
}
