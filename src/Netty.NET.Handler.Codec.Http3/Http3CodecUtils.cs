/*
 * Copyright 2020 The Netty Project
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http3.Http3ErrorCode;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is an internal static class. QuicChannel and QuicStreamChannel are the codec-quic
// interfaces IQuicChannel and IQuicStreamChannel, QuicStreamType the C# enum (docs/codec-classes-quic-porting.md, API
// shapes). A returned ChannelFuture is a Task (docs/transport-porting.md); quicChannel.close(true, code, buffer) and
// shutdownOutput(code) are CloseAsync/ShutdownOutputAsync, whose Tasks are not awaited, as Java ignores the futures.
// closeOnFailure(future) takes the channel of the write as its first argument, because a Task has no channel()
// (ChannelFutureListener.CLOSE_ON_FAILURE closes future.channel(); docs/codec-http2-porting.md, Listener timing).
// IllegalArgumentException is ArgumentException; msg.writeCharSequence(msg, US_ASCII) is
// WriteCharSequence(msg, Encoding.ASCII). The CLR-only helpers NewPromise, Write, WriteAndFlush, Cascade, AddListener
// and Cause are the Http2CodecUtil helpers of codec-http2's port (docs/codec-http2-porting.md, Shared CLR adaptations),
// copied because codec-http3 does not depend on codec-http2. The two numBytesForVariableLengthInteger overloads keep
// their name: the byte overload is the one for a CLR byte argument (Java's signed byte; only bits 6 and 7 are read, so
// the sign does not matter), callers pass a long variable to the other one.
internal static class Http3CodecUtils
{

    // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.8
    internal const long MIN_RESERVED_FRAME_TYPE = 0x1f * 1 + 0x21;
    internal const long MAX_RESERVED_FRAME_TYPE = 0x1f * (long) int.MaxValue + 0x21;

    // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2
    internal const int HTTP3_DATA_FRAME_TYPE = 0x0;
    internal const int HTTP3_HEADERS_FRAME_TYPE = 0x1;
    internal const int HTTP3_CANCEL_PUSH_FRAME_TYPE = 0x3;
    internal const int HTTP3_SETTINGS_FRAME_TYPE = 0x4;
    internal const int HTTP3_PUSH_PROMISE_FRAME_TYPE = 0x5;
    internal const int HTTP3_GO_AWAY_FRAME_TYPE = 0x7;
    internal const int HTTP3_MAX_PUSH_ID_FRAME_TYPE = 0xd;

    internal const int HTTP3_CANCEL_PUSH_FRAME_MAX_LEN = 8;
    internal const int HTTP3_SETTINGS_FRAME_MAX_LEN = 256;
    internal const int HTTP3_GO_AWAY_FRAME_MAX_LEN = 8;
    internal const int HTTP3_MAX_PUSH_ID_FRAME_MAX_LEN = 8;

    internal const int HTTP3_CONTROL_STREAM_TYPE = 0x00;
    internal const int HTTP3_PUSH_STREAM_TYPE = 0x01;
    internal const int HTTP3_QPACK_ENCODER_STREAM_TYPE = 0x02;
    internal const int HTTP3_QPACK_DECODER_STREAM_TYPE = 0x03;

    /**
     * Default is unlimited in the RFC but we want to enforce some "security" default limit.
     * See <a href="https://datatracker.ietf.org/doc/html/rfc9114#section-4.2.2">RFC9114 Section 4.2.2</a>.
     */
    internal const long DEFAULT_MAX_FIELD_SECTION_SIZE = 8192;

    // Let's use 32kb as max
    internal const int DEFAULT_MAX_UNKNOWN_FRAME_PAYLOAD_LENGTH = 32 * 1024;

    internal static long CheckIsReservedFrameType(long type)
    {
        return ObjectUtil.CheckInRange(type, MIN_RESERVED_FRAME_TYPE, MAX_RESERVED_FRAME_TYPE, "type");
    }

    internal static bool IsReservedFrameType(long type)
    {
        return type >= MIN_RESERVED_FRAME_TYPE && type <= MAX_RESERVED_FRAME_TYPE;
    }

    /**
     * Checks if the passed {@link QuicStreamChannel} is a server initiated stream.
     *
     * @param channel to check.
     * @return {@code true} if the passed {@link QuicStreamChannel} is a server initiated stream.
     */
    internal static bool IsServerInitiatedQuicStream(IQuicStreamChannel channel)
    {
        // Server streams have odd stream id
        // https://www.rfc-editor.org/rfc/rfc9000.html#name-stream-types-and-identifier
        return channel.StreamId() % 2 != 0;
    }

    internal static bool IsReservedHttp2FrameType(long type)
    {
        switch ((int) type)
        {
            // Reserved types that were used in HTTP/2
            // https://tools.ietf.org/html/draft-ietf-quic-http-32#section-11.2.1
            case 0x2:
            case 0x6:
            case 0x8:
            case 0x9:
                return true;
            default:
                return false;
        }
    }

    internal static bool IsReservedHttp2Setting(long key)
    {
        // Reserved types that were used in HTTP/2
        // https://tools.ietf.org/html/draft-ietf-quic-http-32#section-11.2.2
        return 0x2L <= key && key <= 0x5L;
    }

    /**
     * Returns the number of bytes needed to encode the variable length integer.
     *
     * See <a href="https://tools.ietf.org/html/draft-ietf-quic-transport-32#section-16">
     *     Variable-Length Integer Encoding</a>.
     */
    internal static int NumBytesForVariableLengthInteger(long value)
    {
        if (value <= 63)
        {
            return 1;
        }
        if (value <= 16383)
        {
            return 2;
        }
        if (value <= 1073741823)
        {
            return 4;
        }
        if (value <= 4611686018427387903L)
        {
            return 8;
        }
        throw new ArgumentException();
    }

    /**
     * Write the variable length integer into the {@link ByteBuf}.
     *
     * See <a href="https://tools.ietf.org/html/draft-ietf-quic-transport-32#section-16">
     *     Variable-Length Integer Encoding</a>.
     */
    internal static void WriteVariableLengthInteger(ByteBuf @out, long value)
    {
        int numBytes = NumBytesForVariableLengthInteger(value);
        WriteVariableLengthInteger(@out, value, numBytes);
    }

    /**
     * Write the variable length integer into the {@link ByteBuf}.
     *
     * See <a href="https://tools.ietf.org/html/draft-ietf-quic-transport-32#section-16">
     *     Variable-Length Integer Encoding</a>.
     */
    internal static void WriteVariableLengthInteger(ByteBuf @out, long value, int numBytes)
    {
        int writerIndex = @out.WriterIndex;
        switch (numBytes)
        {
            case 1:
                @out.WriteByte(unchecked((byte) value));
                break;
            case 2:
                @out.WriteShort(unchecked((short) value));
                EncodeLengthIntoBuffer(@out, writerIndex, 0x40);
                break;
            case 4:
                @out.WriteInt(unchecked((int) value));
                EncodeLengthIntoBuffer(@out, writerIndex, 0x80);
                break;
            case 8:
                @out.WriteLong(value);
                EncodeLengthIntoBuffer(@out, writerIndex, 0xc0);
                break;
            default:
                throw new ArgumentException();
        }
    }

    private static void EncodeLengthIntoBuffer(ByteBuf @out, int index, byte b)
    {
        @out.SetByte(index, @out.GetByte(index) | b);
    }

    /**
     * Read the variable length integer from the {@link ByteBuf}.
     *
     * See <a href="https://tools.ietf.org/html/draft-ietf-quic-transport-32#section-16">
     *     Variable-Length Integer Encoding </a>
     */
    internal static long ReadVariableLengthInteger(ByteBuf @in, int len)
    {
        switch (len)
        {
            case 1:
                return @in.ReadByte();
            case 2:
                return @in.ReadUnsignedShort() & 0x3fff;
            case 4:
                return @in.ReadUnsignedInt() & 0x3fffffff;
            case 8:
                return @in.ReadLong() & 0x3fffffffffffffffL;
            default:
                throw new ArgumentException();
        }
    }

    /**
     * Returns the number of bytes that were encoded into the byte for a variable length integer to read.
     *
     * See <a href="https://tools.ietf.org/html/draft-ietf-quic-transport-32#section-16">
     *     Variable-Length Integer Encoding </a>
     */
    internal static int NumBytesForVariableLengthInteger(byte b)
    {
        byte val = (byte) (b >> 6);
        if ((val & 1) != 0)
        {
            if ((val & 2) != 0)
            {
                return 8;
            }
            return 2;
        }
        if ((val & 2) != 0)
        {
            return 4;
        }
        return 1;
    }

    internal static void CriticalStreamClosed(IChannelHandlerContext ctx)
    {
        if (ctx.Channel.Parent.IsActive)
        {
            // Stream was closed while the parent channel is still active
            Http3CodecUtils.ConnectionError(
                    ctx, Http3ErrorCode.H3_CLOSED_CRITICAL_STREAM, "Critical stream closed.", false);
        }
    }

    /**
     * A connection-error should be handled as defined in the HTTP3 spec.
     * @param ctx           the {@link ChannelHandlerContext} of the handle that handles it.
     * @param exception     the {@link Http3Exception} that caused the error.
     * @param fireException {@code true} if we should also fire the {@link Http3Exception} through the pipeline.
     */
    internal static void ConnectionError(IChannelHandlerContext ctx, Http3Exception exception, bool fireException)
    {
        if (fireException)
        {
            ctx.FireExceptionCaught(exception);
        }
        ConnectionError(ctx.Channel, exception.ErrorCode(), exception.Message);
    }

    /**
     * A connection-error should be handled as defined in the HTTP3 spec.
     *
     * @param ctx           the {@link ChannelHandlerContext} of the handle that handles it.
     * @param errorCode     the {@link Http3ErrorCode} that caused the error.
     * @param msg           the message that should be used as reason for the error, may be {@code null}.
     * @param fireException {@code true} if we should also fire the {@link Http3Exception} through the pipeline.
     */
    internal static void ConnectionError(IChannelHandlerContext ctx, Http3ErrorCode errorCode,
                                         string msg, bool fireException)
    {
         if (fireException)
         {
             ctx.FireExceptionCaught(new Http3Exception(errorCode, msg));
         }
         ConnectionError(ctx.Channel, errorCode, msg);
    }

    /**
     * Closes the channel if the passed {@link ChannelFuture} fails or has already failed.
     *
     * @param future {@link ChannelFuture} which if fails will close the channel.
     */
    // CLR: channel is the channel of the future (see the type comment). The listener is a synchronous continuation, as
    // Channel.close() may be called from any thread.
    internal static void CloseOnFailure(IChannel channel, Task future)
    {
        if (future.IsCompleted && !future.IsCompletedSuccessfully)
        {
            _ = channel.CloseAsync();
            return;
        }
        _ = future.ContinueWith(static (t, state) =>
        {
            if (!t.IsCompletedSuccessfully)
            {
                _ = ((IChannel) state).CloseAsync();
            }
        }, channel, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /**
     * A connection-error should be handled as defined in the HTTP3 spec.
     *
     * @param channel       the {@link Channel} on which error has occurred.
     * @param errorCode     the {@link Http3ErrorCode} that caused the error.
     * @param msg           the message that should be used as reason for the error, may be {@code null}.
     */
    internal static void ConnectionError(IChannel channel, Http3ErrorCode errorCode, string msg)
    {
        IQuicChannel quicChannel;

        if (channel is IQuicChannel)
        {
            quicChannel = (IQuicChannel) channel;
        }
        else
        {
            quicChannel = (IQuicChannel) channel.Parent;
        }
        ByteBuf buffer;
        if (msg != null)
        {
            // As we call an operation on the parent we should also use the parents allocator to allocate the buffer.
            buffer = quicChannel.Alloc().Buffer();
            buffer.WriteCharSequence(msg, Encoding.ASCII);
        }
        else
        {
            buffer = Unpooled.EmptyBuffer;
        }
        _ = quicChannel.CloseAsync(true, errorCode.code, buffer);
    }

    internal static void StreamError(IChannelHandlerContext ctx, Http3ErrorCode errorCode)
    {
        _ = ((IQuicStreamChannel) ctx.Channel).ShutdownOutputAsync(errorCode.code);
    }

    internal static void ReadIfNoAutoRead(IChannelHandlerContext ctx)
    {
        if (!ctx.Channel.Config.IsAutoRead())
        {
            ctx.Read();
        }
    }

    /**
     * Retrieves {@link Http3ConnectionHandler} from the passed {@link QuicChannel} pipeline or closes the connection if
     * none available.
     *
     * @param ch for which the {@link Http3ConnectionHandler} is to be retrieved.
     * @return {@link Http3ConnectionHandler} if available, else close the connection and return {@code null}.
     */
    internal static Http3ConnectionHandler GetConnectionHandlerOrClose(IQuicChannel ch)
    {
        Http3ConnectionHandler connectionHandler = ch.Pipeline.Get<Http3ConnectionHandler>();
        if (connectionHandler == null)
        {
            ConnectionError(ch, H3_INTERNAL_ERROR, "Couldn't obtain the " +
                    StringUtil.SimpleClassName(typeof(Http3ConnectionHandler)) + " of the parent Channel");
            return null;
        }
        return connectionHandler;
    }

    /**
     * Verify if the passed {@link QuicStreamChannel} is a {@link QuicStreamType#UNIDIRECTIONAL} QUIC stream.
     *
     * @param ch to verify
     * @throws IllegalArgumentException if the passed {@link QuicStreamChannel} is not a
     * {@link QuicStreamType#UNIDIRECTIONAL} QUIC stream.
     */
    internal static void VerifyIsUnidirectional(IQuicStreamChannel ch)
    {
        if (ch.Type() != QuicStreamType.UNIDIRECTIONAL)
        {
            throw new ArgumentException("Invalid stream type: " + ch.Type() + " for stream: " +
                    ch.StreamId().ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    // CLR: ctx.newPromise(): a TaskCompletionSource without RunContinuationsAsynchronously, so a listener added with
    // ExecuteSynchronously runs when the promise completes (docs/codec-http2-porting.md, Listener timing).
    internal static TaskCompletionSource NewPromise()
    {
        return new TaskCompletionSource();
    }

    // CLR: ctx.write(msg, promise): the promise completes with the outcome of the context's write Task (TrySetFromTask,
    // the PromiseNotifier mapping of docs/common-task-composition.md); returns the promise's Task, as Java returns the
    // promise.
    internal static Task Write(IChannelHandlerContext ctx, object msg, TaskCompletionSource promise)
    {
        Cascade(ctx, ctx.WriteAsync(msg), promise);
        return promise.Task;
    }

    // CLR: ctx.writeAndFlush(msg, promise); see Write.
    internal static Task WriteAndFlush(IChannelHandlerContext ctx, object msg, TaskCompletionSource promise)
    {
        Cascade(ctx, ctx.WriteAndFlushAsync(msg), promise);
        return promise.Task;
    }

    // CLR: completes promise with the outcome of future (TrySetFromTask). A completed future is copied at once; otherwise
    // the copy is a listener of future (see AddListener), so it runs on the event loop when the future completes there.
    internal static void Cascade(IChannelHandlerContext ctx, Task future, TaskCompletionSource promise)
    {
        if (future.IsCompleted)
        {
            promise.TrySetFromTask(future);
            return;
        }
        _ = future.ContinueWith(static (t, state) => ((TaskCompletionSource) state).TrySetFromTask(t), promise,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => ctx.Executor));
    }

    // CLR: future.addListener(listener): a continuation on Common's EventExecutorTaskScheduler over ctx.Executor, which
    // runs it at once when the future completes on the event loop and otherwise hands it to the executor
    // (DefaultPromise.notifyListeners policy; docs/codec-http-porting.md, Listener timing). A listener added to a
    // completed future runs at once on the event loop, as Java notifies it from addListener.
    internal static void AddListener(IChannelHandlerContext ctx, Task future, Action<Task> listener)
    {
        _ = future.ContinueWith(listener, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => ctx.Executor));
    }

    // CLR: future.cause(): the exception of a faulted Task (the first inner exception of its AggregateException), a
    // TaskCanceledException for a canceled Task (Java's CancellationException) and null otherwise.
    internal static Exception Cause(Task future)
    {
        if (future.IsFaulted)
        {
            return future.Exception.InnerException;
        }
        if (future.IsCanceled)
        {
            return new TaskCanceledException(future);
        }
        return null;
    }
}
