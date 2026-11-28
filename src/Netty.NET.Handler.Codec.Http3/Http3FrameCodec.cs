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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http3.Http3CodecUtils;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * Decodes / encodes {@link Http3Frame}s.
 */
// CLR: ByteToMessageDecoder is the codec-base ByteToMessageDecoder and ChannelOutboundHandler the Transport's
// IChannelOutboundHandler, whose operations return a Task instead of completing a ChannelPromise argument :
// write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken), which creates the promise
// (Http3CodecUtils.NewPromise()), runs the Java body and returns the promise's Task, while the private write
// helpers keep the TaskCompletionSource parameter and ctx.write(msg, promise) is Http3CodecUtils.Write;
// bind/connect/disconnect/close/deregister pass their CancellationToken to the context and return its Task.
// promise.setFailure(e) is SetException, promise.trySuccess() TrySetResult, and ctx.voidPromise() a new promise
// whose Task is not observed (there is no void promise, docs/transport-porting.md). QuicStreamChannel and
// QuicStreamFrame are codec-quic's IQuicStreamChannel and IQuicStreamFrame (content() is the Content property).
// The lambda factory of newFactory is the private nested class CodecFactory, and Http3FrameCodecFactory the nested
// internal interface IHttp3FrameCodecFactory. The BiFunction<T, ByteBuf, Boolean> writers of writeDynamicFrame are
// Func<T, ByteBuf, bool> lambdas. ReadResumptionListener keeps Runnable.run() as Run(), passed to
// QpackDecoder.Decode as the Action whenDecoded, and both listeners keep operationComplete(future) as
// OperationComplete(Task<IQuicStreamChannel>), registered with
// QpackAttributes.WhenDecoderStreamAvailable/WhenEncoderStreamAvailable. The BiConsumer sink is the method group
// Http3HeadersSink.Accept. ReadResumptionListener's byte state keeps the bit masks with explicit narrowing ((byte)
// casts for Java's compound assignments). Map.Entry<Long, Long> is KeyValuePair<long, long>.
// UnsupportedOperationException is NotSupportedException; assert is Debug.Assert; the numbers of the messages
// print with the invariant culture. SocketAddress is EndPoint. The @Nullable annotations and the throws clauses
// have no counterpart.
internal sealed class Http3FrameCodec : ByteToMessageDecoder, IChannelOutboundHandler
{
    private readonly IHttp3FrameTypeValidator validator;
    private readonly long maxHeaderListSize;
    private readonly int maxUnknownFramePayloadLength;
    private readonly QpackDecoder qpackDecoder;
    private readonly QpackEncoder qpackEncoder;
    private readonly IHttp3RequestStreamCodecState encodeState;
    private readonly IHttp3RequestStreamCodecState decodeState;
    private readonly Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator;
    private bool firstFrame = true;
    private bool error;
    private long type = -1;
    private int payLoadLength = -1;
    private QpackAttributes qpackAttributes;
    private ReadResumptionListener readResumptionListener;
    private WriteResumptionListener writeResumptionListener;

    internal static IHttp3FrameCodecFactory NewFactory(QpackDecoder qpackDecoder,
                                                       long maxHeaderListSize, int maxUnknownFramePayloadLength,
                                                       QpackEncoder qpackEncoder)
    {
        CheckNotNull(qpackEncoder, "qpackEncoder");
        CheckNotNull(qpackDecoder, "qpackDecoder");
        CheckPositive(maxHeaderListSize, "maxHeaderListSize");
        CheckPositive(maxUnknownFramePayloadLength, "maxUnknownFramePayloadLength");

        // QPACK decoder and encoder are shared between streams in a connection.
        return new CodecFactory(qpackDecoder, maxHeaderListSize, maxUnknownFramePayloadLength, qpackEncoder);
    }

    // CLR: the lambda (validator, encodeState, decodeState, nonStandardSettingsValidator) -> new Http3FrameCodec(...) of
    // newFactory.
    private sealed class CodecFactory : IHttp3FrameCodecFactory
    {
        private readonly QpackDecoder qpackDecoder;
        private readonly long maxHeaderListSize;
        private readonly int maxUnknownFramePayloadLength;
        private readonly QpackEncoder qpackEncoder;

        internal CodecFactory(QpackDecoder qpackDecoder, long maxHeaderListSize, int maxUnknownFramePayloadLength,
                              QpackEncoder qpackEncoder)
        {
            this.qpackDecoder = qpackDecoder;
            this.maxHeaderListSize = maxHeaderListSize;
            this.maxUnknownFramePayloadLength = maxUnknownFramePayloadLength;
            this.qpackEncoder = qpackEncoder;
        }

        public IChannelHandler NewCodec(IHttp3FrameTypeValidator validator, IHttp3RequestStreamCodecState encodeState,
                                        IHttp3RequestStreamCodecState decodeState,
                                        Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator)
        {
            return new Http3FrameCodec(validator, qpackDecoder,
                    maxHeaderListSize, maxUnknownFramePayloadLength, qpackEncoder,
                    encodeState, decodeState, nonStandardSettingsValidator);
        }
    }

