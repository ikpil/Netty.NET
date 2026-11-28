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
using Netty.NET.Handler.Codec.Quic;
using static Netty.NET.Handler.Codec.Http3.Http3CodecUtils;
using static Netty.NET.Handler.Codec.Http3.QpackHeaderField;
using static Netty.NET.Handler.Codec.Http3.QpackUtil;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * A QPACK encoder.
 */
// CLR: the package-private final class is internal sealed and its package-private members are internal.
// QuicStreamChannel is codec-quic's IQuicStreamChannel, QpackSensitivityDetector IQpackSensitivityDetector and
// Http3Headers IHttp3Headers, whose entries are codec-base IEntry<ICharSequence, ICharSequence> (for (Map.Entry<...>
// header : headers) is a foreach over them, as codec-http2's HpackEncoder). The checked QpackException needs no throws
// clause; assert is Debug.Assert. LongObjectHashMap<Queue<Indices>> is Dictionary<long, Queue<Indices>> and
// ArrayDeque/Queue is Queue<T> (owner decision 2026-11-19; poll() is TryDequeue, isEmpty() Count == 0, computeIfAbsent
// TryGetValue plus Add). The encoder stream writes are closeOnFailure(stream, stream.WriteAndFlushAsync(...))
// (Http3CodecUtils.CloseOnFailure takes the channel, as a Task has no channel()). The (byte) prefix masks are CLR bytes
// with the same bits. Arrays.copyOf is Array.Resize. The @Nullable (javax.annotation) annotations have no counterpart.
// The nested @FunctionalInterface Indices.IndexConsumer is the interface Indices.IIndexConsumer, and the method
// references dynamicTable::acknowledgeInsertCountOnAck/OnCancellation passed to Indices.forEach are the private nested
// class ActionIndexConsumer around the method group.
internal sealed class QpackEncoder
{
    private static readonly QpackException INVALID_SECTION_ACKNOWLEDGMENT =
            QpackException.NewStatic(typeof(QpackDecoder), "sectionAcknowledgment(...)",
                    "QPACK - section acknowledgment received for unknown stream.");
    private const int DYNAMIC_TABLE_ENCODE_NOT_DONE = -1;
    private const int DYNAMIC_TABLE_ENCODE_NOT_POSSIBLE = -2;
    /**
     * Maximum number of field sections for which we will track dynamic-table references while waiting for the
     * peer's Section Acknowledgment or Stream Cancellation instruction. Both instructions are optional per
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#section-2.2.2.2">RFC 9204, section 2.2.2.2</a>, so a
     * remote peer that never sends them (while still acknowledging dynamic-table insertions) must not be able to
     * grow this per-connection state without bound. Once the limit is reached we stop referencing the dynamic
     * table for new field sections (falling back to literal encoding, which is always legal, see
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#section-4.5.4">section 4.5.4</a>) until enough
     * outstanding sections are acknowledged or cancelled to free up tracking capacity again.
     */
    // Visible for tests
    internal const int MAX_OUTSTANDING_SECTIONS = 10_000;

    private readonly QpackHuffmanEncoder huffmanEncoder;
    private readonly QpackEncoderDynamicTable dynamicTable;
    private readonly IQpackSensitivityDetector sensitivityDetector;
    private int maxBlockedStreams;
    private int blockedStreams;
    private Dictionary<long, Queue<Indices>> streamSectionTrackers;
    private int outstandingSections;

    internal QpackEncoder(IQpackSensitivityDetector sensitivityDetector)
        : this(new QpackEncoderDynamicTable(), sensitivityDetector)
    {
    }

    internal QpackEncoder(QpackEncoderDynamicTable dynamicTable, IQpackSensitivityDetector sensitivityDetector)
    {
        huffmanEncoder = new QpackHuffmanEncoder();
        this.dynamicTable = ObjectUtil.CheckNotNull(dynamicTable, "dynamicTable");
        this.sensitivityDetector = sensitivityDetector == null ?
                IQpackSensitivityDetector.NEVER_SENSITIVE : sensitivityDetector;
    }

