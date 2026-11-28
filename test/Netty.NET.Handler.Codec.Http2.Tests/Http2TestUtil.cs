/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Common.ReferenceCountUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Utilities for the integration tests.
 */
// CLR: the final class with a private constructor is a static class; its package-private members are internal.
// ChannelPromise is TaskCompletionSource and ChannelFuture is Task. CountDownLatch is CountdownEvent
// (docs/codec-http-porting.md: "@BeforeEach/@AfterEach, Mockito, CountDownLatch (tests) | test-class
// constructor/Dispose(), Moq, CountdownEvent"); CountDownLatch.countDown() does nothing at zero while
// CountdownEvent.Signal() throws, so FrameCountDown counts down through CountDown(latch), which stops at zero.
// Mockito's mock/when/thenAnswer/doAnswer are Moq Setup/Returns/Callback (Moq Quickstart,
// https://github.com/devlooped/moq/wiki/Quickstart); any(X.class) is It.IsAny<X>(). ThreadLocalRandom is
// Random.Shared, "a thread-safe Random instance that may be used concurrently from any thread". new
// RuntimeException(e) keeps Java's detail message e.toString() through Common's ThrowableUtil.ThrowableToString,
// and new Error(msg, e) is UnreachableException. of(String) returns a StringCharSequence (a C# string is not an
// ICharSequence). The public factories of the package-private HpackEncoder/HpackDecoder are internal (a public
// member cannot expose an internal type, CS0050).
public static class Http2TestUtil
{
    /**
     * A fake exception that can be used in tests to simulate errors. The stack trace is not filled in to avoid
     * unnecessary overhead.
     */
    internal static readonly Exception FAKE_EXCEPTION = new FakeException("Fake exception");

    // CLR: There is no fillInStackTrace(): a CLR exception records its stack trace only when it is thrown, so an
    // exception that is only passed to promises carries none.
    private sealed class FakeException : Exception
    {
        internal FakeException(string message) : base(message)
        {
        }
    }

    /**
     * Interface that allows for running a operation that throws a {@link Http2Exception}.
     */
    internal interface IHttp2Runnable
    {
        void Run();
    }

    /**
     * Runs the given operation within the event loop thread of the given {@link Channel}.
     */
    internal static void RunInChannel(IChannel channel, IHttp2Runnable runnable)
    {
        channel.EventLoop.Execute(() =>
        {
            try
            {
                runnable.Run();
            }
            catch (Http2Exception e)
            {
                throw new Exception(ThrowableUtil.ThrowableToString(e), e);
            }
        });
    }

    /**
     * Returns a byte array filled with random data.
     */
    public static byte[] RandomBytes()
    {
        return RandomBytes(100);
    }

    /**
     * Returns a byte array filled with random data.
     */
    public static byte[] RandomBytes(int size)
    {
        byte[] data = new byte[size];
        Random.Shared.NextBytes(data);
        return data;
    }

    /**
     * Returns an {@link AsciiString} that wraps a randomly-filled byte array.
     */
    public static AsciiString RandomString()
    {
        return new AsciiString(RandomBytes());
    }

    public static ICharSequence Of(string s)
    {
        return new StringCharSequence(s);
    }

    internal static HpackEncoder NewTestEncoder()
    {
        try
        {
            return NewTestEncoder(true, MAX_HEADER_LIST_SIZE, MAX_HEADER_TABLE_SIZE);
        }
        catch (Http2Exception e)
        {
            throw new UnreachableException("max size not allowed?", e);
        }
    }

    internal static HpackEncoder NewTestEncoder(bool ignoreMaxHeaderListSize,
                                              long maxHeaderListSize, long maxHeaderTableSize)
    {
        HpackEncoder hpackEncoder = new HpackEncoder(false, 16, 0);
        ByteBuf buf = Unpooled.Buffer();

        try
        {
            hpackEncoder.SetMaxHeaderTableSize(maxHeaderTableSize);
            hpackEncoder.SetMaxHeaderListSize(maxHeaderListSize);
            hpackEncoder.EncodeHeaders(0, buf, EmptyHttp2Headers.INSTANCE, IHttp2HeadersEncoder.NEVER_SENSITIVE);
        }
        finally
        {
            buf.Release();
        }

        return hpackEncoder;
    }

