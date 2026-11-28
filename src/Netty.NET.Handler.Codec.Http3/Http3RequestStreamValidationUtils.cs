/*
 * Copyright 2021 The Netty Project
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
using System.Globalization;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpUtil;
using static Netty.NET.Handler.Codec.Http3.Http3ErrorCode;
using static Netty.NET.Handler.Codec.Http3.Http3FrameValidationUtils;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is an internal static class. QuicStreamChannel is codec-quic's
// IQuicStreamChannel; ChannelPromise is TaskCompletionSource and promise.setFailure(e) is SetException; ctx.close() is
// CloseAsync, whose Task is not awaited as Java ignores the future. BooleanSupplier is Func<bool>. The lambda listener
// of whenDecoderStreamAvailable is an Action<Task<IQuicStreamChannel>>, future.isSuccess() IsCompletedSuccessfully.
// HttpUtil.normalizeAndGetContentLength is codec-http's NormalizeAndGetContentLength over the IList<ICharSequence> of
// getAll. throws Http3Exception has no counterpart; the numbers of the messages print with the invariant culture.
internal static class Http3RequestStreamValidationUtils
{
    internal const long CONTENT_LENGTH_NOT_MODIFIED = -1;
    internal const long INVALID_FRAME_READ = -2;

    // CLR: the private constructor of the final class is the static class itself.
    // No instances

    /**
     * Validate write of the passed {@link Http3RequestStreamFrame} for a client and takes appropriate error handling
     * for invalid frames.
     *
     * @param frame                  to validate.
     * @param promise                for the write.
     * @param ctx                    for the handler.
     * @param goAwayReceivedSupplier for the channel.
     * @param encodeState            for the stream.
     * @return {@code true} if the frame is valid.
     */
    internal static bool ValidateClientWrite(IHttp3RequestStreamFrame frame, TaskCompletionSource promise,
                                             IChannelHandlerContext ctx, Func<bool> goAwayReceivedSupplier,
                                             IHttp3RequestStreamCodecState encodeState)
    {
        if (goAwayReceivedSupplier() && !encodeState.Started())
        {
            string type = StringUtil.SimpleClassName(frame);
            ReferenceCountUtil.Release(frame);
            promise.SetException(new Http3Exception(Http3ErrorCode.H3_FRAME_UNEXPECTED,
                    "Frame of type " + type + " unexpected as we received a GOAWAY already."));
            _ = ctx.CloseAsync();
            return false;
        }

        if (frame is IHttp3PushPromiseFrame)
        {
            // Only supported on the server.
            // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-4.1
            FrameTypeUnexpected(promise, frame);
            return false;
        }

        return true;
    }

    internal static long ValidateHeaderFrameRead(IHttp3HeadersFrame headersFrame, IChannelHandlerContext ctx,
                                                 IHttp3RequestStreamCodecState decodeState)
    {
        if (headersFrame.Headers().Contains(HttpHeaderNames.CONNECTION))
        {
            HeaderUnexpected(ctx, headersFrame, "connection header included");
            return INVALID_FRAME_READ;
        }

        ICharSequence value = headersFrame.Headers().Get(HttpHeaderNames.TE);
        if (value != null && !HttpHeaderValues.TRAILERS.Equals(value))
        {
            HeaderUnexpected(ctx, headersFrame, "te header field included with invalid value: " + value);
            return INVALID_FRAME_READ;
        }

        if (decodeState.ReceivedFinalHeaders())
        {
            long length = NormalizeAndGetContentLength(
                    headersFrame.Headers().GetAll(HttpHeaderNames.CONTENT_LENGTH), false, true);
            if (length != CONTENT_LENGTH_NOT_MODIFIED)
            {
                headersFrame.Headers().SetLong(HttpHeaderNames.CONTENT_LENGTH, length);
            }

            return length;
        }

        return CONTENT_LENGTH_NOT_MODIFIED;
    }

    internal static long ValidateDataFrameRead(IHttp3DataFrame dataFrame, IChannelHandlerContext ctx,
                                               long expectedLength, long seenLength, bool clientHeadRequest)
    {
        try
        {
            return VerifyContentLength(dataFrame.Content.ReadableBytes, expectedLength, seenLength, false,
                    clientHeadRequest);
        }
        catch (Http3Exception e)
        {
            ReferenceCountUtil.Release(dataFrame);
            FailStream(ctx, e);
            return INVALID_FRAME_READ;
        }
    }

    internal static bool ValidateOnStreamClosure(IChannelHandlerContext ctx, long expectedLength, long seenLength,
                                                 bool clientHeadRequest)
    {
        try
        {
            VerifyContentLength(0, expectedLength, seenLength, true, clientHeadRequest);
            return true;
        }
        catch (Http3Exception e)
        {
            ctx.FireExceptionCaught(e);
            Http3CodecUtils.StreamError(ctx, e.ErrorCode());
            return false;
        }
    }

    internal static void SendStreamAbandonedIfRequired(IChannelHandlerContext ctx, QpackAttributes qpackAttributes,
                                                       QpackDecoder qpackDecoder,
                                                       IHttp3RequestStreamCodecState decodeState)
    {
        if (!qpackAttributes.DynamicTableDisabled() && !decodeState.Terminated())
        {
            long streamId = ((IQuicStreamChannel) ctx.Channel).StreamId();

            if (qpackAttributes.DecoderStreamAvailable())
            {
                qpackDecoder.StreamAbandoned(qpackAttributes.DecoderStream(), streamId);
            }
            else
            {
                qpackAttributes.WhenDecoderStreamAvailable(future =>
                {
                    if (future.IsCompletedSuccessfully)
                    {
                        qpackDecoder.StreamAbandoned(qpackAttributes.DecoderStream(), streamId);
                    }
                });
            }
        }
    }

    private static void HeaderUnexpected(IChannelHandlerContext ctx, IHttp3RequestStreamFrame frame, string msg)
    {
        // We should close the stream.
        // See https://quicwg.org/base-drafts/draft-ietf-quic-http.html#section-4.1.1
        ReferenceCountUtil.Release(frame);
        FailStream(ctx, new Http3Exception(H3_MESSAGE_ERROR, msg));
    }

    private static void FailStream(IChannelHandlerContext ctx, Http3Exception cause)
    {
        ctx.FireExceptionCaught(cause);
        Http3CodecUtils.StreamError(ctx, cause.ErrorCode());
    }

    // See https://tools.ietf.org/html/draft-ietf-quic-http-34#section-4.1.3
    private static long VerifyContentLength(int length, long expectedLength, long seenLength, bool end,
                                            bool clientHeadRequest)
    {
        seenLength += length;
        if (expectedLength != -1 && (seenLength > expectedLength ||
                (!clientHeadRequest && end && seenLength != expectedLength)))
        {
            throw new Http3Exception(
                    H3_MESSAGE_ERROR, "Expected content-length " + expectedLength.ToString(CultureInfo.InvariantCulture) +
                    " != " + seenLength.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return seenLength;
    }
}
