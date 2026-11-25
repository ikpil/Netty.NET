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
using System.Collections.Generic;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Handler.Codec.Http2.Http2FrameTypes;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A {@link Http2FrameWriter} that supports all frame types defined by the HTTP/2 specification.
 */
// CLR: ChannelPromise is TaskCompletionSource and ChannelFuture is Task; the Task-returning methods take the Async
// suffix (docs/codec-http-porting.md, Shared CLR adaptations). ctx.write(msg, promise) is Http2CodecUtil.Write, which
// completes the promise from the context's write Task (TrySetFromTask, docs/common-task-composition.md), and
// promise.setFailure(t) returning the promise is SetException(t) followed by the promise's Task.
// SimpleChannelPromiseAggregator.newPromise() returns a child promise and doneAllocatingPromises()/the aggregator
// itself are used through their Task (see Http2CodecUtil). Configuration is IHttp2FrameWriter.IConfiguration and
// SensitivityDetector IHttp2HeadersEncoder.ISensitivityDetector. "headersEncoder instanceof Closeable" is a test for
// IDisposable, the counterpart of Closeable (docs/handler-porting.md: "Closeable keeps close() as Close() and
// implements IDisposable.Dispose() with it"), and new RuntimeException(e) keeps Java's detail message
// cause.toString() through Common's ThrowableUtil.ThrowableToString (docs/codec-http-porting.md, Exceptions).
// PlatformDependent.throwException(t) rethrows the caught exception (throw;). Http2Settings is a
// Dictionary<char, long>, so the SETTINGS entries are written in Dictionary (insertion) order where Java writes CharObjectHashMap's hash-slot
// order (owner decision 2026-11-25; a SETTINGS frame holds each identifier once); entry.value().intValue() is the
// unchecked int conversion of the long. (int) (0x80000000L | streamDependency) is computed with an explicit long
// conversion of the (non-negative, verified) dependency and an unchecked narrowing, as Java's int widening and
// narrowing do. The overridable Java methods are virtual.
public class DefaultHttp2FrameWriter : IHttp2FrameWriter, IHttp2FrameSizePolicy, IHttp2FrameWriter.IConfiguration
{
    private const string STREAM_ID = "Stream ID";
    private const string STREAM_DEPENDENCY = "Stream Dependency";
    /**
     * This buffer is allocated to the maximum size of the padding field, and filled with zeros.
     * When padding is needed it can be taken as a slice of this buffer. Users should call {@link ByteBuf#retain()}
     * before using their slice.
     */
    private static readonly ByteBuf ZERO_BUFFER = LeakPresenceDetector.StaticInitializer(() =>
            UnreleasableBuffer(DirectBuffer(MAX_UNSIGNED_BYTE).WriteZero(MAX_UNSIGNED_BYTE)).AsReadOnly());

    private readonly IHttp2HeadersEncoder headersEncoder;
    private int maxFrameSize;

    public DefaultHttp2FrameWriter() : this(new DefaultHttp2HeadersEncoder())
    {
    }

    public DefaultHttp2FrameWriter(IHttp2HeadersEncoder.ISensitivityDetector headersSensitivityDetector)
        : this(new DefaultHttp2HeadersEncoder(headersSensitivityDetector))
    {
    }

    public DefaultHttp2FrameWriter(IHttp2HeadersEncoder.ISensitivityDetector headersSensitivityDetector,
        bool ignoreMaxHeaderListSize)
        : this(new DefaultHttp2HeadersEncoder(headersSensitivityDetector, ignoreMaxHeaderListSize))
    {
    }

    /**
     * @param headersEncoder will be closed if it implements {@link Closeable}
     */
    public DefaultHttp2FrameWriter(IHttp2HeadersEncoder headersEncoder)
    {
        this.headersEncoder = headersEncoder;
        maxFrameSize = DEFAULT_MAX_FRAME_SIZE;
    }

    public virtual IHttp2FrameWriter.IConfiguration Configuration()
    {
        return this;
    }

