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
using System.Diagnostics;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http3.Http3CodecUtils;
using static Netty.NET.Handler.Codec.Http3.Http3ErrorCode;
using static Netty.NET.Handler.Codec.Http3.QpackUtil;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: ByteToMessageDecoder is codec-base's (decode(ctx, in, __) is Decode(IChannelHandlerContext, ByteBuf,
// IList<object>), keeping the parameter name __). The @Nullable Long maxTableCapacity is a long?; the other
// @Nullable (org.jetbrains.annotations) annotations have no counterpart. QuicStreamChannel is codec-quic's
// IQuicStreamChannel. The checked QpackException needs no throws clause; assert is Debug.Assert.
// ChannelInputShutdownEvent is the Transport's (Netty.NET.Transport.Channels.Sockets). The instruction byte is the
// CLR byte; the masks test the same bits, and the "Unknown encoder instruction" text prints Java's signed value
// through (sbyte) with the invariant culture. new AsciiString(buf, false) is the Common AsciiString(byte[], bool).
internal sealed class QpackEncoderHandler : ByteToMessageDecoder
{
    private static readonly QpackException INVALID_LENGTH_STRING_LITERAL =
            QpackException.NewStatic(typeof(QpackEncoderHandler), "decodeStringLiteral(...)",
                    "QPACK - invalid length for STRING_LITERAL");
    private static readonly QpackException STRING_LITERAL_TOO_LARGE =
            QpackException.NewStatic(typeof(QpackEncoderHandler), "checkStringLiteralLength(...)",
                    "QPACK - string literal exceeds the maximum dynamic table capacity");
    private readonly QpackHuffmanDecoder huffmanDecoder;
    private readonly QpackDecoder qpackDecoder;
    // No single name/value string literal can ever be inserted into the dynamic table if it alone already
    // exceeds the maximum table capacity, so this also bounds the amount of data that decode(...) will ever
    // buffer for a single string literal while waiting for more bytes to arrive.
    private readonly long maxTableCapacity;
    private bool discard;

    internal QpackEncoderHandler(long? maxTableCapacity, QpackDecoder qpackDecoder)
    {
        this.maxTableCapacity = CheckInRange(
                maxTableCapacity == null ? 0 : maxTableCapacity.Value, 0, MAX_UNSIGNED_INT, "maxTableCapacity");
        huffmanDecoder = new QpackHuffmanDecoder();
        this.qpackDecoder = qpackDecoder;
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> __)
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

        // 4.3.1. Set Dynamic Table Capacity
        //
        //   0   1   2   3   4   5   6   7
        //+---+---+---+---+---+---+---+---+
        //| 0 | 0 | 1 |   Capacity (5+)   |
        //+---+---+---+-------------------+
        if ((b & 0b1110_0000) == 0b0010_0000)
        {
            // new capacity
            try
            {
                long capacity = QpackUtil.DecodePrefixedInteger(@in, 5);
                if (capacity < 0)
                {
                    // Not enough readable bytes
                    return;
                }

                qpackDecoder.SetDynamicTableCapacity(capacity);
            }
            catch (QpackException e)
            {
                HandleDecodeFailure(ctx, e, "setDynamicTableCapacity failed.");
            }

            return;
        }

        QpackAttributes qpackAttributes = Http3.GetQpackAttributes(ctx.Channel.Parent);
        Debug.Assert(qpackAttributes != null);
        if (!qpackAttributes.DynamicTableDisabled() && !qpackAttributes.DecoderStreamAvailable())
        {
            // We need the decoder stream to update the decoder with these instructions.
            return;
        }

        IQuicStreamChannel decoderStream = qpackAttributes.DecoderStream();

        // 4.3.2. Insert With Name Reference
        //
        //      0   1   2   3   4   5   6   7
        //   +---+---+---+---+---+---+---+---+
        //   | 1 | T |    Name Index (6+)    |
        //   +---+---+-----------------------+
        //   | H |     Value Length (7+)     |
        //   +---+---------------------------+
        //   |  Value String (Length bytes)  |
        //   +-------------------------------+
        if ((b & 0b1000_0000) == 0b1000_0000)
        {
            int readerIndex = @in.ReaderIndex;
            // T == 1 implies static table index.
            // https://www.rfc-editor.org/rfc/rfc9204.html#name-insert-with-name-reference
            bool isStaticTableIndex = QpackUtil.FirstByteEquals(@in, 0b1100_0000);
            int nameIdx = DecodePrefixedIntegerAsInt(@in, 6);
            if (nameIdx < 0)
            {
                // Not enough readable bytes
                return;
            }

            try
            {
                ICharSequence value = DecodeLiteralValue(@in);
                if (value == null)
                {
                    // Reset readerIndex
                    @in.ReaderIndex = readerIndex;
                    // Not enough readable bytes
                    return;
                }

                qpackDecoder.InsertWithNameReference(decoderStream, isStaticTableIndex, nameIdx,
                        value);
            }
            catch (QpackException e)
            {
                HandleDecodeFailure(ctx, e, "insertWithNameReference failed.");
            }

            return;
        }