    internal Http3FrameCodec(IHttp3FrameTypeValidator validator, QpackDecoder qpackDecoder,
                             long maxHeaderListSize, int maxUnknownFramePayloadLength,
                             QpackEncoder qpackEncoder, IHttp3RequestStreamCodecState encodeState,
                             IHttp3RequestStreamCodecState decodeState,
                             Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator)
    {
        this.validator = CheckNotNull(validator, "validator");
        this.qpackDecoder = CheckNotNull(qpackDecoder, "qpackDecoder");
        this.maxHeaderListSize = CheckPositive(maxHeaderListSize, "maxHeaderListSize");
        this.maxUnknownFramePayloadLength = CheckPositive(maxUnknownFramePayloadLength, "maxUnknownFramePayloadLength");
        this.qpackEncoder = CheckNotNull(qpackEncoder, "qpackEncoder");
        this.encodeState = CheckNotNull(encodeState, "encodeState");
        this.decodeState = CheckNotNull(decodeState, "decodeState");
        this.nonStandardSettingsValidator = nonStandardSettingsValidator;
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        qpackAttributes = Http3.GetQpackAttributes(ctx.Channel.Parent);
        Debug.Assert(qpackAttributes != null);

        InitReadResumptionListenerIfRequired(ctx);
        base.HandlerAdded(ctx);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        if (writeResumptionListener != null)
        {
            writeResumptionListener.Drain();
        }

        base.ChannelInactive(ctx);
    }

    protected override void HandlerRemoved0(IChannelHandlerContext ctx)
    {
        if (writeResumptionListener != null)
        {
            // drain everything so we are sure we never leak anything.
            writeResumptionListener.Drain();
        }

        AbandonIfBlocked(ctx);
        base.HandlerRemoved0(ctx);
    }

    private void AbandonIfBlocked(IChannelHandlerContext ctx)
    {
        if (readResumptionListener == null || !readResumptionListener.IsSuspended())
        {
            return;
        }

        // https://www.rfc-editor.org/rfc/rfc9204.html#section-2.2.2.2
        long streamId = ((IQuicStreamChannel) ctx.Channel).StreamId();
        if (!qpackDecoder.ReleaseBlockedStream(streamId))
        {
            return;
        }

        if (qpackAttributes.DecoderStreamAvailable())
        {
            qpackDecoder.SendStreamCancellation(qpackAttributes.DecoderStream(), streamId);
        }
        else
        {
            qpackAttributes.WhenDecoderStreamAvailable(f =>
            {
                if (f.IsCompletedSuccessfully)
                {
                    qpackDecoder.SendStreamCancellation(qpackAttributes.DecoderStream(), streamId);
                }
            });
        }
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        ByteBuf buffer;

        if (msg is IQuicStreamFrame)
        {
            IQuicStreamFrame streamFrame = (IQuicStreamFrame) msg;
            buffer = streamFrame.Content.Retain();
            streamFrame.Release();
        }
        else
        {
            buffer = (ByteBuf) msg;
        }

        base.ChannelRead(ctx, buffer);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        Debug.Assert(readResumptionListener != null);

        if (readResumptionListener.ReadCompleted())
        {
            base.ChannelReadComplete(ctx);
        }
    }

    private void ConnectionError(IChannelHandlerContext ctx, Http3ErrorCode code, string msg, bool fireException)
    {
        error = true;
        Http3CodecUtils.ConnectionError(ctx, code, msg, fireException);
    }

    private void ConnectionError(IChannelHandlerContext ctx, Http3Exception exception, bool fireException)
    {
        error = true;
        Http3CodecUtils.ConnectionError(ctx, exception, fireException);
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        Debug.Assert(readResumptionListener != null);
        if (!@in.IsReadable() || readResumptionListener.IsSuspended())
        {
            return;
        }

        if (error)
        {
            @in.SkipBytes(@in.ReadableBytes);
            return;
        }

        if (type == -1)
        {
            int typeLen = NumBytesForVariableLengthInteger(@in.GetByte(@in.ReaderIndex));
            if (@in.ReadableBytes < typeLen)
            {
                return;
            }

            long localType = ReadVariableLengthInteger(@in, typeLen);
            if (Http3CodecUtils.IsReservedHttp2FrameType(localType))
            {
                // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.8
                ConnectionError(ctx, Http3ErrorCode.H3_FRAME_UNEXPECTED,
                        "Reserved type for HTTP/2 received.", true);
                return;
            }

            try
            {
                // Validate if the type is valid for the current stream first.
                validator.Validate(localType, firstFrame);
            }
            catch (Http3Exception e)
            {
                ConnectionError(ctx, e, true);
                return;
            }

            type = localType;
            firstFrame = false;
            if (!@in.IsReadable())
            {
                return;
            }
        }

        if (payLoadLength == -1)
        {
            int payloadLen = NumBytesForVariableLengthInteger(@in.GetByte(@in.ReaderIndex));
            Debug.Assert(payloadLen <= 8);
            if (@in.ReadableBytes < payloadLen)
            {
                return;
            }

            long len = ReadVariableLengthInteger(@in, payloadLen);
            if (len > int.MaxValue)
            {
                ConnectionError(ctx, Http3ErrorCode.H3_EXCESSIVE_LOAD,
                        "Received an invalid frame len.", true);
                return;
            }

            payLoadLength = (int) len;
        }

        int read = DecodeFrame(ctx, type, payLoadLength, @in, @out);
        if (read >= 0)
        {
            if (read == payLoadLength)
            {
                type = -1;
                payLoadLength = -1;
            }
            else
            {
                payLoadLength -= read;
            }
        }
    }