    public virtual IHttp2HeadersEncoder.IConfiguration HeadersConfiguration()
    {
        return headersEncoder.Configuration();
    }

    public virtual IHttp2FrameSizePolicy FrameSizePolicy()
    {
        return this;
    }

    public virtual void MaxFrameSize(int max)
    {
        if (!IsMaxFrameSizeValid(max))
        {
            throw ConnectionError(FRAME_SIZE_ERROR, "Invalid MAX_FRAME_SIZE specified in sent settings: %d", max);
        }
        maxFrameSize = max;
    }

    public virtual int MaxFrameSize()
    {
        return maxFrameSize;
    }

    public virtual void Close()
    {
        if (headersEncoder is IDisposable)
        {
            try
            {
                ((IDisposable) headersEncoder).Dispose();
            }
            catch (Exception e)
            {
                throw new Exception(ThrowableUtil.ThrowableToString(e), e);
            }
        }
    }

    public virtual Task WriteDataAsync(IChannelHandlerContext ctx, int streamId, ByteBuf data,
            int padding, bool endStream, TaskCompletionSource promise)
    {
        SimpleChannelPromiseAggregator promiseAggregator =
                new SimpleChannelPromiseAggregator(promise, ctx.Channel, ctx.Executor);
        ByteBuf frameHeader = null;
        try
        {
            VerifyStreamId(streamId, STREAM_ID);
            VerifyPadding(padding);

            int remainingData = data.ReadableBytes;
            Http2Flags flags = new Http2Flags();
            flags.EndOfStream(false);
            flags.PaddingPresent(false);
            // Fast path to write frames of payload size maxFrameSize first.
            if (remainingData > maxFrameSize)
            {
                frameHeader = ctx.Alloc().Buffer(FRAME_HEADER_LENGTH);
                WriteFrameHeaderInternal(frameHeader, maxFrameSize, DATA, flags, streamId);
                do
                {
                    // Write the header.
                    Write(ctx, frameHeader.RetainedSlice(), promiseAggregator.NewPromise());

                    // Write the payload.
                    Write(ctx, data.ReadRetainedSlice(maxFrameSize), promiseAggregator.NewPromise());

                    remainingData -= maxFrameSize;
                    // Stop iterating if remainingData == maxFrameSize so we can take care of reference counts below.
                } while (remainingData > maxFrameSize);
            }

            if (padding == 0)
            {
                // Write the header.
                if (frameHeader != null)
                {
                    frameHeader.Release();
                    frameHeader = null;
                }
                ByteBuf frameHeader2 = ctx.Alloc().Buffer(FRAME_HEADER_LENGTH);
                flags.EndOfStream(endStream);
                WriteFrameHeaderInternal(frameHeader2, remainingData, DATA, flags, streamId);
                Write(ctx, frameHeader2, promiseAggregator.NewPromise());

                // Write the payload.
                ByteBuf lastFrame = data.ReadSlice(remainingData);
                data = null;
                Write(ctx, lastFrame, promiseAggregator.NewPromise());
            }
            else
            {
                if (remainingData != maxFrameSize)
                {
                    if (frameHeader != null)
                    {
                        frameHeader.Release();
                        frameHeader = null;
                    }
                }
                else
                {
                    remainingData -= maxFrameSize;
                    // Write the header.
                    ByteBuf lastFrame;
                    if (frameHeader == null)
                    {
                        lastFrame = ctx.Alloc().Buffer(FRAME_HEADER_LENGTH);
                        WriteFrameHeaderInternal(lastFrame, maxFrameSize, DATA, flags, streamId);
                    }
                    else
                    {
                        lastFrame = frameHeader.Slice();
                        frameHeader = null;
                    }
                    Write(ctx, lastFrame, promiseAggregator.NewPromise());

                    // Write the payload.
                    lastFrame = data.ReadableBytes != maxFrameSize ? data.ReadSlice(maxFrameSize) : data;
                    data = null;
                    Write(ctx, lastFrame, promiseAggregator.NewPromise());
                }

                do
                {
                    int frameDataBytes = Math.Min(remainingData, maxFrameSize);
                    int framePaddingBytes = Math.Min(padding, Math.Max(0, maxFrameSize - 1 - frameDataBytes));

                    // Decrement the remaining counters.
                    padding -= framePaddingBytes;
                    remainingData -= frameDataBytes;

                    // Write the header.
                    ByteBuf frameHeader2 = ctx.Alloc().Buffer(DATA_FRAME_HEADER_LENGTH);
                    flags.EndOfStream(endStream && remainingData == 0 && padding == 0);
                    flags.PaddingPresent(framePaddingBytes > 0);
                    WriteFrameHeaderInternal(frameHeader2, framePaddingBytes + frameDataBytes, DATA, flags, streamId);
                    WritePaddingLength(frameHeader2, framePaddingBytes);
                    Write(ctx, frameHeader2, promiseAggregator.NewPromise());

                    // Write the payload.
                    if (data != null)
                    { // Make sure Data is not null
                        if (remainingData == 0)
                        {
                            ByteBuf lastFrame = data.ReadSlice(frameDataBytes);
                            data = null;
                            Write(ctx, lastFrame, promiseAggregator.NewPromise());
                        }
                        else
                        {
                            Write(ctx, data.ReadRetainedSlice(frameDataBytes), promiseAggregator.NewPromise());
                        }
                    }
                    // Write the frame padding.
                    if (PaddingBytes(framePaddingBytes) > 0)
                    {
                        Write(ctx, ZERO_BUFFER.Slice(0, PaddingBytes(framePaddingBytes)),
                                  promiseAggregator.NewPromise());
                    }
                } while (remainingData != 0 || padding != 0);
            }
        }
        catch (Exception cause)
        {
            if (frameHeader != null)
            {
                frameHeader.Release();
            }
            // Use a try/finally here in case the data has been released before calling this method. This is not
            // necessary above because we internally allocate frameHeader.
            try
            {
                if (data != null)
                {
                    data.Release();
                }
            }
            finally
            {
                promiseAggregator.SetFailure(cause);
                promiseAggregator.DoneAllocatingPromises();
            }
            return promiseAggregator.Task;
        }
        return promiseAggregator.DoneAllocatingPromises().Task;
    }

