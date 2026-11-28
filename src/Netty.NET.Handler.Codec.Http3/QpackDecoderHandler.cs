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

using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Codec.Http3.Http3CodecUtils;
using static Netty.NET.Handler.Codec.Http3.Http3ErrorCode;
using static Netty.NET.Handler.Codec.Http3.QpackUtil;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is internal sealed; ByteToMessageDecoder is codec-base's (decode(ctx, in, out)
// is Decode(IChannelHandlerContext, ByteBuf, IList<object>)). The checked QpackException needs no throws clause.
// ChannelInputShutdownEvent is the Transport's (Netty.NET.Transport.Channels.Sockets). The instruction byte is the CLR
// byte; the masks test the same bits, and the "Unknown decoder instruction" text prints Java's signed value through
// (sbyte); numbers print with the invariant culture.
internal sealed class QpackDecoderHandler : ByteToMessageDecoder
{

    private bool discard;
    private readonly QpackEncoder qpackEncoder;

    internal QpackDecoderHandler(QpackEncoder qpackEncoder)
    {
        this.qpackEncoder = qpackEncoder;
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        if (!@in.IsReadable())
        {
            return;
        }

        if (discard)
        {
            @in.SkipBytes(@in.ReadableBytes);
            return;
        }

        byte b = @in.GetByte(@in.ReaderIndex);

        // 4.4.1. Section Acknowledgment
        //
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 1 |      Stream ID (7+)       |
        // +---+---------------------------+
        if ((b & 0b1000_0000) == 0b1000_0000)
        {
            try
            {
                long streamId = QpackUtil.DecodePrefixedInteger(@in, 7);
                if (streamId < 0)
                {
                    // Not enough readable bytes
                    return;
                }

                qpackEncoder.SectionAcknowledgment(streamId);
            }
            catch (QpackException e)
            {
                ConnectionError(ctx, new Http3Exception(QPACK_DECODER_STREAM_ERROR,
                                "Section acknowledgment decode failed.", e), true);
            }

            return;
        }

        // 4.4.2. Stream Cancellation
        //
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 1 |     Stream ID (6+)    |
        // +---+---+-----------------------+
        if ((b & 0b1100_0000) == 0b0100_0000)
        {
            try
            {
                long streamId = QpackUtil.DecodePrefixedInteger(@in, 6);
                if (streamId < 0)
                {
                    // Not enough readable bytes
                    return;
                }

                qpackEncoder.StreamCancellation(streamId);
            }
            catch (QpackException e)
            {
                ConnectionError(ctx, new Http3Exception(QPACK_DECODER_STREAM_ERROR,
                        "Stream cancellation decode failed.", e), true);
            }

            return;
        }

        // 4.4.3. Insert Count Increment
        //
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 |     Increment (6+)    |
        // +---+---+-----------------------+
        if ((b & 0b1100_0000) == 0b0000_0000)
        {
            int increment = DecodePrefixedIntegerAsInt(@in, 6);
            if (increment == 0)
            {
                discard = true;
                // Zero is not allowed as an increment
                // https://www.rfc-editor.org/rfc/rfc9204.html#name-insert-count-increment
                // Increment is an unsigned integer, so only 0 is the invalid value.
                // https://www.rfc-editor.org/rfc/rfc7541#section-5
                ConnectionError(ctx, QPACK_DECODER_STREAM_ERROR,
                        "Invalid increment '" + increment.ToString(CultureInfo.InvariantCulture) + "'.",  false);
                return;
            }

            if (increment < 0)
            {
                // Not enough readable bytes
                return;
            }

            try
            {
                qpackEncoder.InsertCountIncrement(increment);
            }
            catch (QpackException e)
            {
                ConnectionError(ctx, new Http3Exception(QPACK_DECODER_STREAM_ERROR,
                        "Insert count increment decode failed.", e), true);
            }

            return;
        }

        // unknown frame
        discard = true;
        ConnectionError(ctx, QPACK_DECODER_STREAM_ERROR,
                "Unknown decoder instruction '" + ((sbyte) b).ToString(CultureInfo.InvariantCulture) + "'.",  false);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ctx.FireChannelReadComplete();

        // QPACK streams should always be processed, no matter what the user is doing in terms of configuration
        // and AUTO_READ.
        Http3CodecUtils.ReadIfNoAutoRead(ctx);
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is ChannelInputShutdownEvent)
        {
            // See https://www.rfc-editor.org/rfc/rfc9204.html#name-encoder-and-decoder-streams
            Http3CodecUtils.CriticalStreamClosed(ctx);
        }

        ctx.FireUserEventTriggered(evt);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        // See https://www.rfc-editor.org/rfc/rfc9204.html#name-encoder-and-decoder-streams
        Http3CodecUtils.CriticalStreamClosed(ctx);
        ctx.FireChannelInactive();
    }
}