    internal static HpackDecoder NewTestDecoder()
    {
        try
        {
            return NewTestDecoder(MAX_HEADER_LIST_SIZE, MAX_HEADER_TABLE_SIZE);
        }
        catch (Http2Exception e)
        {
            throw new UnreachableException("max size not allowed?", e);
        }
    }

    internal static HpackDecoder NewTestDecoder(long maxHeaderListSize, long maxHeaderTableSize)
    {
        HpackDecoder hpackDecoder = new HpackDecoder(maxHeaderListSize);
        hpackDecoder.SetMaxHeaderTableSize(maxHeaderTableSize);
        return hpackDecoder;
    }

    /**
     * A decorator around a {@link Http2FrameListener} that counts down the latch so that we can await the completion of
     * the request.
     */
    internal class FrameCountDown : IHttp2FrameListener
    {
        private readonly IHttp2FrameListener listener;
        private readonly CountdownEvent messageLatch;
        private readonly CountdownEvent settingsAckLatch;
        private readonly CountdownEvent dataLatch;
        private readonly CountdownEvent trailersLatch;
        private readonly CountdownEvent goAwayLatch;

        internal FrameCountDown(IHttp2FrameListener listener, CountdownEvent settingsAckLatch, CountdownEvent messageLatch,
                CountdownEvent dataLatch, CountdownEvent trailersLatch)
            : this(listener, settingsAckLatch, messageLatch, dataLatch, trailersLatch, messageLatch)
        {
        }

        internal FrameCountDown(IHttp2FrameListener listener, CountdownEvent settingsAckLatch, CountdownEvent messageLatch,
                CountdownEvent dataLatch, CountdownEvent trailersLatch, CountdownEvent goAwayLatch)
        {
            this.listener = listener;
            this.messageLatch = messageLatch;
            this.settingsAckLatch = settingsAckLatch;
            this.dataLatch = dataLatch;
            this.trailersLatch = trailersLatch;
            this.goAwayLatch = goAwayLatch;
        }

        public int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding, bool endOfStream)
        {
            int numBytes = data.ReadableBytes;
            int processed = listener.OnDataRead(ctx, streamId, data, padding, endOfStream);
            CountDown(messageLatch);

            if (dataLatch != null)
            {
                for (int i = 0; i < numBytes; ++i)
                {
                    CountDown(dataLatch);
                }
            }

            return processed;
        }

