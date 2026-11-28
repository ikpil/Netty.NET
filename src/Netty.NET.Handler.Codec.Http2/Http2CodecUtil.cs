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
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Constants and utility method used for encoding/decoding HTTP2 frames.
 */
// CLR: the final class with a private constructor is a static class. The String constants that Java types as
// CharSequence are StringCharSequence values (a C# string is not an ICharSequence; docs/codec-http-porting.md,
// Shared CLR adaptations). MILLISECONDS.convert(30, SECONDS) is TimeSpan.FromSeconds(30) in milliseconds.
// ChannelPromise is TaskCompletionSource and ChannelFuture is Task. The members after verifyPadding (Format,
// ToJavaString, NewPromise, Write, WriteAndFlush, Close, Cascade, AddListener and Cause) are CLR-only internal
// helpers of the package: they keep the Java text of String.format/String.valueOf and the timing of ctx.write(msg,
// promise)/future.addListener(...) (see their comments).
public static class Http2CodecUtil
{
    public const int CONNECTION_STREAM_ID = 0;
    public const int HTTP_UPGRADE_STREAM_ID = 1;
    public static readonly ICharSequence HTTP_UPGRADE_SETTINGS_HEADER = AsciiString.Cached("HTTP2-Settings");
    public static readonly ICharSequence HTTP_UPGRADE_PROTOCOL_NAME = new StringCharSequence("h2c");
    public static readonly ICharSequence TLS_UPGRADE_PROTOCOL_NAME =
        new StringCharSequence(ApplicationProtocolNames.HTTP_2);

    public const int PING_FRAME_PAYLOAD_LENGTH = 8;
    public const short MAX_UNSIGNED_BYTE = 0xff;
    /**
     * The maximum number of padding bytes. That is the 255 padding bytes appended to the end of a frame and the 1 byte
     * pad length field.
     */
    public const int MAX_PADDING = 256;
    public const long MAX_UNSIGNED_INT = 0xffffffffL;
    public const int FRAME_HEADER_LENGTH = 9;
    public const int SETTING_ENTRY_LENGTH = 6;
    public const int PRIORITY_ENTRY_LENGTH = 5;
    public const int INT_FIELD_LENGTH = 4;
    public const short MAX_WEIGHT = 256;
    public const short MIN_WEIGHT = 1;

    private static readonly ByteBuf CONNECTION_PREFACE = LeakPresenceDetector.StaticInitializer(() =>
            UnreleasableBuffer(DirectBuffer(24).WriteBytes(Encoding.UTF8.GetBytes("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n")))
                    .AsReadOnly());

    private const int MAX_PADDING_LENGTH_LENGTH = 1;
    public const int DATA_FRAME_HEADER_LENGTH = FRAME_HEADER_LENGTH + MAX_PADDING_LENGTH_LENGTH;
    public const int HEADERS_FRAME_HEADER_LENGTH =
            FRAME_HEADER_LENGTH + MAX_PADDING_LENGTH_LENGTH + INT_FIELD_LENGTH + 1;
    public const int PRIORITY_FRAME_LENGTH = FRAME_HEADER_LENGTH + PRIORITY_ENTRY_LENGTH;
    public const int RST_STREAM_FRAME_LENGTH = FRAME_HEADER_LENGTH + INT_FIELD_LENGTH;
    public const int PUSH_PROMISE_FRAME_HEADER_LENGTH =
            FRAME_HEADER_LENGTH + MAX_PADDING_LENGTH_LENGTH + INT_FIELD_LENGTH;
    public const int GO_AWAY_FRAME_HEADER_LENGTH = FRAME_HEADER_LENGTH + 2 * INT_FIELD_LENGTH;
    public const int WINDOW_UPDATE_FRAME_LENGTH = FRAME_HEADER_LENGTH + INT_FIELD_LENGTH;
    public const int CONTINUATION_FRAME_HEADER_LENGTH = FRAME_HEADER_LENGTH;

    public const char SETTINGS_HEADER_TABLE_SIZE = (char) 1;
    public const char SETTINGS_ENABLE_PUSH = (char) 2;
    public const char SETTINGS_MAX_CONCURRENT_STREAMS = (char) 3;
    public const char SETTINGS_INITIAL_WINDOW_SIZE = (char) 4;
    public const char SETTINGS_MAX_FRAME_SIZE = (char) 5;
    public const char SETTINGS_MAX_HEADER_LIST_SIZE = (char) 6;
    public const char SETTINGS_ENABLE_CONNECT_PROTOCOL = (char) 8;
    public const int NUM_STANDARD_SETTINGS = 7;