    private static int SkipBytes(ByteBuf @in, int payLoadLength)
    {
        int length = Math.Min(@in.ReadableBytes, payLoadLength);
        @in.SkipBytes(length);
        return length;
    }

    private int DecodeFrame(IChannelHandlerContext ctx, long longType, int payLoadLength, ByteBuf @in,
                            IList<object> @out)
    {
        if (longType > int.MaxValue && !Http3CodecUtils.IsReservedFrameType(longType))
        {
            return SkipBytes(@in, payLoadLength);
        }

        int type = (int) longType;

        // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-11.2.1
        switch (type)
        {
            case HTTP3_DATA_FRAME_TYPE:
                // DATA
                // https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.1
                int readable = @in.ReadableBytes;
                if (readable == 0 && payLoadLength > 0)
                {
                    return 0;
                }

                int length = Math.Min(readable, payLoadLength);
                @out.Add(new DefaultHttp3DataFrame(@in.ReadRetainedSlice(length)));
                return length;
            case HTTP3_HEADERS_FRAME_TYPE:
                // HEADERS
                // https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.2
                if (!EnforceMaxPayloadLength(ctx, @in, type, payLoadLength,
                        // Let's use the maxHeaderListSize as a limit as this is this is the decompressed amounts of
                        // bytes which means the once we decompressed the headers we will be bigger then the actual
                        // payload size now.
                        maxHeaderListSize, Http3ErrorCode.H3_EXCESSIVE_LOAD))
                {
                    return 0;
                }

                Debug.Assert(qpackAttributes != null);

                if (!qpackAttributes.DynamicTableDisabled() && !qpackAttributes.DecoderStreamAvailable())
                {
                    Debug.Assert(readResumptionListener != null);
                    readResumptionListener.Suspended();
                    return 0;
                }

                IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
                if (DecodeHeaders(ctx, headersFrame.Headers(), @in, payLoadLength, decodeState.ReceivedFinalHeaders()))
                {
                    @out.Add(headersFrame);
                    return payLoadLength;
                }

                return -1;
            case HTTP3_CANCEL_PUSH_FRAME_TYPE:
                // CANCEL_PUSH
                // https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.3
                if (!EnforceMaxPayloadLength(ctx, @in, type, payLoadLength,
                        HTTP3_CANCEL_PUSH_FRAME_MAX_LEN, Http3ErrorCode.H3_FRAME_ERROR) ||
                        !EnforceNonZeroPayloadLength(ctx, type, payLoadLength))
                {
                    return 0;
                }

                int pushIdLen = NumBytesForVariableLengthInteger(@in.GetByte(@in.ReaderIndex));
                if (!EnforceExactIdLength(ctx, type, payLoadLength, pushIdLen))
                {
                    return 0;
                }

                @out.Add(new DefaultHttp3CancelPushFrame(ReadVariableLengthInteger(@in, pushIdLen)));
                return payLoadLength;
            case HTTP3_SETTINGS_FRAME_TYPE:
                // SETTINGS
                // https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.4

                // Use 256 as this gives space for 16 maximal size encoder and 128 minimal size encoded settings.
                if (!EnforceMaxPayloadLength(ctx, @in, type, payLoadLength, HTTP3_SETTINGS_FRAME_MAX_LEN,
                        Http3ErrorCode.H3_EXCESSIVE_LOAD))
                {
                    return 0;
                }

                IHttp3SettingsFrame settingsFrame = DecodeSettings(ctx, @in, payLoadLength);
                if (settingsFrame != null)
                {
                    @out.Add(settingsFrame);
                }

                return payLoadLength;
            case HTTP3_PUSH_PROMISE_FRAME_TYPE:
                // PUSH_PROMISE
                // https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.5
                if (!EnforceMaxPayloadLength(ctx, @in, type, payLoadLength,
                        // Let's use the maxHeaderListSize as a limit as this is this is the decompressed amounts of
                        // bytes which means the once we decompressed the headers we will be bigger then the actual
                        // payload size now.
                        Math.Max(maxHeaderListSize, maxHeaderListSize + 8), Http3ErrorCode.H3_EXCESSIVE_LOAD) ||
                        !EnforceNonZeroPayloadLength(ctx, type, payLoadLength))
                {
                    return 0;
                }

                Debug.Assert(qpackAttributes != null);

                if (!qpackAttributes.DynamicTableDisabled() && !qpackAttributes.DecoderStreamAvailable())
                {
                    Debug.Assert(readResumptionListener != null);
                    readResumptionListener.Suspended();
                    return 0;
                }

                int readerIdx = @in.ReaderIndex;
                int pushPromiseIdLen = NumBytesForVariableLengthInteger(@in.GetByte(@in.ReaderIndex));
                if (pushPromiseIdLen > payLoadLength)
                {
                    ConnectionError(ctx, Http3ErrorCode.H3_FRAME_ERROR,
                            "Received an invalid frame len " + payLoadLength.ToString(CultureInfo.InvariantCulture) +
                            " for frame of type " + type.ToString(CultureInfo.InvariantCulture) + '.',
                            true);
                    return 0;
                }

                IHttp3PushPromiseFrame pushPromiseFrame = new DefaultHttp3PushPromiseFrame(
                        ReadVariableLengthInteger(@in, pushPromiseIdLen));
                if (DecodeHeaders(ctx, pushPromiseFrame.Headers(), @in, payLoadLength - pushPromiseIdLen, false))
                {
                    @out.Add(pushPromiseFrame);
                    return payLoadLength;
                }

                @in.ReaderIndex = readerIdx;
                return -1;
            case HTTP3_GO_AWAY_FRAME_TYPE:
                // GO_AWAY
                // https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.6
                if (!EnforceMaxPayloadLength(ctx, @in, type, payLoadLength,
                        HTTP3_GO_AWAY_FRAME_MAX_LEN, Http3ErrorCode.H3_FRAME_ERROR) ||
                        !EnforceNonZeroPayloadLength(ctx, type, payLoadLength))
                {
                    return 0;
                }

                int idLen = NumBytesForVariableLengthInteger(@in.GetByte(@in.ReaderIndex));
                if (!EnforceExactIdLength(ctx, type, payLoadLength, idLen))
                {
                    return 0;
                }

                @out.Add(new DefaultHttp3GoAwayFrame(ReadVariableLengthInteger(@in, idLen)));
                return payLoadLength;
            case HTTP3_MAX_PUSH_ID_FRAME_TYPE:
                // MAX_PUSH_ID
                // https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.7
                if (!EnforceMaxPayloadLength(ctx, @in, type, payLoadLength,
                        HTTP3_MAX_PUSH_ID_FRAME_MAX_LEN, Http3ErrorCode.H3_FRAME_ERROR) ||
                        !EnforceNonZeroPayloadLength(ctx, type, payLoadLength))
                {
                    return 0;
                }

                int pidLen = NumBytesForVariableLengthInteger(@in.GetByte(@in.ReaderIndex));
                if (!EnforceExactIdLength(ctx, type, payLoadLength, pidLen))
                {
                    return 0;
                }

                @out.Add(new DefaultHttp3MaxPushIdFrame(ReadVariableLengthInteger(@in, pidLen)));
                return payLoadLength;
            default:
                if (!Http3CodecUtils.IsReservedFrameType(longType))
                {
                    return SkipBytes(@in, payLoadLength);
                }
                // Handling reserved frame types
                // https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.8
                if (!EnforceMaxPayloadLength(ctx, @in, type, payLoadLength,
                        maxUnknownFramePayloadLength, Http3ErrorCode.H3_EXCESSIVE_LOAD))
                {
                    return 0;
                }

                @out.Add(new DefaultHttp3UnknownFrame(longType, @in.ReadRetainedSlice(payLoadLength)));
                return payLoadLength;
        }
    }