        public void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding,
                bool endStream)
        {
            listener.OnHeadersRead(ctx, streamId, headers, padding, endStream);
            CountDown(messageLatch);

            if (trailersLatch != null && endStream)
            {
                CountDown(trailersLatch);
            }
        }

        public void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int streamDependency,
                short weight, bool exclusive, int padding, bool endStream)
        {
            listener.OnHeadersRead(ctx, streamId, headers, streamDependency, weight, exclusive, padding, endStream);
            CountDown(messageLatch);

            if (trailersLatch != null && endStream)
            {
                CountDown(trailersLatch);
            }
        }

        public void OnPriorityRead(IChannelHandlerContext ctx, int streamId, int streamDependency, short weight,
                bool exclusive)
        {
            listener.OnPriorityRead(ctx, streamId, streamDependency, weight, exclusive);
            CountDown(messageLatch);
        }

        public void OnRstStreamRead(IChannelHandlerContext ctx, int streamId, long errorCode)
        {
            listener.OnRstStreamRead(ctx, streamId, errorCode);
            CountDown(messageLatch);
        }

        public void OnSettingsAckRead(IChannelHandlerContext ctx)
        {
            listener.OnSettingsAckRead(ctx);
            CountDown(settingsAckLatch);
        }

        public void OnSettingsRead(IChannelHandlerContext ctx, Http2Settings settings)
        {
            listener.OnSettingsRead(ctx, settings);
            CountDown(messageLatch);
        }

        public void OnPingRead(IChannelHandlerContext ctx, long data)
        {
            listener.OnPingRead(ctx, data);
            CountDown(messageLatch);
        }

        public void OnPingAckRead(IChannelHandlerContext ctx, long data)
        {
            listener.OnPingAckRead(ctx, data);
            CountDown(messageLatch);
        }

        public void OnPushPromiseRead(IChannelHandlerContext ctx, int streamId, int promisedStreamId,
                IHttp2Headers headers, int padding)
        {
            listener.OnPushPromiseRead(ctx, streamId, promisedStreamId, headers, padding);
            CountDown(messageLatch);
        }

        public void OnGoAwayRead(IChannelHandlerContext ctx, int lastStreamId, long errorCode, ByteBuf debugData)
        {
            listener.OnGoAwayRead(ctx, lastStreamId, errorCode, debugData);
            CountDown(goAwayLatch);
        }

        public void OnWindowUpdateRead(IChannelHandlerContext ctx, int streamId, int windowSizeIncrement)
        {
            listener.OnWindowUpdateRead(ctx, streamId, windowSizeIncrement);
            CountDown(messageLatch);
        }

        public void OnUnknownFrame(IChannelHandlerContext ctx, byte frameType, int streamId, Http2Flags flags,
                ByteBuf payload)
        {
            listener.OnUnknownFrame(ctx, frameType, streamId, flags, payload);
            CountDown(messageLatch);
        }

        // CLR: CountDownLatch.countDown(), which does nothing when the count is already zero; a Signal() that loses
        // the race to zero against another thread is retried and then stops.
        private static void CountDown(CountdownEvent latch)
        {
            while (latch.CurrentCount > 0)
            {
                try
                {
                    latch.Signal();
                    return;
                }
                catch (InvalidOperationException)
                {
                    // CLR: the count reached zero on another thread.
                }
            }
        }
    }

    // CLR: DefaultChannelPromise over ImmediateEventExecutor with the void-promise overrides. The port has no void
    // promise and unvoid() returns the promise itself, so the overrides that matter to the code under test are
    // folded into one TaskCompletionSource: a failure fires exceptionCaught on the channel's pipeline, as Java's
    // tryFailure/setFailure and the listener of unvoid() do. The addListener overrides that fail() the test have
    // no counterpart (a Task continuation cannot be intercepted).
    internal static TaskCompletionSource NewVoidPromise(IChannel channel)
    {
        TaskCompletionSource promise = new TaskCompletionSource();
        _ = promise.Task.ContinueWith(future =>
        {
            if (!future.IsCompletedSuccessfully)
            {
                channel.Pipeline.FireExceptionCaught(Cause(future));
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return promise;
    }

    internal sealed class TestStreamByteDistributorStreamState : IStreamByteDistributor.IStreamState
    {
        private readonly IHttp2Stream stream;
        internal bool isWriteAllowed;
        internal long pendingBytes;
        internal bool hasFrame;

        internal TestStreamByteDistributorStreamState(IHttp2Stream stream, long pendingBytes, bool hasFrame,
                                             bool isWriteAllowed)
        {
            this.stream = stream;
            this.isWriteAllowed = isWriteAllowed;
            this.pendingBytes = pendingBytes;
            this.hasFrame = hasFrame;
        }

        public IHttp2Stream Stream()
        {
            return stream;
        }

        public long PendingBytes()
        {
            return pendingBytes;
        }

        public bool HasFrame()
        {
            return hasFrame;
        }

        public int WindowSize()
        {
            return isWriteAllowed ? (int) Math.Min(pendingBytes, int.MaxValue) : -1;
        }
    }

    internal static IHttp2FrameWriter MockedFrameWriter()
    {
        IHttp2FrameWriter.IConfiguration configuration = new MockedConfiguration();

        ConcurrentQueue<ByteBuf> buffers = new ConcurrentQueue<ByteBuf>();

        Mock<IHttp2FrameWriter> frameWriter = new Mock<IHttp2FrameWriter>();
        frameWriter.Setup(w => w.Close()).Callback(() =>
        {
            for (;;)
            {
                if (!buffers.TryDequeue(out ByteBuf buf))
                {
                    break;
                }

                buf.Release();
            }
        });

        frameWriter.Setup(w => w.Configuration()).Returns(configuration);
        frameWriter.Setup(w => w.WriteSettingsAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<Http2Settings>(),
                It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, Http2Settings settings, TaskCompletionSource promise) => SetSuccess(promise));

        frameWriter.Setup(w => w.WriteSettingsAckAsync(It.IsAny<IChannelHandlerContext>(),
                It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, TaskCompletionSource promise) => SetSuccess(promise));

        frameWriter.Setup(w => w.WriteGoAwayAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, int lastStreamId, long errorCode, ByteBuf debugData,
                TaskCompletionSource promise) =>
            {
                buffers.Enqueue(debugData);

                return SetSuccess(promise);
            });
        frameWriter.Setup(w => w.WriteHeadersAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding, bool endStream,
                TaskCompletionSource promise) => SetSuccess(promise));

        frameWriter.Setup(w => w.WriteHeadersAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int streamDependency, short weight,
                bool exclusive, int padding, bool endStream, TaskCompletionSource promise) => SetSuccess(promise));

        frameWriter.Setup(w => w.WriteDataAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<ByteBuf>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding, bool endStream,
                TaskCompletionSource promise) =>
            {
                buffers.Enqueue(data);

                return SetSuccess(promise);
            });

        frameWriter.Setup(w => w.WriteRstStreamAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, int streamId, long errorCode, TaskCompletionSource promise) =>
                SetSuccess(promise));

        frameWriter.Setup(w => w.WriteWindowUpdateAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, int streamId, int windowSizeIncrement, TaskCompletionSource promise) =>
                SetSuccess(promise));

        frameWriter.Setup(w => w.WritePushPromiseAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<IHttp2Headers>(), It.IsAny<int>(), AnyChannelPromise())).Returns(
            (IChannelHandlerContext ctx, int streamId, int promisedStreamId, IHttp2Headers headers, int padding,
                TaskCompletionSource promise) => SetSuccess(promise));

        frameWriter.Setup(w => w.WriteFrameAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<byte>(), It.IsAny<int>(),
                It.IsAny<Http2Flags>(), It.IsAny<ByteBuf>(), AnyChannelPromise())).Returns(
            (IChannelHandlerContext ctx, byte frameType, int streamId, Http2Flags flags, ByteBuf payload,
                TaskCompletionSource promise) =>
            {
                buffers.Enqueue(payload);

                return SetSuccess(promise);
            });
        return frameWriter.Object;
    }

    // CLR: ((ChannelPromise) invocationOnMock.getArgument(n)).setSuccess(), which returns the promise as the future.
    private static Task SetSuccess(TaskCompletionSource promise)
    {
        promise.SetResult();
        return promise.Task;
    }

    private sealed class MockedConfiguration : IHttp2FrameWriter.IConfiguration
    {
        private readonly IHttp2HeadersEncoder.IConfiguration headerConfiguration = new MockedHeadersConfiguration();

        private readonly IHttp2FrameSizePolicy policy = new MockedFrameSizePolicy();

        public IHttp2HeadersEncoder.IConfiguration HeadersConfiguration()
        {
            return headerConfiguration;
        }

        public IHttp2FrameSizePolicy FrameSizePolicy()
        {
            return policy;
        }
    }

    private sealed class MockedHeadersConfiguration : IHttp2HeadersEncoder.IConfiguration
    {
        public void MaxHeaderTableSize(long max)
        {
            // NOOP
        }

        public long MaxHeaderTableSize()
        {
            return 0;
        }

        public void MaxHeaderListSize(long max)
        {
            // NOOP
        }

        public long MaxHeaderListSize()
        {
            return 0;
        }
    }

    private sealed class MockedFrameSizePolicy : IHttp2FrameSizePolicy
    {
        public void MaxFrameSize(int max)
        {
            // NOOP
        }

        public int MaxFrameSize()
        {
            return 0;
        }
    }

    internal static TaskCompletionSource AnyChannelPromise()
    {
        return It.IsAny<TaskCompletionSource>();
    }

    internal static Http2Settings AnyHttp2Settings()
    {
        return It.IsAny<Http2Settings>();
    }

    internal static ByteBuf Bb(string s)
    {
        return ByteBufUtil.WriteUtf8(UnpooledByteBufAllocator.Default, s);
    }

    internal static ByteBuf Bb(int size)
    {
        return UnpooledByteBufAllocator.Default.Buffer().WriteZero(size);
    }

    internal static void AssertEqualsAndRelease(IHttp2Frame expected, IHttp2Frame actual)
    {
        try
        {
            Assert.Equal(expected, actual);
        }
        finally
        {
            Release(expected);
            Release(actual);
            // Will return -1 when not implements ReferenceCounted.
            Assert.True(GetReferenceCount(expected) <= 0);
            Assert.True(GetReferenceCount(actual) <= 0);
        }
    }

}
