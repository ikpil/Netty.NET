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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Quic;
using static Netty.NET.Handler.Codec.Http3.Http3CodecUtils;
using static Netty.NET.Handler.Codec.Http3.QpackUtil;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is internal sealed and its package-private members are internal; decode stays
// public. QuicStreamChannel is codec-quic's IQuicStreamChannel. The checked QpackException needs no throws clause;
// IllegalArgumentException(e) is ArgumentException with the cause's toString() text as its message; assert is
// Debug.Assert. BiConsumer<CharSequence, CharSequence> is Action<ICharSequence, ICharSequence> (callers pass
// Http3HeadersSink.Accept as a method group) and sink.accept(...) is an invocation of it; Runnable whenDecoded is an
// Action. IntObjectHashMap<List<BlockedStream>> is Dictionary<int, List<BlockedStream>> (owner decision 2026-11-19 for
// the primitive maps): computeIfAbsent is TryGetValue plus Add, and releaseBlockedStream removes the entry of the
// list it changed and returns at once, which Dictionary allows during enumeration (.NET Core 3.0+). The decoder
// stream writes are closeOnFailure(stream, stream.WriteAndFlushAsync(...)) (Http3CodecUtils.CloseOnFailure takes the
// channel, as a Task has no channel()). The instruction bytes are CLR bytes; the masks test the same bits as Java's
// signed bytes. Math.floorDiv is QpackUtil.FloorDiv. new AsciiString(buf, false) is the Common AsciiString(byte[],
// bool). The private static nested class BlockedStream keeps its fields internal for the decoder.
internal sealed class QpackDecoder
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(QpackDecoder));
    private static readonly QpackException DYNAMIC_TABLE_CAPACITY_EXCEEDS_MAX =
            QpackException.NewStatic(typeof(QpackDecoder), "setDynamicTableCapacity(...)",
                    "QPACK - decoder dynamic table capacity exceeds max capacity.");
    private static readonly QpackException HEADER_ILLEGAL_INDEX_VALUE =
            QpackException.NewStatic(typeof(QpackDecoder), "decodeIndexed(...)", "QPACK - illegal index value");
    private static readonly QpackException NAME_ILLEGAL_INDEX_VALUE =
            QpackException.NewStatic(typeof(QpackDecoder), "decodeLiteralWithNameRef(...)",
                    "QPACK - illegal name index value");
    private static readonly QpackException INVALID_REQUIRED_INSERT_COUNT =
            QpackException.NewStatic(typeof(QpackDecoder), "decodeRequiredInsertCount(...)",
                    "QPACK - invalid required insert count");
    private static readonly QpackException INVALID_LENGTH_ENCODED_LITERAL =
            QpackException.NewStatic(typeof(QpackDecoder), "decodeHuffmanEncodedLiteral(...)",
                    "QPACK - invalid length for LITERAL");
    private static readonly QpackException MAX_BLOCKED_STREAMS_EXCEEDED =
            QpackException.NewStatic(typeof(QpackDecoder), "shouldWaitForDynamicTableUpdates(...)",
                    "QPACK - exceeded max blocked streams");
    private static readonly QpackException BLOCKED_STREAM_RESUMPTION_FAILED =
            QpackException.NewStatic(typeof(QpackDecoder), "sendInsertCountIncrementIfRequired(...)",
                    "QPACK - failed to resume a blocked stream");

    private static readonly QpackException UNKNOWN_TYPE =
            QpackException.NewStatic(typeof(QpackDecoder), "decode(...)", "QPACK - unknown type");

    private readonly QpackHuffmanDecoder huffmanDecoder;
    private readonly QpackDecoderDynamicTable dynamicTable;
    private readonly long maxTableCapacity;
    private readonly int maxBlockedStreams;
    private readonly IQpackDecoderStateSyncStrategy stateSyncStrategy;
    /**
     * Hashmap with key as the required insert count to unblock the stream and the value a {@link List} of
     * {@link BlockedStream} to invoke when the stream can be unblocked.
     */
    private readonly Dictionary<int, List<BlockedStream>> blockedStreams;

    private readonly long maxEntries;
    private readonly long fullRange;
    private int blockedStreamsCount;
    private long lastAckInsertCount;

    internal QpackDecoder(long maxTableCapacity, int maxBlockedStreams)
        : this(maxTableCapacity, maxBlockedStreams, new QpackDecoderDynamicTable(),
               IQpackDecoderStateSyncStrategy.AckEachInsert())
    {
    }

    internal QpackDecoder(long maxTableCapacity, int maxBlockedStreams,
                          QpackDecoderDynamicTable dynamicTable, IQpackDecoderStateSyncStrategy stateSyncStrategy)
    {
        huffmanDecoder = new QpackHuffmanDecoder();
        this.maxTableCapacity = maxTableCapacity;
        this.maxBlockedStreams = maxBlockedStreams;
        this.stateSyncStrategy = stateSyncStrategy;
        blockedStreams = new Dictionary<int, List<BlockedStream>>(Math.Min(16, maxBlockedStreams));
        this.dynamicTable = dynamicTable;
        maxEntries = QpackUtil.MaxEntries(maxTableCapacity);

        try
        {
            fullRange = ToIntOrThrow(2 * maxEntries);
        }
        catch (QpackException e)
        {
            throw new ArgumentException(ThrowableUtil.ThrowableToString(e), e);
        }
    }

    /**
     * Decode the header block and add these to the {@link BiConsumer}. This method assumes the entire header block is
     * contained in {@code in}. However, this method may not be able to decode the header block if the QPACK dynamic
     * table does not contain all entries required to decode the header block.
     * See <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-blocked-streams">blocked streams</a>.
     * In such a case, this method will return {@code false} and would invoke {@code whenDecoded} when the stream is
     * unblocked and the header block is completely decoded.
     *
     * @param qpackAttributes {@link QpackAttributes} for the channel.
     * @param streamId for the stream on which this header block was received.
     * @param in {@link ByteBuf} containing the header block.
     * @param length Number of bytes to be read from {@code in}
     * @param sink {@link BiConsumer} to
     * @param whenDecoded {@link Runnable} to invoke when a blocked decode finishes decoding.
     * @return {@code true} if the headers were decoded.
     */
    public bool Decode(QpackAttributes qpackAttributes, long streamId, ByteBuf @in,
                       int length, Action<ICharSequence, ICharSequence> sink, Action whenDecoded)
    {
        int initialReaderIdx = @in.ReaderIndex;
        int requiredInsertCount = DecodeRequiredInsertCount(qpackAttributes, @in);
        if (ShouldWaitForDynamicTableUpdates(requiredInsertCount))
        {
            blockedStreamsCount++;

            if (!blockedStreams.TryGetValue(requiredInsertCount, out List<BlockedStream> streams))
            {
                streams = new List<BlockedStream>(2);
                blockedStreams.Add(requiredInsertCount, streams);
            }

            streams.Add(new BlockedStream(streamId, whenDecoded));
            @in.ReaderIndex = initialReaderIdx;
            return false;
        }

        @in = @in.ReadSlice(length - (@in.ReaderIndex - initialReaderIdx));
        int @base = DecodeBase(@in, requiredInsertCount);

        while (@in.IsReadable())
        {
            byte b = @in.GetByte(@in.ReaderIndex);
            if (IsIndexed(b))
            {
                DecodeIndexed(@in, sink, @base);
            }
            else if (IsIndexedWithPostBase(b))
            {
                DecodeIndexedWithPostBase(@in, sink, @base);
            }
            else if (IsLiteralWithNameRef(b))
            {
                DecodeLiteralWithNameRef(@in, sink, @base);
            }
            else if (IsLiteralWithPostBaseNameRef(b))
            {
                DecodeLiteralWithPostBaseNameRef(@in, sink, @base);
            }
            else if (IsLiteral(b))
            {
                DecodeLiteral(@in, sink);
            }
            else
            {
                throw UNKNOWN_TYPE;
            }
        }

        if (requiredInsertCount > 0)
        {
            Debug.Assert(!qpackAttributes.DynamicTableDisabled());
            Debug.Assert(qpackAttributes.DecoderStreamAvailable());

            stateSyncStrategy.SectionAcknowledged(requiredInsertCount);
            ByteBuf sectionAck = qpackAttributes.DecoderStream().Alloc().Buffer(8);
            EncodePrefixedInteger(sectionAck, 0b1000_0000, 7, streamId);
            CloseOnFailure(qpackAttributes.DecoderStream(),
                    qpackAttributes.DecoderStream().WriteAndFlushAsync(sectionAck));
        }

        return true;
    }

    /**
     * Updates dynamic table capacity corresponding to the
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-set-dynamic-table-capacity">
     *     encoder instruction.</a>
     *
     * @param capacity New capacity.
     * @throws  QpackException If the capacity update fails.
     */
    internal void SetDynamicTableCapacity(long capacity)
    {
        if (capacity > maxTableCapacity)
        {
            throw DYNAMIC_TABLE_CAPACITY_EXCEEDS_MAX;
        }

        dynamicTable.SetCapacity(capacity);
    }

    /**
     * Inserts a header field with a name reference corresponding to the
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-insert-with-name-reference">
     *     encoder instruction.</a>
     *
     *  @param qpackDecoderStream {@link QuicStreamChannel} for the QPACK decoder stream.
     *  @param staticTableRef {@code true} if the name reference is to the static table, {@code false} if the reference
     * is to the dynamic table.
     * @param nameIdx Index of the name in the table.
     * @param value Literal value.
     * @throws QpackException if the insertion fails.
     */
    internal void InsertWithNameReference(IQuicStreamChannel qpackDecoderStream, bool staticTableRef, int nameIdx,
                                          ICharSequence value)
    {
        QpackHeaderField entryForName;

        if (staticTableRef)
        {
            entryForName = QpackStaticTable.GetField(nameIdx);
        }
        else
        {
            entryForName = dynamicTable.GetEntryRelativeEncoderInstructions(nameIdx);
        }

        dynamicTable.Add(new QpackHeaderField(entryForName.name, value));
        SendInsertCountIncrementIfRequired(qpackDecoderStream);
    }

    /**
     * Inserts a header field with a literal name corresponding to the
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-insert-with-literal-name">
     *     encoder instruction.</a>
     *
     * @param qpackDecoderStream {@link QuicStreamChannel} for the QPACK decoder stream.
     * @param name of the field.
     * @param value of the field.
     * @throws QpackException if the insertion fails.
     */
    internal void InsertLiteral(IQuicStreamChannel qpackDecoderStream, ICharSequence name, ICharSequence value)
    {
        dynamicTable.Add(new QpackHeaderField(name, value));
        SendInsertCountIncrementIfRequired(qpackDecoderStream);
    }

    /**
     * Duplicates a previous entry corresponding to the
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-insert-with-literal-name">
     *     encoder instruction.</a>
     *
     * @param qpackDecoderStream {@link QuicStreamChannel} for the QPACK decoder stream.
     * @param index which is duplicated.
     * @throws QpackException if duplication fails.
     */
    internal void Duplicate(IQuicStreamChannel qpackDecoderStream, int index)
    {
        dynamicTable.Add(dynamicTable.GetEntryRelativeEncoderInstructions(index));
        SendInsertCountIncrementIfRequired(qpackDecoderStream);
    }

    /**
     * Callback when a bi-directional stream is
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-abandonment-of-a-stream"> abandoned</a>
     *
     * @param qpackDecoderStream {@link QuicStreamChannel} for the QPACK decoder stream.
     * @param streamId which is abandoned.
     */
    internal void StreamAbandoned(IQuicStreamChannel qpackDecoderStream, long streamId)
    {
        if (maxTableCapacity == 0)
        {
            return;
        }

        ReleaseBlockedStream(streamId);
        SendStreamCancellation(qpackDecoderStream, streamId);
    }

    internal void SendStreamCancellation(IQuicStreamChannel qpackDecoderStream, long streamId)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#section-4.4.2
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 1 |     Stream ID (6+)    |
        // +---+---+-----------------------+
        ByteBuf cancel = qpackDecoderStream.Alloc().Buffer(8);
        EncodePrefixedInteger(cancel, 0b0100_0000, 6, streamId);
        CloseOnFailure(qpackDecoderStream, qpackDecoderStream.WriteAndFlushAsync(cancel));
    }

    internal bool ReleaseBlockedStream(long streamId)
    {
        if (blockedStreamsCount == 0)
        {
            return false;
        }

        foreach (KeyValuePair<int, List<BlockedStream>> entry in blockedStreams)
        {
            List<BlockedStream> list = entry.Value;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].streamId == streamId)
                {
                    list.RemoveAt(i);
                    blockedStreamsCount--;

                    if (list.Count == 0)
                    {
                        blockedStreams.Remove(entry.Key);
                    }

                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsIndexed(byte b)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-indexed-field-line
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 1 | T |      Index (6+)       |
        // +---+---+-----------------------+
        return (b & 0b1000_0000) == 0b1000_0000;
    }

    private static bool IsLiteralWithNameRef(byte b)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-literal-field-line-with-nam
        //  0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 1 | N | T |Name Index (4+)|
        // +---+---+---+---+---------------+
        return (b & 0b1100_0000) == 0b0100_0000;
    }

    private static bool IsLiteral(byte b)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-literal-field-line-with-lit
        //  0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 1 | N | H |NameLen(3+)|
        // +---+---+---+---+---+-----------+
        return (b & 0b1110_0000) == 0b0010_0000;
    }

    private static bool IsIndexedWithPostBase(byte b)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-indexed-field-line-with-pos
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 1 |  Index (4+)   |
        // +---+---+---+---+---------------+
        return (b & 0b1111_0000) == 0b0001_0000;
    }

    private static bool IsLiteralWithPostBaseNameRef(byte b)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-literal-field-line-with-pos
        //  0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 0 | N |NameIdx(3+)|
        // +---+---+---+---+---+-----------+
        return (b & 0b1111_0000) == 0b0000_0000;
    }

    private void DecodeIndexed(ByteBuf @in, Action<ICharSequence, ICharSequence> sink, int @base)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-indexed-field-line
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 1 | T |      Index (6+)       |
        // +---+---+-----------------------+
        //
        // T == 1 implies static table
        QpackHeaderField field;

        if (FirstByteEquals(@in, 0b1100_0000))
        {
            int idx = DecodePrefixedIntegerAsInt(@in, 6);
            Debug.Assert(idx >= 0);
            if (idx >= QpackStaticTable.length)
            {
                throw HEADER_ILLEGAL_INDEX_VALUE;
            }

            field = QpackStaticTable.GetField(idx);
        }
        else
        {
            int idx = DecodePrefixedIntegerAsInt(@in, 6);
            Debug.Assert(idx >= 0);
            field = dynamicTable.GetEntryRelativeEncodedField(@base - idx - 1);
        }

        sink(field.name, field.value);
    }

    private void DecodeIndexedWithPostBase(ByteBuf @in, Action<ICharSequence, ICharSequence> sink, int @base)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-indexed-field-line-with-pos
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 1 |  Index (4+)   |
        // +---+---+---+---+---------------+
        int idx = DecodePrefixedIntegerAsInt(@in, 4);
        Debug.Assert(idx >= 0);
        QpackHeaderField field = dynamicTable.GetEntryRelativeEncodedField(@base + idx);
        sink(field.name, field.value);
    }

    private void DecodeLiteralWithNameRef(ByteBuf @in, Action<ICharSequence, ICharSequence> sink, int @base)
    {
        ICharSequence name;

        // https://www.rfc-editor.org/rfc/rfc9204.html#name-literal-field-line-with-nam
        //    0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 1 | N | T |Name Index (4+)|
        // +---+---+---+---+---------------+
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // |  Value String (Length bytes)  |
        // +-------------------------------+
        //
        // T == 1 implies static table
        if (FirstByteEquals(@in, 0b0001_0000))
        {
            int idx = DecodePrefixedIntegerAsInt(@in, 4);
            Debug.Assert(idx >= 0);
            if (idx >= QpackStaticTable.length)
            {
                throw NAME_ILLEGAL_INDEX_VALUE;
            }

            name = QpackStaticTable.GetField(idx).name;
        }
        else
        {
            int idx = DecodePrefixedIntegerAsInt(@in, 4);
            Debug.Assert(idx >= 0);
            name = dynamicTable.GetEntryRelativeEncodedField(@base - idx - 1).name;
        }

        ICharSequence value = DecodeHuffmanEncodedLiteral(@in, 7);
        sink(name, value);
    }

    private void DecodeLiteralWithPostBaseNameRef(ByteBuf @in, Action<ICharSequence, ICharSequence> sink, int @base)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-literal-field-line-with-nam
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 0 | N |NameIdx(3+)|
        // +---+---+---+---+---+-----------+
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // |  Value String (Length bytes)  |
        // +-------------------------------+
        int idx = DecodePrefixedIntegerAsInt(@in, 3);
        Debug.Assert(idx >= 0);
        ICharSequence name = dynamicTable.GetEntryRelativeEncodedField(@base + idx).name;
        ICharSequence value = DecodeHuffmanEncodedLiteral(@in, 7);
        sink(name, value);
    }

    private void DecodeLiteral(ByteBuf @in, Action<ICharSequence, ICharSequence> sink)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-literal-field-line-with-lit
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 1 | N | H |NameLen(3+)|
        // +---+---+---+---+---+-----------+
        // |  Name String (Length bytes)   |
        // +---+---------------------------+
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // |  Value String (Length bytes)  |
        // +-------------------------------+
        ICharSequence name = DecodeHuffmanEncodedLiteral(@in, 3);
        ICharSequence value = DecodeHuffmanEncodedLiteral(@in, 7);
        sink(name, value);
    }

    private ICharSequence DecodeHuffmanEncodedLiteral(ByteBuf @in, int prefix)
    {
        Debug.Assert(prefix < 8);
        bool huffmanEncoded = FirstByteEquals(@in, (byte) (1 << prefix));
        int length = DecodePrefixedIntegerAsInt(@in, prefix);
        Debug.Assert(length >= 0);
        if (huffmanEncoded)
        {
            return huffmanDecoder.Decode(@in, length);
        }
        if (@in.ReadableBytes < length)
        {
            throw INVALID_LENGTH_ENCODED_LITERAL;
        }

        byte[] buf = new byte[length];
        @in.ReadBytes(buf);

        return new AsciiString(buf, false);
    }

    // Visible for testing
    internal int DecodeRequiredInsertCount(QpackAttributes qpackAttributes, ByteBuf buf)
    {
        long encodedInsertCount = QpackUtil.DecodePrefixedInteger(buf, 8);
        Debug.Assert(encodedInsertCount >= 0);
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-required-insert-count
        // FullRange = 2 * MaxEntries
        //   if EncodedInsertCount == 0:
        //      ReqInsertCount = 0
        //   else:
        //      if EncodedInsertCount > FullRange:
        //         Error
        //      MaxValue = TotalNumberOfInserts + MaxEntries
        //
        //      # MaxWrapped is the largest possible value of
        //      # ReqInsertCount that is 0 mod 2 * MaxEntries
        //      MaxWrapped = floor(MaxValue / FullRange) * FullRange
        //      ReqInsertCount = MaxWrapped + EncodedInsertCount - 1
        //
        //      # If ReqInsertCount exceeds MaxValue, the Encoder's value
        //      # must have wrapped one fewer time
        //      if ReqInsertCount > MaxValue:
        //         if ReqInsertCount <= FullRange:
        //            Error
        //         ReqInsertCount -= FullRange
        //
        //      # Value of 0 must be encoded as 0.
        //      if ReqInsertCount == 0:
        //         Error
        if (encodedInsertCount == 0)
        {
            return 0;
        }
        if (qpackAttributes.DynamicTableDisabled() || encodedInsertCount > fullRange)
        {
            throw INVALID_REQUIRED_INSERT_COUNT;
        }

        long maxValue = dynamicTable.InsertCount() + maxEntries;
        long maxWrapped = FloorDiv(maxValue, fullRange) * fullRange;
        long requiredInsertCount = maxWrapped + encodedInsertCount - 1;

        if (requiredInsertCount > maxValue)
        {
            if (requiredInsertCount <= fullRange)
            {
                throw INVALID_REQUIRED_INSERT_COUNT;
            }

            requiredInsertCount -= fullRange;
        }

        // requiredInsertCount can not be negative as encodedInsertCount read from the buffer can not be negative.
        if (requiredInsertCount == 0)
        {
            throw INVALID_REQUIRED_INSERT_COUNT;
        }

        return ToIntOrThrow(requiredInsertCount);
    }

    // Visible for testing
    internal int DecodeBase(ByteBuf buf, int requiredInsertCount)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-encoded-field-section-prefi
        //   0   1   2   3   4   5   6   7
        // +---+---------------------------+
        // | S |      Delta Base (7+)      |
        // +---+---------------------------+
        bool s = (buf.GetByte(buf.ReaderIndex) & 0b1000_0000) == 0b1000_0000;
        int deltaBase = DecodePrefixedIntegerAsInt(buf, 7);
        Debug.Assert(deltaBase >= 0);
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-base
        //    if S == 0:
        //      Base = ReqInsertCount + DeltaBase
        //   else:
        //      Base = ReqInsertCount - DeltaBase - 1
        return s ? requiredInsertCount - deltaBase - 1 : requiredInsertCount + deltaBase;
    }

    private bool ShouldWaitForDynamicTableUpdates(int requiredInsertCount)
    {
        if (requiredInsertCount > dynamicTable.InsertCount())
        {
            if (blockedStreamsCount >= maxBlockedStreams)
            {
                throw MAX_BLOCKED_STREAMS_EXCEEDED;
            }

            return true;
        }

        return false;
    }

    private void SendInsertCountIncrementIfRequired(IQuicStreamChannel qpackDecoderStream)
    {
        int insertCount = dynamicTable.InsertCount();
        if (this.blockedStreams.Remove(insertCount, out List<BlockedStream> streams))
        {
            blockedStreamsCount -= streams.Count;
            bool failed = false;

            foreach (BlockedStream stream in streams)
            {
                try
                {
                    stream.whenDecoded();
                }
                catch (Exception e)
                {
                    failed = true;
                    logger.Error("Failed to resume a blocked stream {}.", stream.whenDecoded, e);
                }
            }

            if (failed)
            {
                throw BLOCKED_STREAM_RESUMPTION_FAILED;
            }
        }

        if (stateSyncStrategy.EntryAdded(insertCount))
        {
            // https://www.rfc-editor.org/rfc/rfc9204.html#name-insert-count-increment
            //   0   1   2   3   4   5   6   7
            // +---+---+---+---+---+---+---+---+
            // | 0 | 0 |     Increment (6+)    |
            // +---+---+-----------------------+
            ByteBuf incr = qpackDecoderStream.Alloc().Buffer(8);
            EncodePrefixedInteger(incr, 0b0, 6, insertCount - lastAckInsertCount);
            lastAckInsertCount = insertCount;
            CloseOnFailure(qpackDecoderStream, qpackDecoderStream.WriteAndFlushAsync(incr));
        }
    }

    private sealed class BlockedStream
    {
        internal readonly long streamId;
        internal readonly Action whenDecoded;

        internal BlockedStream(long streamId, Action whenDecoded)
        {
            this.streamId = streamId;
            this.whenDecoded = whenDecoded;
        }
    }
}