    /**
     * Encode the header field into the header block.
     *
     * <p>Fields for which {@link QpackSensitivityDetector#isSensitive(CharSequence, CharSequence)}
     * returns {@code true} are encoded as literals with the
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#section-4.5.4">"Never Indexed"</a>
     * ({@code N=1}) flag set, and are never inserted into the dynamic table.</p>
     */
    internal void EncodeHeaders(QpackAttributes qpackAttributes, ByteBuf @out, IByteBufAllocator allocator,
                                long streamId, IHttp3Headers headers)
    {
        int @base = dynamicTable.InsertCount();
        // Allocate a new buffer as we have to go back and write a variable length base and required insert count
        // later.
        ByteBuf tmp = allocator.Buffer();

        try
        {
            int maxDynamicTblIdx = -1;
            int requiredInsertCount = 0;
            Indices dynamicTableIndices = null;

            foreach (IEntry<ICharSequence, ICharSequence> header in headers)
            {
                ICharSequence name = header.GetKey();
                ICharSequence value = header.GetValue();
                int dynamicTblIdx;

                if (sensitivityDetector.IsSensitive(name, value))
                {
                    EncodeSensitiveHeader(tmp, name, value);
                    dynamicTblIdx = DYNAMIC_TABLE_ENCODE_NOT_POSSIBLE;
                }
                else
                {
                    dynamicTblIdx = EncodeHeader(qpackAttributes, tmp, @base, name, value);
                }

                if (dynamicTblIdx >= 0)
                {
                    int req = dynamicTable.AddReferenceToEntry(name, value, dynamicTblIdx);

                    if (dynamicTblIdx > maxDynamicTblIdx)
                    {
                        maxDynamicTblIdx = dynamicTblIdx;
                        requiredInsertCount = req;
                    }

                    if (dynamicTableIndices == null)
                    {
                        dynamicTableIndices = new Indices();
                    }

                    dynamicTableIndices.Add(dynamicTblIdx);
                }
            }

            // Track all the indices that we need to ack later.
            if (dynamicTableIndices != null)
            {
                Debug.Assert(streamSectionTrackers != null);

                if (!streamSectionTrackers.TryGetValue(streamId, out Queue<Indices> tracker))
                {
                    tracker = new Queue<Indices>();
                    streamSectionTrackers.Add(streamId, tracker);
                }

                tracker.Enqueue(dynamicTableIndices);
                outstandingSections++;
            }

            // https://www.rfc-editor.org/rfc/rfc9204.html#name-encoded-field-section-prefi
            //   0   1   2   3   4   5   6   7
            // +---+---+---+---+---+---+---+---+
            // |   Required Insert Count (8+)  |
            // +---+---------------------------+
            // | S |      Delta Base (7+)      |
            // +---+---------------------------+
            EncodePrefixedInteger(@out, 0b0, 8, dynamicTable.EncodedRequiredInsertCount(requiredInsertCount));

            if (@base >= requiredInsertCount)
            {
                EncodePrefixedInteger(@out, 0b0, 7, @base - requiredInsertCount);
            }
            else
            {
                EncodePrefixedInteger(@out, 0b1000_0000, 7, requiredInsertCount - @base - 1);
            }

            @out.WriteBytes(tmp);
        }
        finally
        {
            tmp.Release();
        }
    }

    internal void ConfigureDynamicTable(QpackAttributes attributes, long maxTableCapacity, int blockedStreams)
    {
        if (maxTableCapacity > 0)
        {
            Debug.Assert(attributes.EncoderStreamAvailable());
            IQuicStreamChannel encoderStream = attributes.EncoderStream();
            dynamicTable.MaxTableCapacity(maxTableCapacity);
            ByteBuf tableCapacity = encoderStream.Alloc().Buffer(8);
            // https://www.rfc-editor.org/rfc/rfc9204.html#name-set-dynamic-table-capacity
            //  0   1   2   3   4   5   6   7
            // +---+---+---+---+---+---+---+---+
            // | 0 | 0 | 1 |   Capacity (5+)   |
            // +---+---+---+-------------------+
            EncodePrefixedInteger(tableCapacity, 0b0010_0000, 5, maxTableCapacity);
            CloseOnFailure(encoderStream, encoderStream.WriteAndFlushAsync(tableCapacity));

            streamSectionTrackers = new Dictionary<long, Queue<Indices>>();
            maxBlockedStreams = blockedStreams;
        }
    }