    public const long MAX_HEADER_TABLE_SIZE = MAX_UNSIGNED_INT;
    public const long MAX_CONCURRENT_STREAMS = MAX_UNSIGNED_INT;
    public const int MAX_INITIAL_WINDOW_SIZE = int.MaxValue;
    public const int MAX_FRAME_SIZE_LOWER_BOUND = 0x4000;
    public const int MAX_FRAME_SIZE_UPPER_BOUND = 0xffffff;
    public const long MAX_HEADER_LIST_SIZE = MAX_UNSIGNED_INT;

    public const long MIN_HEADER_TABLE_SIZE = 0;
    public const long MIN_CONCURRENT_STREAMS = 0;
    public const int MIN_INITIAL_WINDOW_SIZE = 0;
    public const long MIN_HEADER_LIST_SIZE = 0;

    public const int DEFAULT_WINDOW_SIZE = 65535;
    public const short DEFAULT_PRIORITY_WEIGHT = 16;
    public const int DEFAULT_HEADER_TABLE_SIZE = 4096;
    /**
     * <a href="https://tools.ietf.org/html/rfc7540#section-6.5.2">The initial value of this setting is unlimited</a>.
     * However in practice we don't want to allow our peers to use unlimited memory by default. So we take advantage
     * of the <q>For any given request, a lower limit than what is advertised MAY be enforced.</q> loophole.
     */
    public const long DEFAULT_HEADER_LIST_SIZE = 8192;
    public const int DEFAULT_MAX_FRAME_SIZE = MAX_FRAME_SIZE_LOWER_BOUND;
    /**
     * The assumed minimum value for {@code SETTINGS_MAX_CONCURRENT_STREAMS} as
     * recommended by the <a href="https://tools.ietf.org/html/rfc7540#section-6.5.2">HTTP/2 spec</a>.
     */
    public const int SMALLEST_MAX_CONCURRENT_STREAMS = 100;
    internal const int DEFAULT_MAX_RESERVED_STREAMS = SMALLEST_MAX_CONCURRENT_STREAMS;
    internal const int DEFAULT_MIN_ALLOCATION_CHUNK = 1024;
    internal const int DEFAULT_MAX_SMALL_CONTINUATION_FRAME = 16;

    /**
     * While the RFC only specified a minimum we should still pick a default which is good enough that most people
     * no need to adjust it but still be somewhat protected. Let's use the minimum
     * defined by the <a href="https://tools.ietf.org/html/rfc7540#section-6.5.2">HTTP/2 spec</a>.
     */
    internal const int DEFAULT_MAX_CONCURRENT_STREAMS = SMALLEST_MAX_CONCURRENT_STREAMS;

    /**
     * Calculate the threshold in bytes which should trigger a {@code GO_AWAY} if a set of headers exceeds this amount.
     * @param maxHeaderListSize
     *      <a href="https://tools.ietf.org/html/rfc7540#section-6.5.2">SETTINGS_MAX_HEADER_LIST_SIZE</a> for the local
     *      endpoint.
     * @return the threshold in bytes which should trigger a {@code GO_AWAY} if a set of headers exceeds this amount.
     */
    public static long CalculateMaxHeaderListSizeGoAway(long maxHeaderListSize)
    {
        // This is equivalent to `maxHeaderListSize * 1.25` but we avoid floating point multiplication.
        return maxHeaderListSize + (maxHeaderListSize >>> 2);
    }

    public static readonly long DEFAULT_GRACEFUL_SHUTDOWN_TIMEOUT_MILLIS = (long) TimeSpan.FromSeconds(30).TotalMilliseconds;

    public const int DEFAULT_MAX_QUEUED_CONTROL_FRAMES = 10000;

    /**
     * Returns {@code true} if the stream is an outbound stream.
     *
     * @param server    {@code true} if the endpoint is a server, {@code false} otherwise.
     * @param streamId  the stream identifier
     */
    public static bool IsOutboundStream(bool server, int streamId)
    {
        bool even = (streamId & 1) == 0;
        return streamId > 0 && server == even;
    }