    private bool EnforceMaxPayloadLength(
            IChannelHandlerContext ctx, ByteBuf @in, int type, int payLoadLength,
            long maxPayLoadLength, Http3ErrorCode error)
    {
        if (payLoadLength > maxPayLoadLength)
        {
            ConnectionError(ctx, error,
                    "Received an invalid frame len " + payLoadLength.ToString(CultureInfo.InvariantCulture) +
                    " for frame of type " + type.ToString(CultureInfo.InvariantCulture) + '.', true);
            return false;
        }

        return @in.ReadableBytes >= payLoadLength;
    }

    /**
     * Frames whose payload consists of exactly one variable-length integer identifier (CANCEL_PUSH, GO_AWAY,
     * MAX_PUSH_ID) must declare a frame length that matches the actual encoded length of that integer. The
     * length of the integer is derived from its own first byte and is independent of {@code payLoadLength}, so
     * without this check a peer could declare a short frame length whose single byte nevertheless encodes an
     * integer requiring more bytes than either the declared length or the buffer actually has available.
     */
    private bool EnforceExactIdLength(IChannelHandlerContext ctx, int type, int payLoadLength, int idLen)
    {
        if (idLen != payLoadLength)
        {
            ConnectionError(ctx, Http3ErrorCode.H3_FRAME_ERROR,
                    "Received an invalid frame len " + payLoadLength.ToString(CultureInfo.InvariantCulture) +
                    " for frame of type " + type.ToString(CultureInfo.InvariantCulture) + '.', true);
            return false;
        }

        return true;
    }