    /**
     * <a href="https://quicwg.org/base-drafts/draft-ietf-quic-qpack.html#name-section-acknowledgment">
     *     Section acknowledgment</a> for the passed {@code streamId}.
     *
     * @param streamId For which the header fields section is acknowledged.
     */
    internal void SectionAcknowledgment(long streamId)
    {
        // If a configureDynamicTable(...) was called with a maxTableCapacity of 0 we will have not instanced
        // streamSectionTrackers. The remote peer might still (incorrectly) send a section acknowledgment for a
        // stream, so this must be treated as a protocol error instead of throwing an NPE.
        // See https://www.rfc-editor.org/rfc/rfc9204.html#section-4.4.1
        if (streamSectionTrackers == null)
        {
            throw INVALID_SECTION_ACKNOWLEDGMENT;
        }
        if (!streamSectionTrackers.TryGetValue(streamId, out Queue<Indices> tracker))
        {
            throw INVALID_SECTION_ACKNOWLEDGMENT;
        }

        tracker.TryDequeue(out Indices dynamicTableIndices);

        if (tracker.Count == 0)
        {
            streamSectionTrackers.Remove(streamId);
        }

        if (dynamicTableIndices == null)
        {
            throw INVALID_SECTION_ACKNOWLEDGMENT;
        }

        outstandingSections--;

        dynamicTableIndices.ForEach(new ActionIndexConsumer(dynamicTable.AcknowledgeInsertCountOnAck));
    }

    /**
     * <a href="https://quicwg.org/base-drafts/draft-ietf-quic-qpack.html#name-stream-cancellation">
     *     Stream cancellation</a> for the passed {@code streamId}.
     *
     * @param streamId which is cancelled.
     */
    internal void StreamCancellation(long streamId)
    {
        // If a configureDynamicTable(...) was called with a maxTableCapacity of 0 we will have not instanced
        // streamSectionTrackers. The remote peer might still send a stream cancellation for a stream, while it
        // is optional. See https://www.rfc-editor.org/rfc/rfc9204.html#section-2.2.2.2
        if (streamSectionTrackers == null)
        {
            return;
        }

        if (streamSectionTrackers.Remove(streamId, out Queue<Indices> tracker))
        {
            for (;;)
            {
                if (!tracker.TryDequeue(out Indices dynamicTableIndices))
                {
                    break;
                }

                outstandingSections--;
                dynamicTableIndices.ForEach(new ActionIndexConsumer(dynamicTable.AcknowledgeInsertCountOnCancellation));
            }
        }
    }

    /**
     * <a href="https://quicwg.org/base-drafts/draft-ietf-quic-qpack.html#name-insert-count-increment">
     *     Insert count increment</a>.
     *
     * @param increment for the known received count.
     */
    internal void InsertCountIncrement(int increment)
    {
        dynamicTable.IncrementKnownReceivedCount(increment);
    }

    /**
     * Encode a header field that the {@link QpackSensitivityDetector} flagged as sensitive.
     *
     * <p>Sensitive fields are never inserted into the dynamic table and are encoded
     * with the {@code "Never Indexed"} flag ({@code N=1}) so that intermediaries
     * also avoid indexing them — see
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#section-7.1">RFC 9204 7.1</a>.
     * </p>
     */
    private void EncodeSensitiveHeader(ByteBuf @out, ICharSequence name, ICharSequence value)
    {
        int index = QpackStaticTable.FindFieldIndex(name, value);
        if (index == QpackStaticTable.NOT_FOUND)
        {
            EncodeLiteral(@out, name, value, true);
        }
        else if ((index & QpackStaticTable.MASK_NAME_REF) == QpackStaticTable.MASK_NAME_REF)
        {
            // Name-only match, reuse the cached lookup instead of calling getIndex(name) again.
            EncodeLiteralWithNameRefStaticTable(@out, index ^ QpackStaticTable.MASK_NAME_REF, value, true);
        }
        else
        {
            // Exact (name, value) match in the static table, an indexed static reference
            // does not leak more information than what is already public, and the
            // intermediary cannot gain any compression benefit by inserting a copy into
            // its own dynamic table (the entry is already there as part of the static table).
            EncodeIndexedStaticTable(@out, index);
        }
    }