    /**
     * Returns true if the {@code streamId} is a valid HTTP/2 stream identifier.
     */
    public static bool IsStreamIdValid(int streamId)
    {
        return streamId >= 0;
    }

    internal static bool IsStreamIdValid(int streamId, bool server)
    {
        return IsStreamIdValid(streamId) && server == ((streamId & 1) == 0);
    }

    /**
     * Indicates whether or not the given value for max frame size falls within the valid range.
     */
    public static bool IsMaxFrameSizeValid(int maxFrameSize)
    {
        return maxFrameSize >= MAX_FRAME_SIZE_LOWER_BOUND && maxFrameSize <= MAX_FRAME_SIZE_UPPER_BOUND;
    }

    /**
     * Returns a buffer containing the {@link #CONNECTION_PREFACE}.
     */
    public static ByteBuf ConnectionPrefaceBuf()
    {
        // Return a duplicate so that modifications to the reader index will not affect the original buffer.
        return CONNECTION_PREFACE.RetainedDuplicate();
    }

    /**
     * Iteratively looks through the causality chain for the given exception and returns the first
     * {@link Http2Exception} or {@code null} if none.
     */
    public static Http2Exception GetEmbeddedHttp2Exception(Exception cause)
    {
        while (cause != null)
        {
            if (cause is Http2Exception)
            {
                return (Http2Exception) cause;
            }

            cause = cause.InnerException;
        }

        return null;
    }

    /**
     * Creates a buffer containing the error message from the given exception. If the cause is
     * {@code null} returns an empty buffer.
     */
    // CLR: getMessage() is the Message property, which is never null in .NET (an exception created without a message
    // reports the CLR default text), so the null check keeps its Java shape only (as codec-http Utf8FrameValidator).
    public static ByteBuf ToByteBuf(IChannelHandlerContext ctx, Exception cause)
    {
        if (cause == null || cause.Message == null)
        {
            return EmptyBuffer;
        }

        return ByteBufUtil.WriteUtf8(ctx.Alloc(), cause.Message);
    }

    /**
     * Reads a big-endian (31-bit) integer from the buffer.
     */
    public static int ReadUnsignedInt(ByteBuf buf)
    {
        return buf.ReadInt() & 0x7fffffff;
    }

    /**
     * Writes an HTTP/2 frame header to the output buffer.
     */
    public static void WriteFrameHeader(ByteBuf @out, int payloadLength, byte type,
            Http2Flags flags, int streamId)
    {
        @out.EnsureWritable(FRAME_HEADER_LENGTH + payloadLength);
        WriteFrameHeaderInternal(@out, payloadLength, type, flags, streamId);
    }

    /**
     * Calculate the amount of bytes that can be sent by {@code state}. The lower bound is {@code 0}.
     */
    public static int StreamableBytes(IStreamByteDistributor.IStreamState state)
    {
        return Math.Max(0, (int) Math.Min(state.PendingBytes(), state.WindowSize()));
    }

    /**
     * Results in a RST_STREAM being sent for {@code streamId} due to violating
     * <a href="https://tools.ietf.org/html/rfc7540#section-6.5.2">SETTINGS_MAX_HEADER_LIST_SIZE</a>.
     * @param streamId The stream ID that was being processed when the exceptional condition occurred.
     * @param maxHeaderListSize The max allowed size for a list of headers in bytes which was exceeded.
     * @param onDecode {@code true} if the exception was encountered during decoder. {@code false} for encode.
     * @throws Http2Exception a stream error.
     */
    public static void HeaderListSizeExceeded(int streamId, long maxHeaderListSize,
                                              bool onDecode)
    {
        throw HeaderListSizeError(streamId, PROTOCOL_ERROR, onDecode, "Header size exceeded max " +
                                  "allowed size (%d)", maxHeaderListSize);
    }

    /**
     * Results in a GO_AWAY being sent due to violating
     * <a href="https://tools.ietf.org/html/rfc7540#section-6.5.2">SETTINGS_MAX_HEADER_LIST_SIZE</a> in an unrecoverable
     * manner.
     * @param maxHeaderListSize The max allowed size for a list of headers in bytes which was exceeded.
     * @throws Http2Exception a connection error.
     */
    public static void HeaderListSizeExceeded(long maxHeaderListSize)
    {
        throw ConnectionError(PROTOCOL_ERROR, "Header size exceeded max " +
                "allowed size (%d)", maxHeaderListSize);
    }