    private bool EnforceNonZeroPayloadLength(IChannelHandlerContext ctx, int type, int payLoadLength)
    {
        if (payLoadLength == 0)
        {
            ConnectionError(ctx, Http3ErrorCode.H3_FRAME_ERROR,
                "Received an invalid frame len " + payLoadLength.ToString(CultureInfo.InvariantCulture) +
                " for frame of type " + type.ToString(CultureInfo.InvariantCulture) + '.', true);
            return false;
        }

        return true;
    }

    private IHttp3SettingsFrame DecodeSettings(IChannelHandlerContext ctx, ByteBuf @in, int payLoadLength)
    {
        IHttp3SettingsFrame settingsFrame = new DefaultHttp3SettingsFrame(
                new Http3Settings(nonStandardSettingsValidator));

        while (payLoadLength > 0)
        {
            int keyLen = NumBytesForVariableLengthInteger(@in.GetByte(@in.ReaderIndex));
            if (keyLen > payLoadLength)
            {
                ConnectionError(ctx, Http3ErrorCode.H3_FRAME_ERROR,
                        "Received an invalid settings frame length.", true);
                return null;
            }

            long key = ReadVariableLengthInteger(@in, keyLen);
            if (Http3CodecUtils.IsReservedHttp2Setting(key))
            {
                // This must be treated as a connection error
                // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.4.1
                ConnectionError(ctx, Http3ErrorCode.H3_SETTINGS_ERROR,
                        "Received a settings key that is reserved for HTTP/2.", true);
                return null;
            }

            payLoadLength -= keyLen;
            if (payLoadLength == 0)
            {
                ConnectionError(ctx, Http3ErrorCode.H3_FRAME_ERROR,
                    "Received a settings key without a value.", true);
                return null;
            }

            int valueLen = NumBytesForVariableLengthInteger(@in.GetByte(@in.ReaderIndex));
            if (valueLen > payLoadLength)
            {
                ConnectionError(ctx, Http3ErrorCode.H3_FRAME_ERROR,
                        "Received an invalid settings frame length.", true);
                return null;
            }

            long value = ReadVariableLengthInteger(@in, valueLen);
            payLoadLength -= valueLen;

            if (settingsFrame.Put(key, value) != null)
            {
                // This must be treated as a connection error
                // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.4
                ConnectionError(ctx, Http3ErrorCode.H3_SETTINGS_ERROR,
                        "Received a duplicate settings key.", true);
                return null;
            }
        }

        return settingsFrame;
    }

    /**
     * Decode the header block into header fields.
     *
     * @param ctx {@link ChannelHandlerContext} for this handler.
     * @param headers to be populated by decode.
     * @param in {@link ByteBuf} containing the encode header block. It is assumed that the entire header block is
     *           contained in this buffer.
     * @param length Number of bytes in the passed buffer that represent the encoded header block.
     * @param trailer {@code true} if this is a trailer section.
     * @return {@code true} if the headers were decoded, {@code false} otherwise. A header block may not be decoded if
     * it is awaiting QPACK dynamic table updates.
     */
    private bool DecodeHeaders(IChannelHandlerContext ctx, IHttp3Headers headers, ByteBuf @in, int length,
                               bool trailer)
    {
        try
        {
            Http3HeadersSink sink = new Http3HeadersSink(headers, maxHeaderListSize, true, trailer);
            Debug.Assert(qpackAttributes != null);
            Debug.Assert(readResumptionListener != null);

            if (qpackDecoder.Decode(qpackAttributes,
                    ((IQuicStreamChannel) ctx.Channel).StreamId(), @in, length, sink.Accept, readResumptionListener.Run))
            {
                // Throws exception if detected any problem so far
                sink.Finish();
                return true;
            }

            readResumptionListener.Suspended();
        }
        catch (Http3Exception e)
        {
            ConnectionError(ctx, e.ErrorCode(), e.Message, true);
        }
        catch (QpackException)
        {
            // Must be treated as a connection error.
            ConnectionError(ctx, Http3ErrorCode.QPACK_DECOMPRESSION_FAILED,
                    "Decompression of header block failed.", true);
        }
        catch (Http3HeadersValidationException e)
        {
            error = true;
            ctx.FireExceptionCaught(e);
            // We should shutdown the stream with an error.
            // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-4.1.3
            Http3CodecUtils.StreamError(ctx, Http3ErrorCode.H3_MESSAGE_ERROR);
        }

        return false;
    }

    public Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        TaskCompletionSource promise = Http3CodecUtils.NewPromise();
        Debug.Assert(qpackAttributes != null);

        if (writeResumptionListener != null)
        {
            writeResumptionListener.Enqueue(msg, promise);
            return promise.Task;
        }

        if ((msg is IHttp3HeadersFrame || msg is IHttp3PushPromiseFrame) &&
                !qpackAttributes.DynamicTableDisabled() && !qpackAttributes.EncoderStreamAvailable())
        {
            writeResumptionListener = WriteResumptionListener.NewListener(ctx, this);
            writeResumptionListener.Enqueue(msg, promise);
            return promise.Task;
        }

