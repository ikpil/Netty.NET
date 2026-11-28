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
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Handler.Codec.Http2.Http2FrameTypes;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A {@link Http2FrameReader} that supports all frame types defined by the HTTP/2 specification.
 */
// CLR: Configuration is IHttp2FrameReader.IConfiguration. The inner (non-static) classes HeadersContinuation and
// HeadersBlockBuilder reach the reader's fields through an explicit reference to the reader (a C# nested class has
// no implicit outer instance), and the anonymous HeadersContinuation subclasses of readHeadersFrame and
// readPushPromiseFrame are the private nested PriorityHeadersContinuation, HeadersContinuationWithoutPriority and
// PushPromiseHeadersContinuation, which receive the values the Java closures capture. The frame type is a
// System.Byte, so the switches on it and the listener calls get the unsigned octet; where Java formats the signed
// byte into an exception message (%s of the frame type), the octet is passed as sbyte to keep Java's text for types
// 0x80-0xFF. readUnsignedByte() is the Buffer port's unsigned ReadByte(), writerIndex(int) is the WriterIndex property
// and readUnsignedInt() returns uint, widened to long as in Java. The readFrame catch clauses keep their order;
// RuntimeException and Throwable are both CLR Exceptions, so one catch (Exception) sets readError and rethrows
// (throw; keeps the stack, as PlatformDependent.throwException does). IllegalArgumentException is ArgumentException,
// whose Message is the message Http2Settings.Put gives it. The overridable Java methods are virtual.
public class DefaultHttp2FrameReader : IHttp2FrameReader, IHttp2FrameSizePolicy, IHttp2FrameReader.IConfiguration
{
    private const int FRAGMENT_THRESHOLD = MAX_FRAME_SIZE_LOWER_BOUND / 2;
    private readonly IHttp2HeadersDecoder headersDecoder;

    /**
     * {@code true} = reading headers, {@code false} = reading payload.
     */
    private bool readingHeaders = true;
    /**
     * Once set to {@code true} the value will never change. This is set to {@code true} if an unrecoverable error which
     * renders the connection unusable.
     */
    private bool readError;
    private byte frameType;
    private int streamId;
    private Http2Flags flags;
    private int payloadLength;
    private HeadersContinuation headersContinuation;
    private int maxFrameSize = DEFAULT_MAX_FRAME_SIZE;
    private readonly int maxSmallContinuationFrames;

    /**
     * Create a new instance.
     * <p>
     * Header names will be validated.
     */
    public DefaultHttp2FrameReader() : this(true)
    {
    }

    /**
     * Create a new instance.
     * @param validateHeaders {@code true} to validate headers. {@code false} to not validate headers.
     * @see DefaultHttp2HeadersDecoder(boolean)
     */
    public DefaultHttp2FrameReader(bool validateHeaders) : this(new DefaultHttp2HeadersDecoder(validateHeaders))
    {
    }

    public DefaultHttp2FrameReader(IHttp2HeadersDecoder headersDecoder)
        : this(headersDecoder, Http2CodecUtil.DEFAULT_MAX_SMALL_CONTINUATION_FRAME)
    {
    }

    public DefaultHttp2FrameReader(IHttp2HeadersDecoder headersDecoder, int maxSmallContinuationFrames)
    {
        this.headersDecoder = ObjectUtil.CheckNotNull(headersDecoder, "headersDecoder");
        this.maxSmallContinuationFrames = ObjectUtil.CheckPositiveOrZero(
                maxSmallContinuationFrames, "maxSmallContinuationFrames");
    }

    public virtual IHttp2HeadersDecoder.IConfiguration HeadersConfiguration()
    {
        return headersDecoder.Configuration();
    }

    public virtual IHttp2FrameReader.IConfiguration Configuration()
    {
        return this;
    }

    public virtual IHttp2FrameSizePolicy FrameSizePolicy()
    {
        return this;
    }