    /**
     * Encode the header field into the header block.
     * @param qpackAttributes {@link QpackAttributes} for the channel.
     * @param out {@link ByteBuf} to which encoded header field is to be written.
     * @param base Base for the dynamic table index.
     * @param name for the header field.
     * @param value for the header field.
     * @return Index in the dynamic table if the header field was encoded as a reference to the dynamic table,
     * {@link #DYNAMIC_TABLE_ENCODE_NOT_DONE } otherwise.
     */
    private int EncodeHeader(QpackAttributes qpackAttributes, ByteBuf @out, int @base, ICharSequence name,
                             ICharSequence value)
    {
        int index = QpackStaticTable.FindFieldIndex(name, value);
        if (index == QpackStaticTable.NOT_FOUND)
        {
            if (IsDynamicTableUnavailable(qpackAttributes))
            {
                EncodeLiteral(@out, name, value, false);
                return DYNAMIC_TABLE_ENCODE_NOT_POSSIBLE;
            }

            return EncodeWithDynamicTable(qpackAttributes, @out, @base, name, value);
        }
        else if ((index & QpackStaticTable.MASK_NAME_REF) == QpackStaticTable.MASK_NAME_REF)
        {
            int dynamicTblIdx = TryEncodeWithDynamicTable(qpackAttributes, @out, @base, name, value);
            if (dynamicTblIdx >= 0)
            {
                return dynamicTblIdx;
            }

            int nameIdx = index ^ QpackStaticTable.MASK_NAME_REF;
            dynamicTblIdx = TryAddToDynamicTable(qpackAttributes, true, nameIdx, name, value);
            if (dynamicTblIdx >= 0)
            {
                if (dynamicTblIdx >= @base)
                {
                    EncodePostBaseIndexed(@out, @base, dynamicTblIdx);
                }
                else
                {
                    EncodeIndexedDynamicTable(@out, @base, dynamicTblIdx);
                }

                return dynamicTblIdx;
            }

            EncodeLiteralWithNameRefStaticTable(@out, nameIdx, value, false);
        }
        else
        {
            EncodeIndexedStaticTable(@out, index);
        }

        return IsDynamicTableUnavailable(qpackAttributes) ? DYNAMIC_TABLE_ENCODE_NOT_POSSIBLE :
                DYNAMIC_TABLE_ENCODE_NOT_DONE;
    }

    /**
     * Encode the header field using dynamic table, if possible.
     *
     * @param qpackAttributes {@link QpackAttributes} for the channel.
     * @param out {@link ByteBuf} to which encoded header field is to be written.
     * @param base Base for the dynamic table index.
     * @param name for the header field.
     * @param value for the header field.
     * @return Index in the dynamic table if the header field was encoded as a reference to the dynamic table,
     * {@link #DYNAMIC_TABLE_ENCODE_NOT_DONE } otherwise.
     */
    private int EncodeWithDynamicTable(QpackAttributes qpackAttributes, ByteBuf @out, int @base, ICharSequence name,
                                       ICharSequence value)
    {
        int idx = TryEncodeWithDynamicTable(qpackAttributes, @out, @base, name, value);
        if (idx >= 0)
        {
            return idx;
        }

        if (idx == DYNAMIC_TABLE_ENCODE_NOT_DONE)
        {
            idx = TryAddToDynamicTable(qpackAttributes, false, -1, name, value);
            if (idx >= 0)
            {
                if (idx >= @base)
                {
                    EncodePostBaseIndexed(@out, @base, idx);
                }
                else
                {
                    EncodeIndexedDynamicTable(@out, @base, idx);
                }

                return idx;
            }
        }

        EncodeLiteral(@out, name, value, false);
        return idx;
    }