        // 4.3.3. Insert With Literal Name
        //
        //      0   1   2   3   4   5   6   7
        //   +---+---+---+---+---+---+---+---+
        //   | 0 | 1 | H | Name Length (5+)  |
        //   +---+---+---+-------------------+
        //   |  Name String (Length bytes)   |
        //   +---+---------------------------+
        //   | H |     Value Length (7+)     |
        //   +---+---------------------------+
        //   |  Value String (Length bytes)  |
        //   +-------------------------------+
        if ((b & 0b1100_0000) == 0b0100_0000)
        {
            int readerIndex = @in.ReaderIndex;
            bool nameHuffEncoded = QpackUtil.FirstByteEquals(@in, 0b0110_0000);
            int nameLength = DecodePrefixedIntegerAsInt(@in, 5);
            if (nameLength < 0)
            {
                // Reset readerIndex
                @in.ReaderIndex = readerIndex;
                // Not enough readable bytes
                return;
            }

            try
            {
                CheckStringLiteralLength(nameHuffEncoded, nameLength);

                if (@in.ReadableBytes < nameLength)
                {
                    // Reset readerIndex
                    @in.ReaderIndex = readerIndex;
                    // Not enough readable bytes
                    return;
                }

                ICharSequence name = DecodeStringLiteral(@in, nameHuffEncoded, nameLength);
                ICharSequence value = DecodeLiteralValue(@in);
                if (value == null)
                {
                    // Reset readerIndex
                    @in.ReaderIndex = readerIndex;
                    // Not enough readable bytes
                    return;
                }

                qpackDecoder.InsertLiteral(decoderStream, name, value);
            }
            catch (QpackException e)
            {
                HandleDecodeFailure(ctx, e, "insertLiteral failed.");
            }

            return;
        }

        // 4.3.4. Duplicate
        //
        //      0   1   2   3   4   5   6   7
        //   +---+---+---+---+---+---+---+---+
        //   | 0 | 0 | 0 |    Index (5+)     |
        //   +---+---+---+-------------------+
        if ((b & 0b1110_0000) == 0b0000_0000)
        {
            int readerIndex = @in.ReaderIndex;
            int index = DecodePrefixedIntegerAsInt(@in, 5);
            if (index < 0)
            {
                // Reset readerIndex
                @in.ReaderIndex = readerIndex;
                // Not enough readable bytes
                return;
            }

            try
            {
                qpackDecoder.Duplicate(decoderStream, index);
            }
            catch (QpackException e)
            {
                HandleDecodeFailure(ctx, e, "duplicate failed.");
            }

            return;
        }

        discard = true;
        Http3CodecUtils.ConnectionError(ctx, Http3ErrorCode.QPACK_ENCODER_STREAM_ERROR,
                "Unknown encoder instruction '" + ((sbyte) b).ToString(CultureInfo.InvariantCulture) + "'.",  false);
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

    private void HandleDecodeFailure(IChannelHandlerContext ctx, QpackException cause, string message)
    {
        discard = true;
        ConnectionError(ctx, new Http3Exception(QPACK_ENCODER_STREAM_ERROR, message, cause), true);
    }

    private ICharSequence DecodeLiteralValue(ByteBuf @in)
    {
        int readerIndex = @in.ReaderIndex;
        int valueLength = DecodePrefixedIntegerAsInt(@in, 7);
        if (valueLength < 0)
        {
            // Not enough readable bytes
            return null;
        }

        bool valueHuffEncoded = QpackUtil.ByteEquals(@in, readerIndex, 0b1000_0000);
        CheckStringLiteralLength(valueHuffEncoded, valueLength);
        if (@in.ReadableBytes < valueLength)
        {
            // Not enough readable bytes
            return null;
        }

        return DecodeStringLiteral(@in, valueHuffEncoded, valueLength);
    }

    private void CheckStringLiteralLength(bool huffmanEncoded, int length)
    {
        // A string literal that is larger than the maximum dynamic table capacity can never be inserted into the
        // dynamic table, so there is no reason to buffer it. This also guards against a peer declaring an
        // (attacker-controlled) length of up to Integer.MAX_VALUE and forcing this handler to accumulate up to
        // ~2 GiB per string literal before giving up.
        //
        // The `maxTableCapacity` is the decoded bound. If the value is huffman encoded, then
        // inflate the limit by the max possible huffman expansion.
        // Actual, precise table size is checked in `QpackDecoderDynamicTable.add()`.
        long limit = huffmanEncoded ? (maxTableCapacity * 8 + 4) / 5 : maxTableCapacity;
        if (length > limit)
        {
            throw STRING_LITERAL_TOO_LARGE;
        }
    }

    private ICharSequence DecodeStringLiteral(ByteBuf @in, bool huffmanEncoded, int length)
    {
        if (huffmanEncoded)
        {
            return huffmanDecoder.Decode(@in, length);
        }
        if (@in.ReadableBytes < length)
        {
            throw INVALID_LENGTH_STRING_LITERAL;
        }

        byte[] buf = new byte[length];
        @in.ReadBytes(buf);

        return new AsciiString(buf, false);
    }
}