    internal static void WriteFrameHeaderInternal(ByteBuf @out, int payloadLength, byte type,
            Http2Flags flags, int streamId)
    {
        @out.WriteMedium(payloadLength);
        @out.WriteByte(type);
        @out.WriteByte(flags.Value());
        @out.WriteInt(streamId);
    }

    /**
     * Provides the ability to associate the outcome of multiple {@link ChannelPromise}
     * objects into a single {@link ChannelPromise} object.
     */
    // CLR: DefaultChannelPromise is TaskCompletionSource, whose completion methods are not virtual, so the overridden
    // setSuccess/trySuccess/setFailure/tryFailure are this class's own SetSuccess/TrySuccess/SetFailure/TryFailure,
    // and newPromise() returns a new child promise instead of this: a synchronous continuation of the child's Task calls
    // TrySuccess() or TryFailure(cause) on the aggregator, which is what Java's channel does when it completes the
    // aggregator it was given. The child has no RunContinuationsAsynchronously, so the count is updated while the
    // child completes, as Java's overridden methods run. The Channel and EventExecutor parameters have no use (a
    // TaskCompletionSource has neither) and are kept for the Java signature. super.setSuccess(null)/setFailure(cause)
    // are SetResult()/SetException(cause) and super.trySuccess/tryFailure are TrySetResult()/TrySetException(cause).
    internal sealed class SimpleChannelPromiseAggregator : TaskCompletionSource
    {
        private readonly TaskCompletionSource promise;
        private int expectedCount;
        private int doneCount;
        private Exception aggregateFailure;
        private bool doneAllocating;

        internal SimpleChannelPromiseAggregator(TaskCompletionSource promise, IChannel c, IEventExecutor e)
        {
            Debug.Assert(promise != null && !promise.Task.IsCompleted);
            this.promise = promise;
        }

        /**
         * Allocate a new promise which will be used to aggregate the overall success of this promise aggregator.
         * @return A new promise which will be aggregated.
         * {@code null} if {@link #doneAllocatingPromises()} was previously called.
         */
        public TaskCompletionSource NewPromise()
        {
            Debug.Assert(!doneAllocating, "Done allocating. No more promises can be allocated.");
            ++expectedCount;
            TaskCompletionSource child = new TaskCompletionSource();
            _ = child.Task.ContinueWith(static (t, state) =>
            {
                SimpleChannelPromiseAggregator aggregator = (SimpleChannelPromiseAggregator) state;

                if (t.IsCompletedSuccessfully)
                {
                    aggregator.TrySuccess();
                }
                else
                {
                    aggregator.TryFailure(Cause(t));
                }
            }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return child;
        }

        /**
         * Signify that no more {@link #newPromise()} allocations will be made.
         * The aggregation can not be successful until this method is called.
         * @return The promise that is the aggregation of all promises allocated with {@link #newPromise()}.
         */
        public TaskCompletionSource DoneAllocatingPromises()
        {
            if (!doneAllocating)
            {
                doneAllocating = true;
                if (doneCount == expectedCount || expectedCount == 0)
                {
                    return SetPromise();
                }
            }

            return this;
        }

        public bool TryFailure(Exception cause)
        {
            if (AllowFailure())
            {
                ++doneCount;
                SetAggregateFailure(cause);
                if (AllPromisesDone())
                {
                    return TryPromise();
                }

                // TODO: We break the interface a bit here.
                // Multiple failure events can be processed without issue because this is an aggregation.
                return true;
            }

            return false;
        }

        /**
         * Fail this object if it has not already been failed.
         * <p>
         * This method will NOT throw an {@link IllegalStateException} if called multiple times
         * because that may be expected.
         */
        public TaskCompletionSource SetFailure(Exception cause)
        {
            if (AllowFailure())
            {
                ++doneCount;
                SetAggregateFailure(cause);
                if (AllPromisesDone())
                {
                    return SetPromise();
                }
            }

            return this;
        }

        public TaskCompletionSource SetSuccess()
        {
            if (AwaitingPromises())
            {
                ++doneCount;

                if (AllPromisesDone())
                {
                    SetPromise();
                }
            }

            return this;
        }

        public bool TrySuccess()
        {
            if (AwaitingPromises())
            {
                ++doneCount;
                if (AllPromisesDone())
                {
                    return TryPromise();
                }

                // TODO: We break the interface a bit here.
                // Multiple success events can be processed without issue because this is an aggregation.
                return true;
            }

            return false;
        }

        private bool AllowFailure()
        {
            return AwaitingPromises() || expectedCount == 0;
        }

        private bool AwaitingPromises()
        {
            return doneCount < expectedCount;
        }

        private bool AllPromisesDone()
        {
            return doneCount == expectedCount && doneAllocating;
        }

        private TaskCompletionSource SetPromise()
        {
            if (aggregateFailure == null)
            {
                promise.SetResult();
                SetResult();
                return this;
            }
            else
            {
                promise.SetException(aggregateFailure);
                SetException(aggregateFailure);
                return this;
            }
        }

        private bool TryPromise()
        {
            if (aggregateFailure == null)
            {
                promise.TrySetResult();

                return TrySetResult();
            }
            else
            {
                promise.TrySetException(aggregateFailure);

                return TrySetException(aggregateFailure);
            }
        }

        private void SetAggregateFailure(Exception cause)
        {
            if (aggregateFailure == null)
            {
                aggregateFailure = cause;
            }
        }
    }