    public virtual void MaxFrameSize(int max)
    {
        if (!IsMaxFrameSizeValid(max))
        {
            // SETTINGS frames affect the entire connection state and thus errors must be connection errors.
            // See https://datatracker.ietf.org/doc/html/rfc9113#section-4.2 for details.
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
        CloseHeadersContinuation();
    }

    private void CloseHeadersContinuation()
    {
        if (headersContinuation != null)
        {
            headersContinuation.Close();
            headersContinuation = null;
        }
    }

    public virtual void ReadFrame(IChannelHandlerContext ctx, ByteBuf input, IHttp2FrameListener listener)
    {
        if (readError)
        {
            input.SkipBytes(input.ReadableBytes);
            return;
        }

        try
        {
            do
            {
                if (readingHeaders && !PreProcessFrame(input))
                {
                    return;
                }
                // The header is complete, fall into the next case to process the payload.
                // This is to ensure the proper handling of zero-length payloads. In this
                // case, we don't want to loop around because there may be no more data
                // available, causing us to exit the loop. Instead, we just want to perform
                // the first pass at payload processing now.
                // Wait until the entire payload has been read.
                if (input.ReadableBytes < payloadLength)
                {
                    return;
                }

                // Slice to work only on the frame being read
                ByteBuf framePayload = input.ReadSlice(payloadLength);
                // We have consumed the data for this frame, next time we read,
                // we will be expecting to read a new frame header.
                readingHeaders = true;
                VerifyFrameState();
                ProcessPayloadState(ctx, framePayload, listener);
            } while (input.IsReadable());
        }
        catch (Http2Exception e)
        {
            readError = !Http2Exception.IsStreamError(e);
            throw;
        }
        catch (Exception)
        {
            readError = true;
            throw;
        }
    }

    private bool PreProcessFrame(ByteBuf @in)
    {
        // Start pre-processing the frame by reading the necessary data
        // in common between all frame types
        if (@in.ReadableBytes < FRAME_HEADER_LENGTH)
        {
            // Wait until the entire framing section has been read.
            return false;
        }

        payloadLength = @in.ReadUnsignedMedium();
        if (payloadLength > maxFrameSize)
        {
            throw ConnectionError(FRAME_SIZE_ERROR, "Frame length: %d exceeds maximum: %d", payloadLength,
                                  maxFrameSize);
        }

        frameType = @in.ReadByte();
        flags = new Http2Flags(@in.ReadByte());
        streamId = ReadUnsignedInt(@in);
        readingHeaders = false;
        return true;
    }

    private void VerifyFrameState()
    {
        switch (frameType)
        {
            case DATA:
                VerifyDataFrame();
                break;
            case HEADERS:
                VerifyHeadersFrame();
                break;
            case PRIORITY:
                VerifyPriorityFrame();
                break;
            case RST_STREAM:
                VerifyRstStreamFrame();
                break;
            case SETTINGS:
                VerifySettingsFrame();
                break;
            case PUSH_PROMISE:
                VerifyPushPromiseFrame();
                break;
            case PING:
                VerifyPingFrame();
                break;
            case GO_AWAY:
                VerifyGoAwayFrame();
                break;
            case WINDOW_UPDATE:
                VerifyWindowUpdateFrame();
                break;
            case CONTINUATION:
                VerifyContinuationFrame();
                break;
            default:
                // Unknown frame type, could be an extension.
                VerifyUnknownFrame();
                break;
        }
    }

    private void ProcessPayloadState(IChannelHandlerContext ctx, ByteBuf @in, IHttp2FrameListener listener)
    {
        // When this method is called, we ensure that the payload buffer passed in
        // matches what we expect to be reading for payloadLength
        Debug.Assert(@in.ReadableBytes == payloadLength);

        // Read the payload and fire the frame event to the listener.
        switch (frameType)
        {
            case DATA:
                ReadDataFrame(ctx, @in, listener);
                break;
            case HEADERS:
                ReadHeadersFrame(ctx, @in, listener);
                break;
            case PRIORITY:
                ReadPriorityFrame(ctx, @in, listener);
                break;
            case RST_STREAM:
                ReadRstStreamFrame(ctx, @in, listener);
                break;
            case SETTINGS:
                ReadSettingsFrame(ctx, @in, listener);
                break;
            case PUSH_PROMISE:
                ReadPushPromiseFrame(ctx, @in, listener);
                break;
            case PING:
                ReadPingFrame(ctx, @in.ReadLong(), listener);
                break;
            case GO_AWAY:
                ReadGoAwayFrame(ctx, @in, listener);
                break;
            case WINDOW_UPDATE:
                ReadWindowUpdateFrame(ctx, @in, listener);
                break;
            case CONTINUATION:
                ReadContinuationFrame(@in, listener);
                break;
            default:
                ReadUnknownFrame(ctx, @in, listener);
                break;
        }
    }

    private void VerifyDataFrame()
    {
        VerifyAssociatedWithAStream();
        VerifyNotProcessingHeaders();

        if (payloadLength < flags.GetPaddingPresenceFieldLength())
        {
            throw StreamError(streamId, FRAME_SIZE_ERROR,
                    "Frame length %d too small.", payloadLength);
        }
    }

    private void VerifyHeadersFrame()
    {
        VerifyAssociatedWithAStream();
        VerifyNotProcessingHeaders();

        int requiredLength = flags.GetPaddingPresenceFieldLength() + flags.GetNumPriorityBytes();
        if (payloadLength < requiredLength)
        {
            // HEADER frames carry a field_block and thus failure to process them results
            // in HPACK corruption and renders the connection unusable.
            // See https://datatracker.ietf.org/doc/html/rfc9113#section-4.2 for details.
            throw ConnectionError(FRAME_SIZE_ERROR,
                    "Frame length %d too small for HEADERS frame with stream %d.", payloadLength, streamId);
        }
    }

    private void VerifyPriorityFrame()
    {
        VerifyAssociatedWithAStream();
        VerifyNotProcessingHeaders();

        if (payloadLength != PRIORITY_ENTRY_LENGTH)
        {
            throw StreamError(streamId, FRAME_SIZE_ERROR,
                    "Invalid frame length %d.", payloadLength);
        }
    }

    private void VerifyRstStreamFrame()
    {
        VerifyAssociatedWithAStream();
        VerifyNotProcessingHeaders();

        if (payloadLength != INT_FIELD_LENGTH)
        {
            throw ConnectionError(FRAME_SIZE_ERROR, "Invalid frame length %d.", payloadLength);
        }
    }

    private void VerifySettingsFrame()
    {
        VerifyNotProcessingHeaders();
        if (streamId != 0)
        {
            throw ConnectionError(PROTOCOL_ERROR, "A stream ID must be zero.");
        }
        if (flags.Ack() && payloadLength > 0)
        {
            throw ConnectionError(FRAME_SIZE_ERROR, "Ack settings frame must have an empty payload.");
        }
        if (payloadLength % SETTING_ENTRY_LENGTH > 0)
        {
            throw ConnectionError(FRAME_SIZE_ERROR, "Frame length %d invalid.", payloadLength);
        }
    }

    private void VerifyPushPromiseFrame()
    {
        VerifyAssociatedWithAStream();
        VerifyNotProcessingHeaders();

        // Subtract the length of the promised stream ID field, to determine the length of the
        // rest of the payload (header block fragment + payload).
        int minLength = flags.GetPaddingPresenceFieldLength() + INT_FIELD_LENGTH;
        if (payloadLength < minLength)
        {
            // PUSH_PROMISE frames carry a field_block and thus failure to process them results
            // in HPACK corruption and renders the connection unusable.
            // See https://datatracker.ietf.org/doc/html/rfc9113#section-4.2 for details.
            throw ConnectionError(FRAME_SIZE_ERROR,
                    "Frame length %d too small for PUSH_PROMISE frame with stream id %d.", payloadLength, streamId);
        }
    }

    private void VerifyPingFrame()
    {
        VerifyNotProcessingHeaders();
        if (streamId != 0)
        {
            throw ConnectionError(PROTOCOL_ERROR, "A stream ID must be zero.");
        }
        if (payloadLength != PING_FRAME_PAYLOAD_LENGTH)
        {
            throw ConnectionError(FRAME_SIZE_ERROR,
                    "Frame length %d incorrect size for ping.", payloadLength);
        }
    }

    private void VerifyGoAwayFrame()
    {
        VerifyNotProcessingHeaders();

        if (streamId != 0)
        {
            throw ConnectionError(PROTOCOL_ERROR, "A stream ID must be zero.");
        }
        if (payloadLength < 8)
        {
            throw ConnectionError(FRAME_SIZE_ERROR, "Frame length %d too small.", payloadLength);
        }
    }

    private void VerifyWindowUpdateFrame()
    {
        VerifyNotProcessingHeaders();
        VerifyStreamOrConnectionId(streamId, "Stream ID");

        if (payloadLength != INT_FIELD_LENGTH)
        {
            throw ConnectionError(FRAME_SIZE_ERROR, "Invalid frame length %d.", payloadLength);
        }
    }

    private void VerifyContinuationFrame()
    {
        VerifyAssociatedWithAStream();

        if (headersContinuation == null)
        {
            throw ConnectionError(PROTOCOL_ERROR, "Received %s frame but not currently processing headers.",
                    unchecked((sbyte) frameType));
        }

        if (streamId != headersContinuation.GetStreamId())
        {
            throw ConnectionError(PROTOCOL_ERROR, "Continuation stream ID does not match pending headers. "
                    + "Expected %d, but received %d.", headersContinuation.GetStreamId(), streamId);
        }

        if (headersContinuation.NumSmallFragments() >=  maxSmallContinuationFrames)
        {
            throw ConnectionError(ENHANCE_YOUR_CALM,
                    "Number of small consecutive continuations frames %d exceeds maximum: %d",
                    headersContinuation.NumSmallFragments(), maxSmallContinuationFrames);
        }
    }

    private void VerifyUnknownFrame()
    {
        VerifyNotProcessingHeaders();
    }

    private void ReadDataFrame(IChannelHandlerContext ctx, ByteBuf payload,
            IHttp2FrameListener listener)
    {
        int padding = ReadPadding(payload);

        // Determine how much data there is to read by removing the trailing
        // padding.
        int dataLength = LengthWithoutTrailingPadding(payload.ReadableBytes, padding);

        payload.WriterIndex = payload.ReaderIndex + dataLength;
        listener.OnDataRead(ctx, streamId, payload, padding, flags.EndOfStream());
    }

    private void ReadHeadersFrame(IChannelHandlerContext ctx, ByteBuf payload,
            IHttp2FrameListener listener)
    {
        int headersStreamId = streamId;
        Http2Flags headersFlags = flags;
        int padding = ReadPadding(payload);

        // The callback that is invoked is different depending on whether priority information
        // is present in the headers frame.
        if (flags.PriorityPresent())
        {
            long word1 = payload.ReadUnsignedInt();
            bool exclusive = (word1 & 0x80000000L) != 0;
            int streamDependency = (int) (word1 & 0x7FFFFFFFL);
            if (streamDependency == streamId)
            {
                // Stream dependencies are deprecated in RFC 9113 but this behavior is defined in
                // https://datatracker.ietf.org/doc/html/rfc7540#section-5.3.1 which says this must be treated as a
                // stream error of type PROTOCOL_ERROR. However, because we will not process the payload, a stream
                // error would result in HPACK corruption. Therefor, it is elevated to a connection error.
                throw ConnectionError(
                        PROTOCOL_ERROR, "HEADERS frame for stream %d cannot depend on itself.", streamId);
            }

            short weight = (short) (payload.ReadByte() + 1);
            int lenToRead = LengthWithoutTrailingPadding(payload.ReadableBytes, padding);

            // Create a handler that invokes the listener when the header block is complete.
            headersContinuation = new PriorityHeadersContinuation(this, ctx, headersStreamId, headersFlags,
                    streamDependency, weight, exclusive, padding);

            // Process the initial fragment, invoking the listener's callback if end of headers.
            headersContinuation.ProcessFragment(flags.EndOfHeaders(), payload, lenToRead, listener);
            ResetHeadersContinuationIfEnd(flags.EndOfHeaders());
            return;
        }

        // The priority fields are not present in the frame. Prepare a continuation that invokes
        // the listener callback without priority information.
        headersContinuation = new HeadersContinuationWithoutPriority(this, ctx, headersStreamId, headersFlags,
                padding);

        // Process the initial fragment, invoking the listener's callback if end of headers.
        int len = LengthWithoutTrailingPadding(payload.ReadableBytes, padding);
        headersContinuation.ProcessFragment(flags.EndOfHeaders(), payload, len, listener);
        ResetHeadersContinuationIfEnd(flags.EndOfHeaders());
    }

    // CLR: the anonymous HeadersContinuation of readHeadersFrame for a HEADERS frame with priority fields.
    private sealed class PriorityHeadersContinuation : HeadersContinuation
    {
        private readonly IChannelHandlerContext ctx;
        private readonly int headersStreamId;
        private readonly Http2Flags headersFlags;
        private readonly int streamDependency;
        private readonly short weight;
        private readonly bool exclusive;
        private readonly int padding;

        internal PriorityHeadersContinuation(DefaultHttp2FrameReader reader, IChannelHandlerContext ctx,
                int headersStreamId, Http2Flags headersFlags, int streamDependency, short weight, bool exclusive,
                int padding) : base(reader)
        {
            this.ctx = ctx;
            this.headersStreamId = headersStreamId;
            this.headersFlags = headersFlags;
            this.streamDependency = streamDependency;
            this.weight = weight;
            this.exclusive = exclusive;
            this.padding = padding;
        }

        internal override int GetStreamId()
        {
            return headersStreamId;
        }

        internal override void ProcessFragment(bool endOfHeaders, ByteBuf fragment, int len,
                IHttp2FrameListener listener)
        {
            HeadersBlockBuilder hdrBlockBuilder = HeadersBlockBuilder();
            hdrBlockBuilder.AddFragment(fragment, len, ctx.Alloc(), endOfHeaders);

            if (endOfHeaders)
            {
                listener.OnHeadersRead(ctx, headersStreamId, hdrBlockBuilder.Headers(), streamDependency,
                        weight, exclusive, padding, headersFlags.EndOfStream());
            }
        }
    }

    // CLR: the anonymous HeadersContinuation of readHeadersFrame for a HEADERS frame without priority fields.
    private sealed class HeadersContinuationWithoutPriority : HeadersContinuation
    {
        private readonly IChannelHandlerContext ctx;
        private readonly int headersStreamId;
        private readonly Http2Flags headersFlags;
        private readonly int padding;

        internal HeadersContinuationWithoutPriority(DefaultHttp2FrameReader reader, IChannelHandlerContext ctx,
                int headersStreamId, Http2Flags headersFlags, int padding) : base(reader)
        {
            this.ctx = ctx;
            this.headersStreamId = headersStreamId;
            this.headersFlags = headersFlags;
            this.padding = padding;
        }

        internal override int GetStreamId()
        {
            return headersStreamId;
        }

        internal override void ProcessFragment(bool endOfHeaders, ByteBuf fragment, int len,
                IHttp2FrameListener listener)
        {
            HeadersBlockBuilder hdrBlockBuilder = HeadersBlockBuilder();
            hdrBlockBuilder.AddFragment(fragment, len, ctx.Alloc(), endOfHeaders);

            if (endOfHeaders)
            {
                listener.OnHeadersRead(ctx, headersStreamId, hdrBlockBuilder.Headers(), padding,
                                headersFlags.EndOfStream());
            }
        }
    }

    private void ResetHeadersContinuationIfEnd(bool endOfHeaders)
    {
        if (endOfHeaders)
        {
            CloseHeadersContinuation();
        }
    }

    private void ReadPriorityFrame(IChannelHandlerContext ctx, ByteBuf payload,
            IHttp2FrameListener listener)
    {
        long word1 = payload.ReadUnsignedInt();
        bool exclusive = (word1 & 0x80000000L) != 0;
        int streamDependency = (int) (word1 & 0x7FFFFFFFL);
        if (streamDependency == streamId)
        {
            throw StreamError(streamId, PROTOCOL_ERROR, "A stream cannot depend on itself.");
        }

        short weight = (short) (payload.ReadByte() + 1);
        listener.OnPriorityRead(ctx, streamId, streamDependency, weight, exclusive);
    }

    private void ReadRstStreamFrame(IChannelHandlerContext ctx, ByteBuf payload,
            IHttp2FrameListener listener)
    {
        long errorCode = payload.ReadUnsignedInt();
        listener.OnRstStreamRead(ctx, streamId, errorCode);
    }

    private void ReadSettingsFrame(IChannelHandlerContext ctx, ByteBuf payload,
            IHttp2FrameListener listener)
    {
        if (flags.Ack())
        {
            listener.OnSettingsAckRead(ctx);
        }
        else
        {
            int numSettings = payloadLength / SETTING_ENTRY_LENGTH;
            Http2Settings settings = new Http2Settings();

            for (int index = 0; index < numSettings; ++index)
            {
                char id = (char) payload.ReadUnsignedShort();
                long value = payload.ReadUnsignedInt();

                try
                {
                    settings.Put(id, value);
                }
                catch (ArgumentException e)
                {
                    if (id == SETTINGS_INITIAL_WINDOW_SIZE)
                    {
                        throw ConnectionError(FLOW_CONTROL_ERROR, e,
                                "Failed setting initial window size: %s", e.Message);
                    }

                    throw ConnectionError(PROTOCOL_ERROR, e, "Protocol error: %s", e.Message);
                }
            }

            listener.OnSettingsRead(ctx, settings);
        }
    }

    private void ReadPushPromiseFrame(IChannelHandlerContext ctx, ByteBuf payload,
            IHttp2FrameListener listener)
    {
        int pushPromiseStreamId = streamId;
        int padding = ReadPadding(payload);
        int promisedStreamId = ReadUnsignedInt(payload);

        // Create a handler that invokes the listener when the header block is complete.
        headersContinuation = new PushPromiseHeadersContinuation(this, ctx, pushPromiseStreamId, promisedStreamId,
                padding);

        // Process the initial fragment, invoking the listener's callback if end of headers.
        int len = LengthWithoutTrailingPadding(payload.ReadableBytes, padding);
        headersContinuation.ProcessFragment(flags.EndOfHeaders(), payload, len, listener);
        ResetHeadersContinuationIfEnd(flags.EndOfHeaders());
    }

    // CLR: the anonymous HeadersContinuation of readPushPromiseFrame.
    private sealed class PushPromiseHeadersContinuation : HeadersContinuation
    {
        private readonly IChannelHandlerContext ctx;
        private readonly int pushPromiseStreamId;
        private readonly int promisedStreamId;
        private readonly int padding;

        internal PushPromiseHeadersContinuation(DefaultHttp2FrameReader reader, IChannelHandlerContext ctx,
                int pushPromiseStreamId, int promisedStreamId, int padding) : base(reader)
        {
            this.ctx = ctx;
            this.pushPromiseStreamId = pushPromiseStreamId;
            this.promisedStreamId = promisedStreamId;
            this.padding = padding;
        }

        internal override int GetStreamId()
        {
            return pushPromiseStreamId;
        }

        internal override void ProcessFragment(bool endOfHeaders, ByteBuf fragment, int len,
                IHttp2FrameListener listener)
        {
            HeadersBlockBuilder().AddFragment(fragment, len, ctx.Alloc(), endOfHeaders);

            if (endOfHeaders)
            {
                listener.OnPushPromiseRead(ctx, pushPromiseStreamId, promisedStreamId,
                        HeadersBlockBuilder().Headers(), padding);
            }
        }
    }

    private void ReadPingFrame(IChannelHandlerContext ctx, long data,
            IHttp2FrameListener listener)
    {
        if (flags.Ack())
        {
            listener.OnPingAckRead(ctx, data);
        }
        else
        {
            listener.OnPingRead(ctx, data);
        }
    }

    private void ReadGoAwayFrame(IChannelHandlerContext ctx, ByteBuf payload,
            IHttp2FrameListener listener)
    {
        int lastStreamId = ReadUnsignedInt(payload);
        long errorCode = payload.ReadUnsignedInt();
        listener.OnGoAwayRead(ctx, lastStreamId, errorCode, payload);
    }

    private void ReadWindowUpdateFrame(IChannelHandlerContext ctx, ByteBuf payload,
            IHttp2FrameListener listener)
    {
        int windowSizeIncrement = ReadUnsignedInt(payload);
        if (windowSizeIncrement == 0)
        {
            // On the connection stream this must be a connection error but for request streams it is a stream error.
            // See https://datatracker.ietf.org/doc/html/rfc9113#section-6.9 for details.
            if (streamId == CONNECTION_STREAM_ID)
            {
                throw ConnectionError(PROTOCOL_ERROR,
                        "Received WINDOW_UPDATE with delta 0 for connection stream");
            }
            else
            {
                throw StreamError(streamId, PROTOCOL_ERROR,
                        "Received WINDOW_UPDATE with delta 0 for stream: %d", streamId);
            }
        }

        listener.OnWindowUpdateRead(ctx, streamId, windowSizeIncrement);
    }

    private void ReadContinuationFrame(ByteBuf payload, IHttp2FrameListener listener)
    {
        // Process the initial fragment, invoking the listener's callback if end of headers.
        headersContinuation.ProcessFragment(flags.EndOfHeaders(), payload,
                payloadLength, listener);
        ResetHeadersContinuationIfEnd(flags.EndOfHeaders());
    }

    private void ReadUnknownFrame(IChannelHandlerContext ctx, ByteBuf payload,
            IHttp2FrameListener listener)
    {
        listener.OnUnknownFrame(ctx, frameType, streamId, flags, payload);
    }

    /**
     * If padding is present in the payload, reads the next byte as padding. The padding also includes the one byte
     * width of the pad length field. Otherwise, returns zero.
     */
    private int ReadPadding(ByteBuf payload)
    {
        if (!flags.PaddingPresent())
        {
            return 0;
        }

        return payload.ReadByte() + 1;
    }

    /**
     * The padding parameter consists of the 1 byte pad length field and the trailing padding bytes. This method
     * returns the number of readable bytes without the trailing padding.
     */
    private static int LengthWithoutTrailingPadding(int readableBytes, int padding)
    {
        if (padding == 0)
        {
            return readableBytes;
        }

        int n = readableBytes - (padding - 1);
        if (n < 0)
        {
            throw ConnectionError(PROTOCOL_ERROR, "Frame payload too small for padding.");
        }

        return n;
    }

    /**
     * Base class for processing of HEADERS and PUSH_PROMISE header blocks that potentially span
     * multiple frames. The implementation of this interface will perform the final callback to the
     * {@link Http2FrameListener} once the end of headers is reached.
     */
    // CLR: headersBlockBuilder() is HeadersBlockBuilder(): a member of the reader's nested types cannot be called
    // like the nested HeadersBlockBuilder type it is used with (CS0102 inside the outer type's scope; owner rule:
    // trailing underscore). The reader reference replaces the implicit outer instance.
    private abstract class HeadersContinuation
    {
        private readonly HeadersBlockBuilder builder;

        protected HeadersContinuation(DefaultHttp2FrameReader reader)
        {
            builder = new HeadersBlockBuilder(reader);
        }

        /**
         * Returns the stream for which headers are currently being processed.
         */
        internal abstract int GetStreamId();

        /**
         * Return the number of fragments that were used so far.
         *
         * @return the number of fragments
         */
        internal int NumSmallFragments()
        {
            return builder.NumSmallFragments();
        }

        /**
         * Processes the next fragment for the current header block.
         *
         * @param endOfHeaders whether the fragment is the last in the header block.
         * @param fragment the fragment of the header block to be added.
         * @param listener the listener to be notified if the header block is completed.
         */
        internal abstract void ProcessFragment(bool endOfHeaders, ByteBuf fragment, int len,
                IHttp2FrameListener listener);

        internal HeadersBlockBuilder HeadersBlockBuilder()
        {
            return builder;
        }

        /**
         * Free any allocated resources.
         */
        internal void Close()
        {
            builder.Close();
        }
    }

    /**
     * Utility class to help with construction of the headers block that may potentially span
     * multiple frames.
     */
    // CLR: the inner class gets the reader as a constructor argument (a C# nested class has no implicit outer
    // instance); the constructor is internal because the reader creates it (Java's implicit constructor).
    protected class HeadersBlockBuilder
    {
        private readonly DefaultHttp2FrameReader reader;
        private ByteBuf headerBlock;
        private int numSmallFragments;

        internal HeadersBlockBuilder(DefaultHttp2FrameReader reader)
        {
            this.reader = reader;
        }

        /**
         * The local header size maximum has been exceeded while accumulating bytes.
         * @throws Http2Exception A connection error indicating too much data has been received.
         */
        private void HeaderSizeExceeded()
        {
            Close();
            HeaderListSizeExceeded(reader.headersDecoder.Configuration().MaxHeaderListSizeGoAway());
        }

        /**
         * Return the number of fragments that was used so far.
         *
         * @return number of fragments.
         */
        internal int NumSmallFragments()
        {
            return numSmallFragments;
        }

        /**
         * Adds a fragment to the block.
         *
         * @param fragment the fragment of the headers block to be added.
         * @param alloc allocator for new blocks if needed.
         * @param endOfHeaders flag indicating whether the current frame is the end of the headers.
         *            This is used for an optimization for when the first fragment is the full
         *            block. In that case, the buffer is used directly without copying.
         */
        internal void AddFragment(ByteBuf fragment, int len, IByteBufAllocator alloc,
                bool endOfHeaders)
        {
            if (reader.maxSmallContinuationFrames > 0 && !endOfHeaders && len < FRAGMENT_THRESHOLD)
            {
                // Only count of the fragment is not the end of header and if its < 8kb.
                numSmallFragments++;
            }

            if (headerBlock == null)
            {
                if (len > reader.headersDecoder.Configuration().MaxHeaderListSizeGoAway())
                {
                    HeaderSizeExceeded();
                }

                if (endOfHeaders)
                {
                    // Optimization - don't bother copying, just use the buffer as-is. Need
                    // to retain since we release when the header block is built.
                    headerBlock = fragment.ReadRetainedSlice(len);
                }
                else
                {
                    headerBlock = alloc.Buffer(len).WriteBytes(fragment, len);
                }

                return;
            }

            if (reader.headersDecoder.Configuration().MaxHeaderListSizeGoAway() - len <
                    headerBlock.ReadableBytes)
            {
                HeaderSizeExceeded();
            }

            if (headerBlock.IsWritable(len))
            {
                // The buffer can hold the requested bytes, just write it directly.
                headerBlock.WriteBytes(fragment, len);
            }
            else
            {
                // Allocate a new buffer that is big enough to hold the entire header block so far.
                ByteBuf buf = alloc.Buffer(headerBlock.ReadableBytes + len);
                buf.WriteBytes(headerBlock).WriteBytes(fragment, len);
                headerBlock.Release();
                headerBlock = buf;
            }
        }

        /**
         * Builds the headers from the completed headers block. After this is called, this builder
         * should not be called again.
         */
        internal IHttp2Headers Headers()
        {
            try
            {
                return reader.headersDecoder.DecodeHeaders(reader.streamId, headerBlock);
            }
            finally
            {
                Close();
            }
        }

        /**
         * Closes this builder and frees any resources.
         */
        internal void Close()
        {
            if (headerBlock != null)
            {
                headerBlock.Release();
                headerBlock = null;
            }

            // Clear the member variable pointing at this instance.
            reader.headersContinuation = null;
        }
    }

    /**
     * Verify that current state is not processing on header block
     * @throws Http2Exception thrown if {@link #headersContinuation} is not null
     */
    private void VerifyNotProcessingHeaders()
    {
        if (headersContinuation != null)
        {
            throw ConnectionError(PROTOCOL_ERROR, "Received frame of type %s while processing headers on stream %d.",
                                  unchecked((sbyte) frameType), headersContinuation.GetStreamId());
        }
    }

    private void VerifyAssociatedWithAStream()
    {
        if (streamId == 0)
        {
            throw ConnectionError(PROTOCOL_ERROR, "Frame of type %s must be associated with a stream.",
                    unchecked((sbyte) frameType));
        }
    }

    private static void VerifyStreamOrConnectionId(int streamId, string argumentName)
    {
        if (streamId < 0)
        {
            throw ConnectionError(PROTOCOL_ERROR, "%s must be >= 0", argumentName);
        }
    }
}
