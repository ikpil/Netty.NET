/*
 * Copyright 2015 The Netty Project
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

/*
 * Copyright 2014 Twitter, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Diagnostics;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http2.HpackUtil;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Handler.Codec.Http2.IHttp2Headers.PseudoHeaderName;
using static Netty.NET.Common.AsciiString;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

// CLR: the package-private final class is internal sealed and its package-private members are internal (the tests
// reach them through InternalsVisibleTo). Class is Type in Http2Exception.NewStatic. readerIndex()/writerIndex()/
// readableBytes() are the ByteBuf properties. The header representation byte of decode(ByteBuf, Http2HeadersSink)
// is read as sbyte, because "b < 0" tests the sign of the Java byte (the indexed-header bit 0x80); the masks give the
// same values for the signed and the unsigned byte. The other byte reads (decodeDynamicTableSizeUpdates,
// decodeULE128) only mask or compare with non-negative constants, so System.Byte gives the Java results. The
// simple enums IndexType and HeaderType are C# enums; a null HeaderType is HeaderType?. throw new Error(...) is
// UnreachableException and IllegalArgumentException is ArgumentException (docs/codec-http-porting.md, Shared CLR
// adaptations). catch (IllegalArgumentException) leaves out ArgumentNullException (Java's NullPointerException)
// and ArgumentOutOfRangeException (Java's IndexOutOfBoundsException), which derive from ArgumentException in the
// BCL (docs/codec-http-porting.md, Multipart: HttpPostBodyUtil.IsIllegalArgument). getMessage() is Message. The last
// section of each nested switch ends with break (C# forbids falling out of a switch section, CS8070), and the
// READ_INDEXED_HEADER section is a block, because a C# local is scoped to the whole switch block and the nested
// switch declares a local of the same name (CS0136).
internal sealed class HpackDecoder
{
    private static readonly Http2Exception DECODE_ULE_128_DECOMPRESSION_EXCEPTION =
            Http2Exception.NewStatic(COMPRESSION_ERROR, "HPACK - decompression failure",
                    Http2Exception.ShutdownHint.HARD_SHUTDOWN, typeof(HpackDecoder),
            "decodeULE128(..)");
    private static readonly Http2Exception DECODE_ULE_128_TO_LONG_DECOMPRESSION_EXCEPTION =
            Http2Exception.NewStatic(COMPRESSION_ERROR, "HPACK - long overflow",
                    Http2Exception.ShutdownHint.HARD_SHUTDOWN, typeof(HpackDecoder), "decodeULE128(..)");
    private static readonly Http2Exception DECODE_ULE_128_TO_INT_DECOMPRESSION_EXCEPTION =
            Http2Exception.NewStatic(COMPRESSION_ERROR, "HPACK - int overflow",
                    Http2Exception.ShutdownHint.HARD_SHUTDOWN, typeof(HpackDecoder), "decodeULE128ToInt(..)");
    private static readonly Http2Exception DECODE_ILLEGAL_INDEX_VALUE =
            Http2Exception.NewStatic(COMPRESSION_ERROR, "HPACK - illegal index value",
                    Http2Exception.ShutdownHint.HARD_SHUTDOWN, typeof(HpackDecoder), "decode(..)");
    private static readonly Http2Exception INDEX_HEADER_ILLEGAL_INDEX_VALUE =
            Http2Exception.NewStatic(COMPRESSION_ERROR, "HPACK - illegal index value",
                    Http2Exception.ShutdownHint.HARD_SHUTDOWN, typeof(HpackDecoder), "indexHeader(..)");
    private static readonly Http2Exception READ_NAME_ILLEGAL_INDEX_VALUE =
            Http2Exception.NewStatic(COMPRESSION_ERROR, "HPACK - illegal index value",
                    Http2Exception.ShutdownHint.HARD_SHUTDOWN, typeof(HpackDecoder), "readName(..)");
    private static readonly Http2Exception INVALID_MAX_DYNAMIC_TABLE_SIZE =
            Http2Exception.NewStatic(COMPRESSION_ERROR, "HPACK - invalid max dynamic table size",
                    Http2Exception.ShutdownHint.HARD_SHUTDOWN, typeof(HpackDecoder),
            "setDynamicTableSize(..)");
    private static readonly Http2Exception MAX_DYNAMIC_TABLE_SIZE_CHANGE_REQUIRED =
            Http2Exception.NewStatic(COMPRESSION_ERROR, "HPACK - max dynamic table size change required",
                    Http2Exception.ShutdownHint.HARD_SHUTDOWN, typeof(HpackDecoder), "decode(..)");
    private const byte READ_HEADER_REPRESENTATION = 0;
    private const byte READ_INDEXED_HEADER = 1;
    private const byte READ_INDEXED_HEADER_NAME = 2;
    private const byte READ_LITERAL_HEADER_NAME_LENGTH_PREFIX = 3;
    private const byte READ_LITERAL_HEADER_NAME_LENGTH = 4;
    private const byte READ_LITERAL_HEADER_NAME = 5;
    private const byte READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX = 6;
    private const byte READ_LITERAL_HEADER_VALUE_LENGTH = 7;
    private const byte READ_LITERAL_HEADER_VALUE = 8;

    private readonly HpackHuffmanDecoder huffmanDecoder = new HpackHuffmanDecoder();
    private readonly HpackDynamicTable hpackDynamicTable;
    private long maxHeaderListSize;
    private long maxDynamicTableSize;
    private long encoderMaxDynamicTableSize;
    private bool maxDynamicTableSizeChangeRequired;

    /**
     * Create a new instance.
     * @param maxHeaderListSize This is the only setting that can be configured before notifying the peer.
     *  This is because <a href="https://tools.ietf.org/html/rfc7540#section-6.5.1">SETTINGS_MAX_HEADER_LIST_SIZE</a>
     *  allows a lower than advertised limit from being enforced, and the default limit is unlimited
     *  (which is dangerous).
     */
    internal HpackDecoder(long maxHeaderListSize) : this(maxHeaderListSize, DEFAULT_HEADER_TABLE_SIZE)
    {
    }

    /**
     * Exposed Used for testing only! Default values used in the initial settings frame are overridden intentionally
     * for testing but violate the RFC if used outside the scope of testing.
     */
    internal HpackDecoder(long maxHeaderListSize, int maxHeaderTableSize)
    {
        this.maxHeaderListSize = CheckPositive(maxHeaderListSize, "maxHeaderListSize");

        maxDynamicTableSize = encoderMaxDynamicTableSize = maxHeaderTableSize;
        maxDynamicTableSizeChangeRequired = false;
        hpackDynamicTable = new HpackDynamicTable(maxHeaderTableSize);
    }

    /**
     * Decode the header block into header fields.
     * <p>
     * This method assumes the entire header block is contained in {@code in}.
     */
    internal void Decode(int streamId, ByteBuf @in, IHttp2Headers headers, bool validateHeaders)
    {
        Http2HeadersSink sink = new Http2HeadersSink(
                streamId, headers, maxHeaderListSize, validateHeaders);
        // Check for dynamic table size updates, which must occur at the beginning:
        // https://www.rfc-editor.org/rfc/rfc7541.html#section-4.2
        DecodeDynamicTableSizeUpdates(@in);
        Decode(@in, sink);

        // Now that we've read all of our headers we can perform the validation steps. We must
        // delay throwing until this point to prevent dynamic table corruption.
        sink.Finish();
    }

    private void DecodeDynamicTableSizeUpdates(ByteBuf @in)
    {
        byte b;
        while (@in.IsReadable() && ((b = @in.GetByte(@in.ReaderIndex)) & 0x20) == 0x20 && ((b & 0xC0) == 0x00))
        {
            @in.ReadByte();
            int index = b & 0x1F;
            if (index == 0x1F)
            {
                SetDynamicTableSize(DecodeULE128(@in, (long) index));
            }
            else
            {
                SetDynamicTableSize(index);
            }
        }
    }

    private void Decode(ByteBuf @in, Http2HeadersSink sink)
    {
        int index = 0;
        int nameLength = 0;
        int valueLength = 0;
        byte state = READ_HEADER_REPRESENTATION;
        bool huffmanEncoded = false;
        AsciiString name = null;
        IndexType indexType = IndexType.NONE;
        while (@in.IsReadable())
        {
            switch (state)
            {
                case READ_HEADER_REPRESENTATION:
                    sbyte b = (sbyte) @in.ReadByte();
                    if (maxDynamicTableSizeChangeRequired && (b & 0xE0) != 0x20)
                    {
                        // HpackEncoder MUST signal maximum dynamic table size change
                        throw MAX_DYNAMIC_TABLE_SIZE_CHANGE_REQUIRED;
                    }
                    if (b < 0)
                    {
                        // Indexed Header Field
                        index = b & 0x7F;
                        switch (index)
                        {
                            case 0:
                                throw DECODE_ILLEGAL_INDEX_VALUE;
                            case 0x7F:
                                state = READ_INDEXED_HEADER;
                                break;
                            default:
                                HpackHeaderField indexedHeader = GetIndexedHeader(index);
                                sink.AppendToHeaderList(
                                        (AsciiString) indexedHeader.name,
                                        (AsciiString) indexedHeader.value);
                                break;
                        }
                    }
                    else if ((b & 0x40) == 0x40)
                    {
                        // Literal Header Field with Incremental Indexing
                        indexType = IndexType.INCREMENTAL;
                        index = b & 0x3F;
                        switch (index)
                        {
                            case 0:
                                state = READ_LITERAL_HEADER_NAME_LENGTH_PREFIX;
                                break;
                            case 0x3F:
                                state = READ_INDEXED_HEADER_NAME;
                                break;
                            default:
                                // Index was stored as the prefix
                                name = ReadName(index);
                                nameLength = name.Length();
                                state = READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                                break;
                        }
                    }
                    else if ((b & 0x20) == 0x20)
                    {
                        // Dynamic Table Size Update
                        // See https://www.rfc-editor.org/rfc/rfc7541.html#section-4.2
                        throw ConnectionError(COMPRESSION_ERROR, "Dynamic table size update must happen " +
                            "at the beginning of the header block");
                    }
                    else
                    {
                        // Literal Header Field without Indexing / never Indexed
                        indexType = (b & 0x10) == 0x10 ? IndexType.NEVER : IndexType.NONE;
                        index = b & 0x0F;
                        switch (index)
                        {
                            case 0:
                                state = READ_LITERAL_HEADER_NAME_LENGTH_PREFIX;
                                break;
                            case 0x0F:
                                state = READ_INDEXED_HEADER_NAME;
                                break;
                            default:
                                // Index was stored as the prefix
                                name = ReadName(index);
                                nameLength = name.Length();
                                state = READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                                break;
                        }
                    }
                    break;

                case READ_INDEXED_HEADER:
                {
                    HpackHeaderField indexedHeader = GetIndexedHeader(DecodeULE128(@in, index));
                    sink.AppendToHeaderList(
                            (AsciiString) indexedHeader.name,
                            (AsciiString) indexedHeader.value);
                    state = READ_HEADER_REPRESENTATION;
                    break;
                }

                case READ_INDEXED_HEADER_NAME:
                    // Header Name matches an entry in the Header Table
                    name = ReadName(DecodeULE128(@in, index));
                    nameLength = name.Length();
                    state = READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                    break;

                case READ_LITERAL_HEADER_NAME_LENGTH_PREFIX:
                    b = (sbyte) @in.ReadByte();
                    huffmanEncoded = (b & 0x80) == 0x80;
                    index = b & 0x7F;
                    if (index == 0x7f)
                    {
                        state = READ_LITERAL_HEADER_NAME_LENGTH;
                    }
                    else
                    {
                        nameLength = index;
                        state = READ_LITERAL_HEADER_NAME;
                    }
                    break;

                case READ_LITERAL_HEADER_NAME_LENGTH:
                    // Header Name is a Literal String
                    nameLength = DecodeULE128(@in, index);

                    state = READ_LITERAL_HEADER_NAME;
                    break;

                case READ_LITERAL_HEADER_NAME:
                    // Wait until entire name is readable
                    if (@in.ReadableBytes < nameLength)
                    {
                        throw NotEnoughDataException(@in);
                    }

                    name = ReadStringLiteral(@in, nameLength, huffmanEncoded);

                    state = READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                    break;

                case READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX:
                    b = (sbyte) @in.ReadByte();
                    huffmanEncoded = (b & 0x80) == 0x80;
                    index = b & 0x7F;
                    switch (index)
                    {
                        case 0x7f:
                            state = READ_LITERAL_HEADER_VALUE_LENGTH;
                            break;
                        case 0:
                            InsertHeader(sink, name, EMPTY_STRING, indexType);
                            state = READ_HEADER_REPRESENTATION;
                            break;
                        default:
                            valueLength = index;
                            state = READ_LITERAL_HEADER_VALUE;
                            break;
                    }

                    break;

                case READ_LITERAL_HEADER_VALUE_LENGTH:
                    // Header Value is a Literal String
                    valueLength = DecodeULE128(@in, index);

                    state = READ_LITERAL_HEADER_VALUE;
                    break;

                case READ_LITERAL_HEADER_VALUE:
                    // Wait until entire value is readable
                    if (@in.ReadableBytes < valueLength)
                    {
                        throw NotEnoughDataException(@in);
                    }

                    AsciiString value = ReadStringLiteral(@in, valueLength, huffmanEncoded);
                    InsertHeader(sink, name, value, indexType);
                    state = READ_HEADER_REPRESENTATION;
                    break;

                default:
                    throw new UnreachableException("should not reach here state: " + state);
            }
        }

        if (state != READ_HEADER_REPRESENTATION)
        {
            throw ConnectionError(COMPRESSION_ERROR, "Incomplete header block fragment.");
        }
    }

    /**
     * Set the maximum table size. If this is below the maximum size of the dynamic table used by
     * the encoder, the beginning of the next header block MUST signal this change.
     */
    internal void SetMaxHeaderTableSize(long maxHeaderTableSize)
    {
        if (maxHeaderTableSize < MIN_HEADER_TABLE_SIZE || maxHeaderTableSize > MAX_HEADER_TABLE_SIZE)
        {
            throw ConnectionError(PROTOCOL_ERROR, "Header Table Size must be >= %d and <= %d but was %d",
                    MIN_HEADER_TABLE_SIZE, MAX_HEADER_TABLE_SIZE, maxHeaderTableSize);
        }
        maxDynamicTableSize = maxHeaderTableSize;
        if (maxDynamicTableSize < encoderMaxDynamicTableSize)
        {
            // decoder requires less space than encoder
            // encoder MUST signal this change
            maxDynamicTableSizeChangeRequired = true;
            hpackDynamicTable.SetCapacity(maxDynamicTableSize);
        }
    }

    internal void SetMaxHeaderListSize(long maxHeaderListSize)
    {
        if (maxHeaderListSize < MIN_HEADER_LIST_SIZE || maxHeaderListSize > MAX_HEADER_LIST_SIZE)
        {
            throw ConnectionError(PROTOCOL_ERROR, "Header List Size must be >= %d and <= %d but was %d",
                    MIN_HEADER_LIST_SIZE, MAX_HEADER_LIST_SIZE, maxHeaderListSize);
        }
        this.maxHeaderListSize = maxHeaderListSize;
    }

    internal long GetMaxHeaderListSize()
    {
        return maxHeaderListSize;
    }

    /**
     * Return the maximum table size. This is the maximum size allowed by both the encoder and the
     * decoder.
     */
    internal long GetMaxHeaderTableSize()
    {
        return hpackDynamicTable.Capacity();
    }

    /**
     * Return the number of header fields in the dynamic table. Exposed for testing.
     */
    internal int Length()
    {
        return hpackDynamicTable.Length();
    }

    /**
     * Return the size of the dynamic table. Exposed for testing.
     */
    internal long Size()
    {
        return hpackDynamicTable.Size();
    }

    /**
     * Return the header field at the given index. Exposed for testing.
     */
    internal HpackHeaderField GetHeaderField(int index)
    {
        return hpackDynamicTable.GetEntry(index + 1);
    }

    private void SetDynamicTableSize(long dynamicTableSize)
    {
        if (dynamicTableSize > maxDynamicTableSize)
        {
            throw INVALID_MAX_DYNAMIC_TABLE_SIZE;
        }
        encoderMaxDynamicTableSize = dynamicTableSize;
        maxDynamicTableSizeChangeRequired = false;
        hpackDynamicTable.SetCapacity(dynamicTableSize);
    }

    private static HeaderType ValidateHeader(int streamId, AsciiString name, ICharSequence value,
            HeaderType? previousHeaderType)
    {
        if (HasPseudoHeaderFormat(name))
        {
            if (previousHeaderType == HeaderType.REGULAR_HEADER)
            {
                throw StreamError(streamId, PROTOCOL_ERROR,
                        "Pseudo-header field '%s' found after regular header.", name);
            }
            IHttp2Headers.PseudoHeaderName pseudoHeader = GetPseudoHeader(name);
            HeaderType currentHeaderType = pseudoHeader.IsRequestOnly() ?
                    HeaderType.REQUEST_PSEUDO_HEADER : HeaderType.RESPONSE_PSEUDO_HEADER;
            if (previousHeaderType != null && currentHeaderType != previousHeaderType)
            {
                throw StreamError(streamId, PROTOCOL_ERROR, "Mix of request and response pseudo-headers.");
            }
            return currentHeaderType;
        }
        if (HttpHeaderValidationUtil.IsConnectionHeader(name, true))
        {
            throw StreamError(streamId, PROTOCOL_ERROR, "Illegal connection-specific header '%s' encountered.", name);
        }
        if (HttpHeaderValidationUtil.IsTeNotTrailers(name, value))
        {
            throw StreamError(streamId, PROTOCOL_ERROR,
                    "Illegal value specified for the 'TE' header (only 'trailers' is allowed).");
        }

        return HeaderType.REGULAR_HEADER;
    }

    private AsciiString ReadName(int index)
    {
        if (index <= HpackStaticTable.length)
        {
            HpackHeaderField hpackHeaderField = HpackStaticTable.GetEntry(index);
            return (AsciiString) hpackHeaderField.name;
        }
        if (index - HpackStaticTable.length <= hpackDynamicTable.Length())
        {
            HpackHeaderField hpackHeaderField = hpackDynamicTable.GetEntry(index - HpackStaticTable.length);
            return (AsciiString) hpackHeaderField.name;
        }
        throw READ_NAME_ILLEGAL_INDEX_VALUE;
    }

    private HpackHeaderField GetIndexedHeader(int index)
    {
        if (index <= HpackStaticTable.length)
        {
            return HpackStaticTable.GetEntry(index);
        }
        if (index - HpackStaticTable.length <= hpackDynamicTable.Length())
        {
            return hpackDynamicTable.GetEntry(index - HpackStaticTable.length);
        }
        throw INDEX_HEADER_ILLEGAL_INDEX_VALUE;
    }

    private void InsertHeader(Http2HeadersSink sink, AsciiString name, AsciiString value, IndexType indexType)
    {
        sink.AppendToHeaderList(name, value);

        switch (indexType)
        {
            case IndexType.NONE:
            case IndexType.NEVER:
                break;

            case IndexType.INCREMENTAL:
                hpackDynamicTable.Add(new HpackHeaderField(name, value));
                break;

            default:
                throw new UnreachableException("Unexpected index type: " + indexType);
        }
    }

    private AsciiString ReadStringLiteral(ByteBuf @in, int length, bool huffmanEncoded)
    {
        if (huffmanEncoded)
        {
            return huffmanDecoder.Decode(@in, length);
        }
        byte[] buf = new byte[length];
        @in.ReadBytes(buf);
        return new AsciiString(buf, false);
    }

    private static ArgumentException NotEnoughDataException(ByteBuf @in)
    {
        return new ArgumentException("decode only works with an entire header block! " + @in);
    }

    /**
     * Unsigned Little Endian Base 128 Variable-Length Integer Encoding
     * <p>
     * Visible for testing only!
     */
    internal static int DecodeULE128(ByteBuf @in, int result)
    {
        int readerIndex = @in.ReaderIndex;
        long v = DecodeULE128(@in, (long) result);
        if (v > int.MaxValue)
        {
            // the maximum value that can be represented by a signed 32 bit number is:
            // [0x1,0x7f] + 0x7f + (0x7f << 7) + (0x7f << 14) + (0x7f << 21) + (0x6 << 28)
            // OR
            // 0x0 + 0x7f + (0x7f << 7) + (0x7f << 14) + (0x7f << 21) + (0x7 << 28)
            // we should reset the readerIndex if we overflowed the int type.
            @in.ReaderIndex = readerIndex;
            throw DECODE_ULE_128_TO_INT_DECOMPRESSION_EXCEPTION;
        }
        return (int) v;
    }

    /**
     * Unsigned Little Endian Base 128 Variable-Length Integer Encoding
     * <p>
     * Visible for testing only!
     */
    internal static long DecodeULE128(ByteBuf @in, long result)
    {
        Debug.Assert(result <= 0x7f && result >= 0);
        bool resultStartedAtZero = result == 0;
        int writerIndex = @in.WriterIndex;
        for (int readerIndex = @in.ReaderIndex, shift = 0; readerIndex < writerIndex; ++readerIndex, shift += 7)
        {
            byte b = @in.GetByte(readerIndex);
            if (shift == 56 && ((b & 0x80) != 0 || b == 0x7F && !resultStartedAtZero))
            {
                // the maximum value that can be represented by a signed 64 bit number is:
                // [0x01L, 0x7fL] + 0x7fL + (0x7fL << 7) + (0x7fL << 14) + (0x7fL << 21) + (0x7fL << 28) + (0x7fL << 35)
                // + (0x7fL << 42) + (0x7fL << 49) + (0x7eL << 56)
                // OR
                // 0x0L + 0x7fL + (0x7fL << 7) + (0x7fL << 14) + (0x7fL << 21) + (0x7fL << 28) + (0x7fL << 35) +
                // (0x7fL << 42) + (0x7fL << 49) + (0x7fL << 56)
                // this means any more shifts will result in overflow so we should break out and throw an error.
                throw DECODE_ULE_128_TO_LONG_DECOMPRESSION_EXCEPTION;
            }

            if ((b & 0x80) == 0)
            {
                @in.ReaderIndex = readerIndex + 1;
                return result + ((b & 0x7FL) << shift);
            }
            result += (b & 0x7FL) << shift;
        }

        throw DECODE_ULE_128_DECOMPRESSION_EXCEPTION;
    }

    /**
     * HTTP/2 header types.
     */
    private enum HeaderType
    {
        REGULAR_HEADER,
        REQUEST_PSEUDO_HEADER,
        RESPONSE_PSEUDO_HEADER
    }

    private sealed class Http2HeadersSink
    {
        private readonly IHttp2Headers headers;
        private readonly long maxHeaderListSize;
        private readonly int streamId;
        private readonly bool validateHeaders;
        private long headersLength;
        private bool exceededMaxLength;
        private HeaderType? previousType;
        private Http2Exception validationException;

        internal Http2HeadersSink(int streamId, IHttp2Headers headers, long maxHeaderListSize, bool validateHeaders)
        {
            this.headers = headers;
            this.maxHeaderListSize = maxHeaderListSize;
            this.streamId = streamId;
            this.validateHeaders = validateHeaders;
        }

        internal void Finish()
        {
            if (exceededMaxLength)
            {
                HeaderListSizeExceeded(streamId, maxHeaderListSize, true);
            }
            else if (validationException != null)
            {
                throw validationException;
            }
        }

        internal void AppendToHeaderList(AsciiString name, AsciiString value)
        {
            headersLength += HpackHeaderField.SizeOf(name, value);
            exceededMaxLength |= headersLength > maxHeaderListSize;

            if (exceededMaxLength || validationException != null)
            {
                // We don't store the header since we've already failed validation requirements.
                return;
            }

            try
            {
                headers.Add(name, value);
                if (validateHeaders)
                {
                    previousType = ValidateHeader(streamId, name, value, previousType);
                }
            }
            catch (ArgumentException ex) when (ex is not ArgumentNullException && ex is not ArgumentOutOfRangeException)
            {
                validationException = StreamError(streamId, PROTOCOL_ERROR, ex,
                        "Validation failed for header '%s': %s", name, ex.Message);
            }
            catch (Http2Exception ex)
            {
                validationException = StreamError(streamId, PROTOCOL_ERROR, ex, ex.Message);
            }
        }
    }
}