    public static void VerifyPadding(int padding)
    {
        if (padding < 0 || padding > MAX_PADDING)
        {
            throw new ArgumentException(Format("Invalid padding '%d'. Padding must be between 0 and " +
                                                             "%d (inclusive).", padding, MAX_PADDING));
        }
    }

    // CLR: String.format(fmt, args) for the conversions this package uses
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Formatter.html): %s gives "null" for a
    // null argument and otherwise "the argument's toString()" (ToJavaString, so booleans are true/false), %d formats
    // "as a decimal integer" (with the invariant culture here, where Java applies the default locale's localization
    // algorithm; the integers in these messages have no grouping), %b gives "false" for null, the value of a Boolean
    // and "true" otherwise, and %% is "a literal '%'". A missing argument throws FormatException where Java throws
    // MissingFormatArgumentException.
    internal static string Format(string format, params object[] args)
    {
        StringBuilder sb = new StringBuilder(format.Length + 16);
        int argIndex = 0;

        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '%' && i + 1 < format.Length)
            {
                char conversion = format[i + 1];
                if (conversion == '%')
                {
                    sb.Append('%');
                    i++;
                    continue;
                }

                if (conversion == 'd' || conversion == 's' || conversion == 'b')
                {
                    if (args == null || argIndex >= args.Length)
                    {
                        throw new FormatException("Format specifier '%" + conversion + "'");
                    }

                    object arg = args[argIndex++];

                    if (conversion == 'b')
                    {
                        sb.Append(arg == null ? "false" : arg is bool b ? (b ? "true" : "false") : "true");
                    }
                    else
                    {
                        sb.Append(ToJavaString(arg));
                    }

                    i++;
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    // CLR: String.valueOf(Object) for the values this package formats or concatenates: null is "null", a bool is
    // true/false (Boolean.toString), IFormattable values (numbers) use the invariant culture, and anything else is
    // ToString().
    internal static string ToJavaString(object o)
    {
        if (o == null)
        {
            return "null";
        }
        if (o is bool b)
        {
            return b ? "true" : "false";
        }
        if (o is IFormattable f)
        {
            return f.ToString(null, CultureInfo.InvariantCulture);
        }

        return o.ToString();
    }

    // CLR: ctx.newPromise()/channel.newPromise(): a TaskCompletionSource without RunContinuationsAsynchronously,
    // so a listener added with ExecuteSynchronously runs when the promise completes, as DefaultPromise notifies at
    // once on the event loop (docs/codec-http-porting.md, Handshakers: channel.newPromise()). ctx.voidPromise() is
    // a new promise whose Task nobody observes, and promise.unvoid() returns the promise itself.
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

    // CLR: ctx.close(promise); see Write.
    internal static Task Close(IChannelHandlerContext ctx, TaskCompletionSource promise)
    {
        Cascade(ctx, ctx.CloseAsync(), promise);
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