        Write0(ctx, msg, promise);
        return promise.Task;
    }

    private void Write0(IChannelHandlerContext ctx, object msg, TaskCompletionSource promise)
    {
        try
        {
            if (msg is IHttp3DataFrame)
            {
                WriteDataFrame(ctx, (IHttp3DataFrame) msg, promise);
            }
            else if (msg is IHttp3HeadersFrame)
            {
                WriteHeadersFrame(ctx, (IHttp3HeadersFrame) msg, promise);
            }
            else if (msg is IHttp3CancelPushFrame)
            {
                WriteCancelPushFrame(ctx, (IHttp3CancelPushFrame) msg, promise);
            }
            else if (msg is IHttp3SettingsFrame)
            {
                WriteSettingsFrame(ctx, (IHttp3SettingsFrame) msg, promise);
            }
            else if (msg is IHttp3PushPromiseFrame)
            {
                WritePushPromiseFrame(ctx, (IHttp3PushPromiseFrame) msg, promise);
            }
            else if (msg is IHttp3GoAwayFrame)
            {
                WriteGoAwayFrame(ctx, (IHttp3GoAwayFrame) msg, promise);
            }
            else if (msg is IHttp3MaxPushIdFrame)
            {
                WriteMaxPushIdFrame(ctx, (IHttp3MaxPushIdFrame) msg, promise);
            }
            else if (msg is IHttp3UnknownFrame)
            {
                WriteUnknownFrame(ctx, (IHttp3UnknownFrame) msg, promise);
            }
            else
            {
                Unsupported(promise);
            }
        }
        finally
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    private static void WriteDataFrame(
            IChannelHandlerContext ctx, IHttp3DataFrame frame, TaskCompletionSource promise)
    {
        ByteBuf @out = ctx.Alloc().DirectBuffer(16);
        WriteVariableLengthInteger(@out, frame.Type());
        WriteVariableLengthInteger(@out, frame.Content.ReadableBytes);
        ByteBuf content = frame.Content.Retain();
        Http3CodecUtils.Write(ctx, Unpooled.WrappedUnmodifiableBuffer(@out, content), promise);
    }

    private void WriteHeadersFrame(IChannelHandlerContext ctx, IHttp3HeadersFrame frame, TaskCompletionSource promise)
    {
        Debug.Assert(qpackAttributes != null);
        IQuicStreamChannel channel = (IQuicStreamChannel) ctx.Channel;
        WriteDynamicFrame(ctx, frame.Type(), frame, (f, @out) =>
        {
            qpackEncoder.EncodeHeaders(qpackAttributes, @out, ctx.Alloc(), channel.StreamId(), f.Headers());
            return true;
        }, promise);
    }

    private static void WriteCancelPushFrame(
            IChannelHandlerContext ctx, IHttp3CancelPushFrame frame, TaskCompletionSource promise)
    {
        WriteFrameWithId(ctx, frame.Type(), frame.Id(), promise);
    }

    private static void WriteSettingsFrame(
            IChannelHandlerContext ctx, IHttp3SettingsFrame frame, TaskCompletionSource promise)
    {
        WriteDynamicFrame(ctx, frame.Type(), frame, (f, @out) =>
        {
            foreach (KeyValuePair<long, long> e in f)
            {
                long key = e.Key;
                if (Http3CodecUtils.IsReservedHttp2Setting(key))
                {
                    Http3Exception exception = new Http3Exception(Http3ErrorCode.H3_SETTINGS_ERROR,
                            "Received a settings key that is reserved for HTTP/2.");
                    promise.SetException(exception);
                    // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.8
                    Http3CodecUtils.ConnectionError(ctx, exception, false);
                    return false;
                }

                long value = e.Value;
                int keyLen = NumBytesForVariableLengthInteger(key);
                int valueLen = NumBytesForVariableLengthInteger(value);
                WriteVariableLengthInteger(@out, key, keyLen);
                WriteVariableLengthInteger(@out, value, valueLen);
            }

            return true;
        }, promise);
    }

    private static void WriteDynamicFrame<T>(IChannelHandlerContext ctx, long type, T frame,
                                             Func<T, ByteBuf, bool> writer,
                                             TaskCompletionSource promise) where T : IHttp3Frame
    {
        ByteBuf @out = ctx.Alloc().DirectBuffer();
        int initialWriterIndex = @out.WriterIndex;
        // Move 16 bytes forward as this is the maximum amount we could ever need for the type + payload length.
        int payloadStartIndex = initialWriterIndex + 16;
        @out.WriterIndex = payloadStartIndex;

        if (writer(frame, @out))
        {
            int finalWriterIndex = @out.WriterIndex;
            int payloadLength = finalWriterIndex - payloadStartIndex;
            int len = NumBytesForVariableLengthInteger((long) payloadLength);
            @out.WriterIndex = payloadStartIndex - len;
            WriteVariableLengthInteger(@out, payloadLength, len);

            int typeLength = NumBytesForVariableLengthInteger(type);
            int startIndex = payloadStartIndex - len - typeLength;
            @out.WriterIndex = startIndex;
            WriteVariableLengthInteger(@out, type, typeLength);

            @out.SetIndex(startIndex, finalWriterIndex);
            Http3CodecUtils.Write(ctx, @out, promise);
        }
        else
        {
            // We failed to encode, lets release the buffer so we dont leak.
            @out.Release();
        }
    }

    private void WritePushPromiseFrame(IChannelHandlerContext ctx, IHttp3PushPromiseFrame frame,
                                       TaskCompletionSource promise)
    {
        Debug.Assert(qpackAttributes != null);
        IQuicStreamChannel channel = (IQuicStreamChannel) ctx.Channel;
        WriteDynamicFrame(ctx, frame.Type(), frame, (f, @out) =>
        {
            long id = f.Id();
            WriteVariableLengthInteger(@out, id);
            qpackEncoder.EncodeHeaders(qpackAttributes, @out, ctx.Alloc(), channel.StreamId(), f.Headers());
            return true;
        }, promise);
    }

    private static void WriteGoAwayFrame(
            IChannelHandlerContext ctx, IHttp3GoAwayFrame frame, TaskCompletionSource promise)
    {
        WriteFrameWithId(ctx, frame.Type(), frame.Id(), promise);
    }

    private static void WriteMaxPushIdFrame(
            IChannelHandlerContext ctx, IHttp3MaxPushIdFrame frame, TaskCompletionSource promise)
    {
        WriteFrameWithId(ctx, frame.Type(), frame.Id(), promise);
    }

    private static void WriteFrameWithId(IChannelHandlerContext ctx, long type, long id, TaskCompletionSource promise)
    {
        ByteBuf @out = ctx.Alloc().DirectBuffer(24);
        WriteVariableLengthInteger(@out, type);
        WriteVariableLengthInteger(@out, NumBytesForVariableLengthInteger(id));
        WriteVariableLengthInteger(@out, id);
        Http3CodecUtils.Write(ctx, @out, promise);
    }

    private void WriteUnknownFrame(
            IChannelHandlerContext ctx, IHttp3UnknownFrame frame, TaskCompletionSource promise)
    {
        long type = frame.Type();
        if (Http3CodecUtils.IsReservedHttp2FrameType(type))
        {
            Http3Exception exception = new Http3Exception(Http3ErrorCode.H3_FRAME_UNEXPECTED,
                    "Reserved type for HTTP/2 send.");
            promise.SetException(exception);
            // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.8
            ConnectionError(ctx, exception.ErrorCode(), exception.Message, false);
            return;
        }

        if (!Http3CodecUtils.IsReservedFrameType(type))
        {
            Http3Exception exception = new Http3Exception(Http3ErrorCode.H3_FRAME_UNEXPECTED,
                    "Non reserved type for HTTP/3 send.");
            promise.SetException(exception);
            return;
        }

        ByteBuf @out = ctx.Alloc().DirectBuffer();
        WriteVariableLengthInteger(@out, type);
        WriteVariableLengthInteger(@out, frame.Content.ReadableBytes);
        ByteBuf content = frame.Content.Retain();
        Http3CodecUtils.Write(ctx, Unpooled.WrappedUnmodifiableBuffer(@out, content), promise);
    }

    private void InitReadResumptionListenerIfRequired(IChannelHandlerContext ctx)
    {
        if (readResumptionListener == null)
        {
            readResumptionListener = new ReadResumptionListener(ctx, this);
        }
    }

    private static void Unsupported(TaskCompletionSource promise)
    {
        promise.SetException(new NotSupportedException());
    }

    public Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress, CancellationToken cancellationToken)
    {
        return ctx.BindAsync(localAddress, cancellationToken);
    }

    public Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress,
                             EndPoint localAddress, CancellationToken cancellationToken)
    {
        return ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    public Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DisconnectAsync(cancellationToken);
    }

    public Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.CloseAsync(cancellationToken);
    }

    public Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DeregisterAsync(cancellationToken);
    }

    public void Read(IChannelHandlerContext ctx)
    {
        Debug.Assert(readResumptionListener != null);

        if (readResumptionListener.ReadRequested())
        {
            ctx.Read();
        }
    }

    public void Flush(IChannelHandlerContext ctx)
    {
        if (writeResumptionListener != null)
        {
            writeResumptionListener.EnqueueFlush();
        }
        else
        {
            ctx.Flush();
        }
    }

    private sealed class ReadResumptionListener
    {
        private const int STATE_SUSPENDED = 0b1000_0000;
        private const int STATE_READ_PENDING = 0b0100_0000;
        private const int STATE_READ_COMPLETE_PENDING = 0b0010_0000;

        private readonly IChannelHandlerContext ctx;
        private readonly Http3FrameCodec codec;
        private byte state;

        internal ReadResumptionListener(IChannelHandlerContext ctx, Http3FrameCodec codec)
        {
            this.ctx = ctx;
            this.codec = codec;
            Debug.Assert(codec.qpackAttributes != null);

            if (!codec.qpackAttributes.DynamicTableDisabled() && !codec.qpackAttributes.DecoderStreamAvailable())
            {
                codec.qpackAttributes.WhenDecoderStreamAvailable(OperationComplete);
            }
        }

        internal void Suspended()
        {
            Debug.Assert(!codec.qpackAttributes.DynamicTableDisabled());
            SetState(STATE_SUSPENDED);
        }

        internal bool ReadCompleted()
        {
            if (HasState(STATE_SUSPENDED))
            {
                SetState(STATE_READ_COMPLETE_PENDING);
                return false;
            }

            return true;
        }

        internal bool ReadRequested()
        {
            if (HasState(STATE_SUSPENDED))
            {
                SetState(STATE_READ_PENDING);
                return false;
            }

            return true;
        }

        internal bool IsSuspended()
        {
            return HasState(STATE_SUSPENDED);
        }

        public void OperationComplete(Task<IQuicStreamChannel> future)
        {
            if (future.IsCompletedSuccessfully)
            {
                Run();
            }
            else
            {
                ctx.FireExceptionCaught(Http3CodecUtils.Cause(future));
            }
        }

        public void Run()
        {
            // If we are not on the handler's event executor loop, schedule resume to execute there.
            if (ctx.Executor.InEventLoop())
            {
                Resume();
            }
            else
            {
                ctx.Executor.Execute(Run);
            }
        }

        private void Resume()
        {
            UnsetState(STATE_SUSPENDED);

            try
            {
                codec.ChannelRead(ctx, Unpooled.EmptyBuffer);

                if (HasState(STATE_READ_COMPLETE_PENDING))
                {
                    UnsetState(STATE_READ_COMPLETE_PENDING);
                    codec.ChannelReadComplete(ctx);
                }

                if (HasState(STATE_READ_PENDING))
                {
                    UnsetState(STATE_READ_PENDING);
                    codec.Read(ctx);
                }
            }
            catch (Exception e)
            {
                ctx.FireExceptionCaught(e);
            }
        }

        private void SetState(int toSet)
        {
            state = (byte) (state | toSet);
        }

        private bool HasState(int toCheck)
        {
            return (state & toCheck) == toCheck;
        }

        private void UnsetState(int toUnset)
        {
            state = (byte) (state & ~toUnset);
        }
    }

    private sealed class WriteResumptionListener
    {
        private static readonly object FLUSH = new object();
        private readonly PendingWriteQueue queue;
        private readonly IChannelHandlerContext ctx;
        private readonly Http3FrameCodec codec;

        private WriteResumptionListener(IChannelHandlerContext ctx, Http3FrameCodec codec)
        {
            this.ctx = ctx;
            this.codec = codec;
            queue = new PendingWriteQueue(ctx);
        }

        public void OperationComplete(Task<IQuicStreamChannel> future)
        {
            // If we are not on the handler's event executor loop, schedule drain to execute there.
            if (ctx.Executor.InEventLoop())
            {
                Drain();
            }
            else
            {
                ctx.Executor.Execute(Drain);
            }
        }

        internal void Enqueue(object msg, TaskCompletionSource promise)
        {
            Debug.Assert(ctx.Channel.EventLoop.InEventLoop());
            // Touch the message to allow easier debugging of memory leaks
            ReferenceCountUtil.Touch(msg);
            queue.Add(msg, promise);
        }

        internal void EnqueueFlush()
        {
            Debug.Assert(ctx.Channel.EventLoop.InEventLoop());
            queue.Add(FLUSH, Http3CodecUtils.NewPromise());
        }

        internal void Drain()
        {
            Debug.Assert(ctx.Channel.EventLoop.InEventLoop());
            bool flushSeen = false;

            try
            {
                for (;;)
                {
                    object entry = queue.Current;
                    if (entry == null)
                    {
                        break;
                    }

                    if (entry == FLUSH)
                    {
                        flushSeen = true;
                        queue.Remove().TrySetResult();
                    }
                    else
                    {
                        // Retain the entry as remove() will call release() as well.
                        codec.Write0(ctx, ReferenceCountUtil.Retain(entry), queue.Remove());
                    }
                }

                // indicate that writes do not need to be enqueued. As we are on the eventloop, no other writes can
                // happen while we are draining, hence we would not write out of order.
                codec.writeResumptionListener = null;
            }
            finally
            {
                if (flushSeen)
                {
                    codec.Flush(ctx);
                }
            }
        }

        internal static WriteResumptionListener NewListener(IChannelHandlerContext ctx, Http3FrameCodec codec)
        {
            WriteResumptionListener listener = new WriteResumptionListener(ctx, codec);
            Debug.Assert(codec.qpackAttributes != null);
            codec.qpackAttributes.WhenEncoderStreamAvailable(listener.OperationComplete);
            return listener;
        }
    }

    /**
     * A factory for creating codec for HTTP3 frames.
     */
    internal interface IHttp3FrameCodecFactory
    {
        /**
         * Creates a new codec instance for the passed {@code streamType}.
         *
         * @param validator for the frames.
         * @param encodeState for the request stream.
         * @param decodeState for the request stream.
         * @return new codec instance for the passed {@code streamType}.
         */
        IChannelHandler NewCodec(IHttp3FrameTypeValidator validator, IHttp3RequestStreamCodecState encodeState,
                                 IHttp3RequestStreamCodecState decodeState,
                                 Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator);
    }
}