    /**
     * Try to encode the header field using dynamic table, otherwise do not encode.
     *
     * @param qpackAttributes {@link QpackAttributes} for the channel.
     * @param out {@link ByteBuf} to which encoded header field is to be written.
     * @param base Base for the dynamic table index.
     * @param name for the header field.
     * @param value for the header field.
     * @return Index in the dynamic table if the header field was encoded as a reference to the dynamic table.
     * {@link #DYNAMIC_TABLE_ENCODE_NOT_DONE } if encoding was not done. {@link #DYNAMIC_TABLE_ENCODE_NOT_POSSIBLE }
     * if dynamic table encoding is not possible (size constraint) and hence should not be tried for this header.
     */
    private int TryEncodeWithDynamicTable(QpackAttributes qpackAttributes, ByteBuf @out, int @base,
                                          ICharSequence name, ICharSequence value)
    {
        if (IsDynamicTableUnavailable(qpackAttributes))
        {
            return DYNAMIC_TABLE_ENCODE_NOT_POSSIBLE;
        }

        Debug.Assert(qpackAttributes.EncoderStreamAvailable());
        IQuicStreamChannel encoderStream = qpackAttributes.EncoderStream();

        int idx = dynamicTable.GetEntryIndex(name, value);
        if (idx == QpackEncoderDynamicTable.NOT_FOUND)
        {
            return DYNAMIC_TABLE_ENCODE_NOT_DONE;
        }

        if (idx >= 0)
        {
            if (dynamicTable.RequiresDuplication(idx, SizeOf(name, value)))
            {
                idx = dynamicTable.Add(name, value, SizeOf(name, value));
                Debug.Assert(idx >= 0);
                // https://www.rfc-editor.org/rfc/rfc9204.html#section-4.3.4
                //  0   1   2   3   4   5   6   7
                // +---+---+---+---+---+---+---+---+
                // | 0 | 0 | 0 |    Index (5+)     |
                // +---+---+---+-------------------+
                ByteBuf duplicate = encoderStream.Alloc().Buffer(8);
                EncodePrefixedInteger(duplicate, 0b0000_0000, 5,
                        dynamicTable.RelativeIndexForEncoderInstructions(idx));
                CloseOnFailure(encoderStream, encoderStream.WriteAndFlushAsync(duplicate));
                if (MayNotBlockStream())
                {
                    // Add to the table but do not use the entry in the header block to avoid blocking.
                    return DYNAMIC_TABLE_ENCODE_NOT_POSSIBLE;
                }
            }

            if (idx >= @base)
            {
                EncodePostBaseIndexed(@out, @base, idx);
            }
            else
            {
                EncodeIndexedDynamicTable(@out, @base, idx);
            }
        }
        else
        { // name match
            idx = -(idx + 1);
            int addIdx = TryAddToDynamicTable(qpackAttributes, false,
                    dynamicTable.RelativeIndexForEncoderInstructions(idx), name, value);
            if (addIdx < 0)
            {
                return DYNAMIC_TABLE_ENCODE_NOT_POSSIBLE;
            }

            idx = addIdx;

            if (idx >= @base)
            {
                EncodeLiteralWithPostBaseNameRef(@out, @base, idx, value);
            }
            else
            {
                EncodeLiteralWithNameRefDynamicTable(@out, @base, idx, value);
            }
        }

        return idx;
    }

    /**
     * Try adding the header field to the dynamic table.
     *
     * @param qpackAttributes {@link QpackAttributes} for the channel.
     * @param staticTableNameRef if {@code nameIdx} is an index in the static table.
     * @param nameIdx Index of the name if {@code > 0}.
     * @param name for the header field.
     * @param value for the header field.
     * @return Index in the dynamic table if the header field was encoded as a reference to the dynamic table,
     * {@link #DYNAMIC_TABLE_ENCODE_NOT_DONE} otherwise.
     */
    private int TryAddToDynamicTable(QpackAttributes qpackAttributes, bool staticTableNameRef, int nameIdx,
                                     ICharSequence name, ICharSequence value)
    {
        if (IsDynamicTableUnavailable(qpackAttributes))
        {
            return DYNAMIC_TABLE_ENCODE_NOT_POSSIBLE;
        }

        Debug.Assert(qpackAttributes.EncoderStreamAvailable());
        IQuicStreamChannel encoderStream = qpackAttributes.EncoderStream();

        int idx = dynamicTable.Add(name, value, SizeOf(name, value));
        if (idx >= 0)
        {
            ByteBuf insert = null;

            try
            {
                if (nameIdx >= 0)
                {
                    // 2 prefixed integers (name index and value length) each requires a maximum of 8 bytes
                    insert = encoderStream.Alloc().Buffer(value.Length() + 16);
                    // https://www.rfc-editor.org/rfc/rfc9204.html#name-insert-with-name-reference
                    //    0   1   2   3   4   5   6   7
                    // +---+---+---+---+---+---+---+---+
                    // | 1 | T |    Name Index (6+)    |
                    // +---+---+-----------------------+
                    EncodePrefixedInteger(insert, (byte) (staticTableNameRef ? 0b1100_0000 : 0b1000_0000), 6, nameIdx);
                }
                else
                {
                    // 2 prefixed integers (name and value length) each requires a maximum of 8 bytes
                    insert = encoderStream.Alloc().Buffer(name.Length() + value.Length() + 16);
                    // https://www.rfc-editor.org/rfc/rfc9204.html#name-insert-with-literal-name
                    //     0   1   2   3   4   5   6   7
                    //   +---+---+---+---+---+---+---+---+
                    //   | 0 | 1 | H | Name Length (5+)  |
                    //   +---+---+---+-------------------+
                    //   |  Name String (Length bytes)   |
                    //   +---+---------------------------+
                    // Names are always Huffman-encoded (H = 1). RFC 9204 makes
                    // Huffman a pure size/CPU tradeoff and does not tie it to the
                    // sensitivity of the field; whether intermediaries may index
                    // the field is controlled separately by the N bit on the
                    // matching literal field line.
                    EncodeLengthPrefixedHuffmanEncodedLiteral(insert, 0b0110_0000, 5, name);
                }

                //    0   1   2   3   4   5   6   7
                // +---+---+-----------------------+
                // | H |     Value Length (7+)     |
                // +---+---------------------------+
                // |  Value String (Length bytes)  |
                // +-------------------------------+
                EncodeStringLiteral(insert, value);
            }
            catch (Exception)
            {
                ReferenceCountUtil.Release(insert);
                return DYNAMIC_TABLE_ENCODE_NOT_DONE;
            }

            CloseOnFailure(encoderStream, encoderStream.WriteAndFlushAsync(insert));
            if (MayNotBlockStream())
            {
                // Add to the table but do not use the entry in the header block to avoid blocking.
                return DYNAMIC_TABLE_ENCODE_NOT_DONE;
            }

            blockedStreams++;
        }

        return idx;
    }