    public virtual Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId,
            IHttp2Headers headers, int padding, bool endStream, TaskCompletionSource promise)
    {
        return WriteHeadersInternal(ctx, streamId, headers, padding, endStream,
                false, 0, (short) 0, false, promise);
    }

    public virtual Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId,
            IHttp2Headers headers, int streamDependency, short weight, bool exclusive,
            int padding, bool endStream, TaskCompletionSource promise)
    {
        return WriteHeadersInternal(ctx, streamId, headers, padding, endStream,
                true, streamDependency, weight, exclusive, promise);
    }

    public virtual Task WritePriorityAsync(IChannelHandlerContext ctx, int streamId,
            int streamDependency, short weight, bool exclusive, TaskCompletionSource promise)
    {
        try
        {
            VerifyStreamId(streamId, STREAM_ID);
            VerifyStreamOrConnectionId(streamDependency, STREAM_DEPENDENCY);
            VerifyWeight(weight);

            ByteBuf buf = ctx.Alloc().Buffer(PRIORITY_FRAME_LENGTH);
            WriteFrameHeaderInternal(buf, PRIORITY_ENTRY_LENGTH, PRIORITY, new Http2Flags(), streamId);
            buf.WriteInt(exclusive ? unchecked((int) (0x80000000L | (long) streamDependency)) : streamDependency);
            // Adjust the weight so that it fits into a single byte on the wire.
            buf.WriteByte(weight - 1);
            return Write(ctx, buf, promise);
        }
        catch (Exception t)
        {
            promise.SetException(t);
            return promise.Task;
        }
    }

    public virtual Task WriteRstStreamAsync(IChannelHandlerContext ctx, int streamId, long errorCode,
            TaskCompletionSource promise)
    {
        try
        {
            VerifyStreamId(streamId, STREAM_ID);
            VerifyErrorCode(errorCode);

            ByteBuf buf = ctx.Alloc().Buffer(RST_STREAM_FRAME_LENGTH);
            WriteFrameHeaderInternal(buf, INT_FIELD_LENGTH, RST_STREAM, new Http2Flags(), streamId);
            buf.WriteInt(unchecked((int) errorCode));
            return Write(ctx, buf, promise);
        }
        catch (Exception t)
        {
            promise.SetException(t);
            return promise.Task;
        }
    }

    public virtual Task WriteSettingsAsync(IChannelHandlerContext ctx, Http2Settings settings,
            TaskCompletionSource promise)
    {
        try
        {
            CheckNotNull(settings, "settings");
            int payloadLength = SETTING_ENTRY_LENGTH * settings.Count;
            ByteBuf buf = ctx.Alloc().Buffer(FRAME_HEADER_LENGTH + payloadLength);
            WriteFrameHeaderInternal(buf, payloadLength, SETTINGS, new Http2Flags(), 0);
            foreach (KeyValuePair<char, long> entry in settings)
            {
                buf.WriteChar(entry.Key);
                buf.WriteInt(unchecked((int) entry.Value));
            }
            return Write(ctx, buf, promise);
        }
        catch (Exception t)
        {
            promise.SetException(t);
            return promise.Task;
        }
    }

    public virtual Task WriteSettingsAckAsync(IChannelHandlerContext ctx, TaskCompletionSource promise)
    {
        try
        {
            ByteBuf buf = ctx.Alloc().Buffer(FRAME_HEADER_LENGTH);
            WriteFrameHeaderInternal(buf, 0, SETTINGS, new Http2Flags().Ack(true), 0);
            return Write(ctx, buf, promise);
        }
        catch (Exception t)
        {
            promise.SetException(t);
            return promise.Task;
        }
    }

    public virtual Task WritePingAsync(IChannelHandlerContext ctx, bool ack, long data, TaskCompletionSource promise)
    {
        Http2Flags flags = ack ? new Http2Flags().Ack(true) : new Http2Flags();
        ByteBuf buf = ctx.Alloc().Buffer(FRAME_HEADER_LENGTH + PING_FRAME_PAYLOAD_LENGTH);
        // Assume nothing below will throw until buf is written. That way we don't have to take care of ownership
        // in the catch block.
        WriteFrameHeaderInternal(buf, PING_FRAME_PAYLOAD_LENGTH, PING, flags, 0);
        buf.WriteLong(data);
        return Write(ctx, buf, promise);
    }

    public virtual Task WritePushPromiseAsync(IChannelHandlerContext ctx, int streamId,
            int promisedStreamId, IHttp2Headers headers, int padding, TaskCompletionSource promise)
    {
        ByteBuf headerBlock = null;
        ByteBuf fragment = null;
        SimpleChannelPromiseAggregator promiseAggregator =
                new SimpleChannelPromiseAggregator(promise, ctx.Channel, ctx.Executor);
        try
        {
            VerifyStreamId(streamId, STREAM_ID);
            VerifyStreamId(promisedStreamId, "Promised Stream ID");
            VerifyPadding(padding);

            // Encode the entire header block into an intermediate buffer.
            headerBlock = ctx.Alloc().Buffer();
            headersEncoder.EncodeHeaders(streamId, headers, headerBlock);

            // Read the first fragment (possibly everything).
            Http2Flags flags = new Http2Flags().PaddingPresent(padding > 0);
            // INT_FIELD_LENGTH is for the length of the promisedStreamId
            int nonFragmentLength = INT_FIELD_LENGTH + padding;
            int maxFragmentLength = maxFrameSize - nonFragmentLength;
            fragment = headerBlock.ReadRetainedSlice(Math.Min(headerBlock.ReadableBytes, maxFragmentLength));

            flags.EndOfHeaders(!headerBlock.IsReadable());

            int payloadLength = fragment.ReadableBytes + nonFragmentLength;
            ByteBuf buf = ctx.Alloc().Buffer(PUSH_PROMISE_FRAME_HEADER_LENGTH);
            WriteFrameHeaderInternal(buf, payloadLength, PUSH_PROMISE, flags, streamId);
            WritePaddingLength(buf, padding);

            // Write out the promised stream ID.
            buf.WriteInt(promisedStreamId);
            Write(ctx, buf, promiseAggregator.NewPromise());

            // Write the first fragment.
            Write(ctx, fragment, promiseAggregator.NewPromise());
            fragment = null;

            // Write out the padding, if any.
            if (PaddingBytes(padding) > 0)
            {
                Write(ctx, ZERO_BUFFER.Slice(0, PaddingBytes(padding)), promiseAggregator.NewPromise());
            }

            if (!flags.EndOfHeaders())
            {
                WriteContinuationFrames(ctx, streamId, headerBlock, promiseAggregator);
            }
        }
        catch (Http2Exception e)
        {
            promiseAggregator.SetFailure(e);
        }
        catch (Exception t)
        {
            promiseAggregator.SetFailure(t);
            promiseAggregator.DoneAllocatingPromises();
            throw;
        }
        finally
        {
            if (fragment != null)
            {
                fragment.Release();
            }
            if (headerBlock != null)
            {
                headerBlock.Release();
            }
        }
        return promiseAggregator.DoneAllocatingPromises().Task;
    }

    public virtual Task WriteGoAwayAsync(IChannelHandlerContext ctx, int lastStreamId, long errorCode,
            ByteBuf debugData, TaskCompletionSource promise)
    {
        SimpleChannelPromiseAggregator promiseAggregator =
                new SimpleChannelPromiseAggregator(promise, ctx.Channel, ctx.Executor);
        try
        {
            VerifyStreamOrConnectionId(lastStreamId, "Last Stream ID");
            VerifyErrorCode(errorCode);

            int payloadLength = 8 + debugData.ReadableBytes;
            ByteBuf buf = ctx.Alloc().Buffer(GO_AWAY_FRAME_HEADER_LENGTH);
            // Assume nothing below will throw until buf is written. That way we don't have to take care of ownership
            // in the catch block.
            WriteFrameHeaderInternal(buf, payloadLength, GO_AWAY, new Http2Flags(), 0);
            buf.WriteInt(lastStreamId);
            buf.WriteInt(unchecked((int) errorCode));
            Write(ctx, buf, promiseAggregator.NewPromise());
        }
        catch (Exception t)
        {
            try
            {
                debugData.Release();
            }
            finally
            {
                promiseAggregator.SetFailure(t);
                promiseAggregator.DoneAllocatingPromises();
            }
            return promiseAggregator.Task;
        }

        try
        {
            Write(ctx, debugData, promiseAggregator.NewPromise());
        }
        catch (Exception t)
        {
            promiseAggregator.SetFailure(t);
        }
        return promiseAggregator.DoneAllocatingPromises().Task;
    }

    public virtual Task WriteWindowUpdateAsync(IChannelHandlerContext ctx, int streamId,
            int windowSizeIncrement, TaskCompletionSource promise)
    {
        try
        {
            VerifyStreamOrConnectionId(streamId, STREAM_ID);
            VerifyWindowSizeIncrement(windowSizeIncrement);

            ByteBuf buf = ctx.Alloc().Buffer(WINDOW_UPDATE_FRAME_LENGTH);
            WriteFrameHeaderInternal(buf, INT_FIELD_LENGTH, WINDOW_UPDATE, new Http2Flags(), streamId);
            buf.WriteInt(windowSizeIncrement);
            return Write(ctx, buf, promise);
        }
        catch (Exception t)
        {
            promise.SetException(t);
            return promise.Task;
        }
    }

    public virtual Task WriteFrameAsync(IChannelHandlerContext ctx, byte frameType, int streamId,
            Http2Flags flags, ByteBuf payload, TaskCompletionSource promise)
    {
        SimpleChannelPromiseAggregator promiseAggregator =
                new SimpleChannelPromiseAggregator(promise, ctx.Channel, ctx.Executor);
        try
        {
            VerifyStreamOrConnectionId(streamId, STREAM_ID);
            ByteBuf buf = ctx.Alloc().Buffer(FRAME_HEADER_LENGTH);
            // Assume nothing below will throw until buf is written. That way we don't have to take care of ownership
            // in the catch block.
            WriteFrameHeaderInternal(buf, payload.ReadableBytes, frameType, flags, streamId);
            Write(ctx, buf, promiseAggregator.NewPromise());
        }
        catch (Exception t)
        {
            try
            {
                payload.Release();
            }
            finally
            {
                promiseAggregator.SetFailure(t);
                promiseAggregator.DoneAllocatingPromises();
            }
            return promiseAggregator.Task;
        }
        try
        {
            Write(ctx, payload, promiseAggregator.NewPromise());
        }
        catch (Exception t)
        {
            promiseAggregator.SetFailure(t);
        }
        return promiseAggregator.DoneAllocatingPromises().Task;
    }

    private Task WriteHeadersInternal(IChannelHandlerContext ctx,
            int streamId, IHttp2Headers headers, int padding, bool endStream,
            bool hasPriority, int streamDependency, short weight, bool exclusive, TaskCompletionSource promise)
    {
        ByteBuf headerBlock = null;
        ByteBuf fragment = null;
        SimpleChannelPromiseAggregator promiseAggregator =
                new SimpleChannelPromiseAggregator(promise, ctx.Channel, ctx.Executor);
        try
        {
            VerifyStreamId(streamId, STREAM_ID);
            if (hasPriority)
            {
                VerifyStreamOrConnectionId(streamDependency, STREAM_DEPENDENCY);
                VerifyPadding(padding);
                VerifyWeight(weight);
            }

            // Encode the entire header block.
            headerBlock = ctx.Alloc().Buffer();
            headersEncoder.EncodeHeaders(streamId, headers, headerBlock);

            Http2Flags flags =
                    new Http2Flags().EndOfStream(endStream).PriorityPresent(hasPriority).PaddingPresent(padding > 0);

            // Read the first fragment (possibly everything).
            int nonFragmentBytes = padding + flags.GetNumPriorityBytes();
            int maxFragmentLength = maxFrameSize - nonFragmentBytes;
            fragment = headerBlock.ReadRetainedSlice(Math.Min(headerBlock.ReadableBytes, maxFragmentLength));

            // Set the end of headers flag for the first frame.
            flags.EndOfHeaders(!headerBlock.IsReadable());

            int payloadLength = fragment.ReadableBytes + nonFragmentBytes;
            ByteBuf buf = ctx.Alloc().Buffer(HEADERS_FRAME_HEADER_LENGTH);
            WriteFrameHeaderInternal(buf, payloadLength, HEADERS, flags, streamId);
            WritePaddingLength(buf, padding);

            if (hasPriority)
            {
                buf.WriteInt(exclusive ? unchecked((int) (0x80000000L | (long) streamDependency)) : streamDependency);

                // Adjust the weight so that it fits into a single byte on the wire.
                buf.WriteByte(weight - 1);
            }
            Write(ctx, buf, promiseAggregator.NewPromise());

            // Write the first fragment.
            Write(ctx, fragment, promiseAggregator.NewPromise());
            fragment = null;

            // Write out the padding, if any.
            if (PaddingBytes(padding) > 0)
            {
                Write(ctx, ZERO_BUFFER.Slice(0, PaddingBytes(padding)), promiseAggregator.NewPromise());
            }

            if (!flags.EndOfHeaders())
            {
                WriteContinuationFrames(ctx, streamId, headerBlock, promiseAggregator);
            }
        }
        catch (Http2Exception e)
        {
            promiseAggregator.SetFailure(e);
        }
        catch (Exception t)
        {
            promiseAggregator.SetFailure(t);
            promiseAggregator.DoneAllocatingPromises();
            throw;
        }
        finally
        {
            if (fragment != null)
            {
                fragment.Release();
            }
            if (headerBlock != null)
            {
                headerBlock.Release();
            }
        }
        return promiseAggregator.DoneAllocatingPromises().Task;
    }

    /**
     * Writes as many continuation frames as needed until {@code padding} and {@code headerBlock} are consumed.
     */
    private Task WriteContinuationFrames(IChannelHandlerContext ctx, int streamId,
            ByteBuf headerBlock, SimpleChannelPromiseAggregator promiseAggregator)
    {
        Http2Flags flags = new Http2Flags();

        if (headerBlock.IsReadable())
        {
            int fragmentReadableBytes;
            ByteBuf buf = null;
            try
            {
                do
                {
                    fragmentReadableBytes = Math.Min(headerBlock.ReadableBytes, maxFrameSize);
                    ByteBuf fragment = headerBlock.ReadRetainedSlice(fragmentReadableBytes);
                    bool fragmentWritten = false;
                    try
                    {
                        if (headerBlock.IsReadable())
                        {
                            if (buf == null)
                            {
                                buf = ctx.Alloc().Buffer(CONTINUATION_FRAME_HEADER_LENGTH);
                                WriteFrameHeaderInternal(buf, fragmentReadableBytes, CONTINUATION, flags, streamId);
                            }
                            Write(ctx, buf.RetainedSlice(), promiseAggregator.NewPromise());
                        }
                        else
                        {
                            // The frame header is different for the last frame, so re-allocate and release
                            // the old buffer
                            if (buf != null)
                            {
                                buf.Release();
                                buf = null;
                            }
                            flags = flags.EndOfHeaders(true);
                            buf = ctx.Alloc().Buffer(CONTINUATION_FRAME_HEADER_LENGTH);
                            WriteFrameHeaderInternal(buf, fragmentReadableBytes, CONTINUATION, flags, streamId);
                            Write(ctx, buf, promiseAggregator.NewPromise());
                            buf = null;
                        }
                        Write(ctx, fragment, promiseAggregator.NewPromise());
                        fragmentWritten = true;
                    }
                    finally
                    {
                        if (!fragmentWritten)
                        {
                            fragment.Release();
                        }
                    }
                } while (headerBlock.IsReadable());
            }
            finally
            {
                if (buf != null)
                {
                    buf.Release();
                }
            }
        }
        return promiseAggregator.Task;
    }

    /**
     * Returns the number of padding bytes that should be appended to the end of a frame.
     */
    private static int PaddingBytes(int padding)
    {
        // The padding parameter contains the 1 byte pad length field as well as the trailing padding bytes.
        // Subtract 1, so to only get the number of padding bytes that need to be appended to the end of a frame.
        return padding - 1;
    }

    private static void WritePaddingLength(ByteBuf buf, int padding)
    {
        if (padding > 0)
        {
            // It is assumed that the padding length has been bounds checked before this
            // Minus 1, as the pad length field is included in the padding parameter and is 1 byte wide.
            buf.WriteByte(padding - 1);
        }
    }

    private static void VerifyStreamId(int streamId, string argumentName)
    {
        CheckPositive(streamId, argumentName);
    }

    private static void VerifyStreamOrConnectionId(int streamId, string argumentName)
    {
        CheckPositiveOrZero(streamId, argumentName);
    }

    private static void VerifyWeight(short weight)
    {
        if (weight < MIN_WEIGHT || weight > MAX_WEIGHT)
        {
            throw new ArgumentException("Invalid weight: " + ToJavaString(weight));
        }
    }

    private static void VerifyErrorCode(long errorCode)
    {
        if (errorCode < 0 || errorCode > MAX_UNSIGNED_INT)
        {
            throw new ArgumentException("Invalid errorCode: " + ToJavaString(errorCode));
        }
    }

    private static void VerifyWindowSizeIncrement(int windowSizeIncrement)
    {
        CheckPositiveOrZero(windowSizeIncrement, "windowSizeIncrement");
    }
}