    private void EncodeIndexedStaticTable(ByteBuf @out, int index)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-indexed-field-line
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 1 | T |      Index (6+)       |
        // +---+---+-----------------------+
        EncodePrefixedInteger(@out, 0b1100_0000, 6, index);
    }

    private void EncodeIndexedDynamicTable(ByteBuf @out, int @base, int index)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-indexed-field-line
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 1 | T |      Index (6+)       |
        // +---+---+-----------------------+
        EncodePrefixedInteger(@out, 0b1000_0000, 6, @base - index - 1);
    }

    private void EncodePostBaseIndexed(ByteBuf @out, int @base, int index)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-indexed-field-line-with-pos
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 1 |  Index (4+)   |
        // +---+---+---+---+---------------+
        EncodePrefixedInteger(@out, 0b0001_0000, 4, index - @base);
    }

    private void EncodeLiteralWithNameRefStaticTable(ByteBuf @out, int nameIndex, ICharSequence value,
                                                     bool neverIndex)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-literal-field-line-with-nam
        //     0   1   2   3   4   5   6   7
        //   +---+---+---+---+---+---+---+---+
        //   | 0 | 1 | N | T |Name Index (4+)|
        //   +---+---+---+---+---------------+
        //   | H |     Value Length (7+)     |
        //   +---+---------------------------+
        //   |  Value String (Length bytes)  |
        //   +-------------------------------+
        //
        // T = 1 (static table). N is driven by the sensitivity detector.
        byte prefix = (byte) (0b0101_0000 | (neverIndex ? 0b0010_0000 : 0));
        EncodePrefixedInteger(@out, prefix, 4, nameIndex);
        EncodeStringLiteral(@out, value);
    }

    private void EncodeLiteralWithNameRefDynamicTable(ByteBuf @out, int @base, int nameIndex, ICharSequence value)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-literal-field-line-with-nam
        //     0   1   2   3   4   5   6   7
        //   +---+---+---+---+---+---+---+---+
        //   | 0 | 1 | N | T |Name Index (4+)|
        //   +---+---+---+---+---------------+
        //   | H |     Value Length (7+)     |
        //   +---+---------------------------+
        //   |  Value String (Length bytes)  |
        //   +-------------------------------+
        //
        // T = 0 (dynamic table). Sensitive headers are routed through
        // encodeSensitiveHeader() which bypasses the dynamic table entirely, so
        // anything reaching this method is non-sensitive and N is always 0.
        EncodePrefixedInteger(@out, 0b0100_0000, 4, @base - nameIndex - 1);
        EncodeStringLiteral(@out, value);
    }

    private void EncodeLiteralWithPostBaseNameRef(ByteBuf @out, int @base, int nameIndex, ICharSequence value)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-literal-field-line-with-pos
        //    0   1   2   3   4   5   6   7
        //   +---+---+---+---+---+---+---+---+
        //   | 0 | 0 | 0 | 0 | N |NameIdx(3+)|
        //   +---+---+---+---+---+-----------+
        //   | H |     Value Length (7+)     |
        //   +---+---------------------------+
        //   |  Value String (Length bytes)  |
        //   +-------------------------------+
        //
        // Same as above post-base references only exist for entries in the
        // encoder's dynamic table, so sensitive headers never reach here and
        // N is always 0.
        EncodePrefixedInteger(@out, 0, 3, nameIndex - @base);
        EncodeStringLiteral(@out, value);
    }

    private void EncodeLiteral(ByteBuf @out, ICharSequence name, ICharSequence value, bool neverIndex)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-literal-field-line-with-lit
        //   0   1   2   3   4   5   6   7
        //   +---+---+---+---+---+---+---+---+
        //   | 0 | 0 | 1 | N | H |NameLen(3+)|
        //   +---+---+---+---+---+-----------+
        //   |  Name String (Length bytes)   |
        //   +---+---------------------------+
        //   | H |     Value Length (7+)     |
        //   +---+---------------------------+
        //   |  Value String (Length bytes)  |
        //   +-------------------------------+
        //
        // H = 1 (Huffman) is always set for the name length prefix.
        byte prefix = (byte) (0b0010_1000 | (neverIndex ? 0b0001_0000 : 0));
        EncodeLengthPrefixedHuffmanEncodedLiteral(@out, prefix, 3, name);
        EncodeStringLiteral(@out, value);
    }

    /**
     * Encode string literal according to Section 5.2.
     * <a href="https://tools.ietf.org/html/rfc7541#section-5.2">Section 5.2</a>.
     */
    private void EncodeStringLiteral(ByteBuf @out, ICharSequence value)
    {
        //    0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | H |    String Length (7+)     |
        // +---+---------------------------+
        // |  String Data (Length octets)  |
        // +-------------------------------+
        // String values are always Huffman-encoded (H = 1). RFC 9204 treats the
        // H bit as a pure size/CPU choice; whether intermediaries may index the
        // field is controlled separately by the N bit on the literal field line.
        EncodeLengthPrefixedHuffmanEncodedLiteral(@out, 0b1000_0000, 7, value);
    }

    /**
     * Encode a string literal.
     */
    private void EncodeLengthPrefixedHuffmanEncodedLiteral(ByteBuf @out, byte mask, int prefix, ICharSequence value)
    {
        int huffmanLength = huffmanEncoder.GetEncodedLength(value);
        EncodePrefixedInteger(@out, mask, prefix, huffmanLength);
        huffmanEncoder.Encode(@out, value);
    }

    private bool MayNotBlockStream()
    {
        return blockedStreams >= maxBlockedStreams - 1;
    }

    /**
     * Whether the dynamic table must not be used to encode the next header field: either because it was disabled
     * for this connection, or because we are already tracking {@link #MAX_OUTSTANDING_SECTIONS} field sections
     * that are awaiting a Section Acknowledgment or Stream Cancellation instruction from the peer.
     *
     * @param qpackAttributes  the attributes used.
     * @return {@code true} if the dynamic table can't be used, {@code false} otherwise.
     */
    private bool IsDynamicTableUnavailable(QpackAttributes qpackAttributes)
    {
        return qpackAttributes.DynamicTableDisabled() || outstandingSections >= MAX_OUTSTANDING_SECTIONS;
    }

    // Visible for tests
    internal int OutstandingSectionCount()
    {
        return outstandingSections;
    }

    private sealed class Indices
    {
        private int idx;
        // Let's just assume 4 indices for now that we will store here as max.
        private int[] array = new int[4];

        internal void Add(int index)
        {
            if (idx == array.Length)
            {
                // Double it if needed.
                Array.Resize(ref array, array.Length << 1);
            }

            array[idx++] = index;
        }

        internal void ForEach(IIndexConsumer consumer)
        {
            for (int i = 0; i < idx; i++)
            {
                consumer.Accept(array[i]);
            }
        }

        internal interface IIndexConsumer
        {
            void Accept(int idx);
        }
    }

    // CLR: the method references dynamicTable::acknowledgeInsertCountOnAck and
    // dynamicTable::acknowledgeInsertCountOnCancellation passed as Indices.IndexConsumer.
    private sealed class ActionIndexConsumer : Indices.IIndexConsumer
    {
        private readonly Action<int> action;

        internal ActionIndexConsumer(Action<int> action)
        {
            this.action = action;
        }

        public void Accept(int idx)
        {
            action(idx);
        }
    }
}
