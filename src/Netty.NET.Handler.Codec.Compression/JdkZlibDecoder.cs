/*
 * Copyright 2013 The Netty Project
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
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Decompress a {@link ByteBuf} using the inflate algorithm.
 */
// CLR: java.util.zip.Inflater is the internal stand-in Inflater (Inflater.cs, owner decision 2026-11-23) over the BCL
// DeflateStream/ZLibStream, and DataFormatException is the InvalidDataException those streams throw. The GZIP wrapper
// creates it with exactRemaining (one byte per Read), so that getRemaining() is exact at the end of the deflate body and
// the trailer is read from the right offset; ZLIB and NONE feed whole buffers (see Inflater.cs for the measured
// throughput). The BCL streams have no dictionary API; the stand-in implements needsDictionary/setDictionary on them
// (owner decision 2026-11-24, see Inflater.cs), so a stream with the zlib FDICT flag decodes with the configured
// dictionary as in Java (after the dictionary the stand-in reads the deflate data one byte per Read). java.util.zip.CRC32
// is System.IO.Hashing.Crc32 (owner decision 2026-11-23), wrapped by ByteBufChecksum.WrapChecksum. Deflater.DEFLATED is
// the stand-in Deflater's constant. The @Deprecated constructors name their replacements and are not ported (owner
// decision 2026-11-16); their comments are recorded in the manifest. checkNotNull(wrapper) has no counterpart because
// ZlibWrapper is a C# enum (a value type). The Buffer port has no hasArray()/array()/arrayOffset(); borrowed memory and
// MemoryMarshal.TryGetArray replace them (docs/buffer-porting.md, Array extraction), see TryGetArray and ArrayOf below.
// readByte() returns the unsigned byte in the Buffer port (owner decision), so the signed Java value of magic0 is
// restored with (sbyte); readUnsignedByte() is ReadByte(). C# switch sections cannot fall through, so each "fall
// through" is a goto case ("To imitate the fall-through behavior and pass control to other switch section, you can use
// the goto statement", https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements).
// assert is Debug.Assert, IllegalStateException is InvalidOperationException, and numbers in exception messages use the
// invariant culture (docs/codec-base-porting.md).
public class JdkZlibDecoder : ZlibDecoder
{
    private const int FHCRC = 0x02;
    private const int FEXTRA = 0x04;
    private const int FNAME = 0x08;
    private const int FCOMMENT = 0x10;
    private const int FRESERVED = 0xE0;

    /**
     * Smallest buffer we hand to {@link Inflater#inflate(byte[], int, int)}. The number of remaining input bytes is
     * only a hint for how much output to expect: the inflater may still hold decoded data that did not fit into the
     * previous output buffer, and by then it may have consumed all input bytes already
     * ({@link Inflater#getRemaining()} == 0). Inflating into a zero-sized buffer can never make progress.
     */
    private const int MIN_OUTPUT_BUFFER_SIZE = 512;

    private Inflater inflater;
    private readonly byte[] dictionary;

    // GZIP related
    private readonly ByteBufChecksum crc;
    private readonly bool decompressConcatenated;

    private enum GzipState
    {
        HEADER_START,
        HEADER_END,
        FLG_READ,
        XLEN_READ,
        SKIP_FNAME,
        SKIP_COMMENT,
        PROCESS_FHCRC,
        FOOTER_START,
    }

    private GzipState gzipState = GzipState.HEADER_START;
    private int flags = -1;
    private int xlen = -1;
    private bool needsRead;

    private static readonly int DEFAULT_MAX_FORWARD_BYTES = CompressionUtil.DEFAULT_MAX_FORWARD_BYTES;
    private readonly int maxForwardBytes;

    private volatile bool finished;

    private bool decideZlibOrNone;

    /**
     * Creates a new instance with the default wrapper ({@link ZlibWrapper#ZLIB})
     * and the specified maximum buffer allocation.
     *
     * @param maxAllocation
     *          Maximum size of the decompression buffer. Must be &gt;= 0.
     *          If zero, maximum size is decided by the {@link ByteBufAllocator}.
     */
    public JdkZlibDecoder(int maxAllocation) : this(ZlibWrapper.ZLIB, null, false, maxAllocation)
    {
    }

    /**
     * Creates a new instance with the specified preset dictionary and maximum buffer allocation.
     * The wrapper is always {@link ZlibWrapper#ZLIB} because it is the only format that
     * supports the preset dictionary.
     *
     * @param maxAllocation
     *          Maximum size of the decompression buffer. Must be &gt;= 0.
     *          If zero, maximum size is decided by the {@link ByteBufAllocator}.
     */
    public JdkZlibDecoder(byte[] dictionary, int maxAllocation) : this(ZlibWrapper.ZLIB, dictionary, false, maxAllocation)
    {
    }

    /**
     * Creates a new instance with the specified wrapper and maximum buffer allocation.
     * Be aware that only {@link ZlibWrapper#GZIP}, {@link ZlibWrapper#ZLIB} and {@link ZlibWrapper#NONE} are
     * supported atm.
     *
     * @param maxAllocation
     *          Maximum size of the decompression buffer. Must be &gt;= 0.
     *          If zero, maximum size is decided by the {@link ByteBufAllocator}.
     */
    public JdkZlibDecoder(ZlibWrapper wrapper, int maxAllocation) : this(wrapper, null, false, maxAllocation)
    {
    }

    public JdkZlibDecoder(ZlibWrapper wrapper, bool decompressConcatenated, int maxAllocation)
        : this(wrapper, null, decompressConcatenated, maxAllocation)
    {
    }

    public JdkZlibDecoder(bool decompressConcatenated, int maxAllocation)
        : this(ZlibWrapper.GZIP, null, decompressConcatenated, maxAllocation)
    {
    }

    private JdkZlibDecoder(ZlibWrapper wrapper, byte[] dictionary, bool decompressConcatenated, int maxAllocation)
        : base(maxAllocation)
    {
        this.maxForwardBytes = maxAllocation > 0 ? maxAllocation : DEFAULT_MAX_FORWARD_BYTES;

        this.decompressConcatenated = decompressConcatenated;
        switch (wrapper)
        {
            case ZlibWrapper.GZIP:
                inflater = new Inflater(true, true);
                crc = ByteBufChecksum.WrapChecksum(new System.IO.Hashing.Crc32());
                break;
            case ZlibWrapper.NONE:
                inflater = new Inflater(true);
                crc = null;
                break;
            case ZlibWrapper.ZLIB:
                inflater = new Inflater();
                crc = null;
                break;
            case ZlibWrapper.ZLIB_OR_NONE:
                // Postpone the decision until decode(...) is called.
                decideZlibOrNone = true;
                crc = null;
                break;
            default:
                throw new ArgumentException("Only GZIP or ZLIB is supported, but you used " + wrapper);
        }
        this.dictionary = dictionary;
    }

    public override bool IsClosed()
    {
        return finished;
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        needsRead = true;
        if (finished)
        {
            // Skip data received after finished.
            @in.SkipBytes(@in.ReadableBytes);
            return;
        }

        int readableBytes = @in.ReadableBytes;
        if (readableBytes == 0)
        {
            return;
        }

        if (decideZlibOrNone)
        {
            // First two bytes are needed to decide if it's a ZLIB stream.
            if (readableBytes < 2)
            {
                return;
            }

            bool nowrap = !LooksLikeZlib(@in.GetShort(@in.ReaderIndex));
            inflater = new Inflater(nowrap);
            decideZlibOrNone = false;
        }

        if (crc != null)
        {
            if (gzipState != GzipState.HEADER_END)
            {
                if (gzipState == GzipState.FOOTER_START)
                {
                    if (!HandleGzipFooter(@in))
                    {
                        // Either there was not enough data or the input is finished.
                        return;
                    }
                    // If we consumed the footer we will start with the header again.
                    Debug.Assert(gzipState == GzipState.HEADER_START);
                }
                if (!ReadGZIPHeader(@in))
                {
                    // There was not enough data readable to read the GZIP header.
                    return;
                }
                // Some bytes may have been consumed, and so we must re-set the number of readable bytes.
                readableBytes = @in.ReadableBytes;
                if (readableBytes == 0)
                {
                    return;
                }
            }
        }

        if (inflater.NeedsInput())
        {
            if (TryGetArray(@in, out ArraySegment<byte> inArray))
            {
                inflater.SetInput(inArray.Array, inArray.Offset, readableBytes);
            }
            else
            {
                byte[] array = new byte[readableBytes];
                @in.GetBytes(@in.ReaderIndex, array);
                inflater.SetInput(array);
            }
        }

        ByteBuf decompressed = PrepareDecompressBuffer(ctx, null, PreferredOutputBufferSize());
        try
        {
            bool readFooter = false;
            // If this is true the last inflate(...) filled the output buffer completely, so the inflater may still
            // hold decoded data even once all input bytes were consumed. needsInput() only tells us that the input
            // was consumed, not that all output was produced, so on its own it would end the loop too early and
            // the pending bytes would be dropped together with the input we skip below.
            bool pendingOutput = false;
            while (pendingOutput || !inflater.NeedsInput())
            {
                int writerIndex = decompressed.WriterIndex;
                int writable = decompressed.WritableBytes;
                ArraySegment<byte> outSegment = ArrayOf(decompressed, writerIndex, writable);
                byte[] outArray = outSegment.Array;
                int outIndex = outSegment.Offset;
                int outputLength = inflater.Inflate(outArray, outIndex, writable);
                pendingOutput = outputLength == writable;
                if (outputLength > 0)
                {
                    decompressed.WriterIndex = writerIndex + outputLength;
                    if (crc != null)
                    {
                        crc.Update(outArray, outIndex, outputLength);
                    }
                    if (maxAllocation == 0 && decompressed.ReadableBytes >= maxForwardBytes)
                    {
                        // Forward the buffer once it exceeds the threshold to bound memory
                        // while avoiding excessive fireChannelRead calls.
                        ByteBuf buffer = decompressed;
                        decompressed = null;
                        needsRead = false;
                        ctx.FireChannelRead(buffer);
                    }
                }
                else if (inflater.NeedsDictionary())
                {
                    if (dictionary == null)
                    {
                        throw new DecompressionException(
                                "decompression failure, unable to set dictionary as non was specified");
                    }
                    inflater.SetDictionary(dictionary);
                }

                if (inflater.Finished())
                {
                    if (crc == null)
                    {
                        finished = true; // Do not decode anymore.
                    }
                    else
                    {
                        readFooter = true;
                    }
                    break;
                }
                else
                {
                    decompressed = PrepareDecompressBuffer(ctx, decompressed, PreferredOutputBufferSize());
                }
            }

            @in.SkipBytes(readableBytes - inflater.GetRemaining());

            if (readFooter)
            {
                gzipState = GzipState.FOOTER_START;
                HandleGzipFooter(@in);
            }
        }
        catch (InvalidDataException e)
        {
            throw new DecompressionException("decompression failure", e);
        }
        finally
        {
            if (decompressed != null)
            {
                if (decompressed.IsReadable())
                {
                    needsRead = false;
                    ctx.FireChannelRead(decompressed);
                }
                else
                {
                    decompressed.Release();
                }
            }
        }
    }

    /**
     * The size we ask {@link #prepareDecompressBuffer(ChannelHandlerContext, ByteBuf, int)} for. Twice the remaining
     * input is a good guess for how much output is still coming, but it must never be zero because
     * {@link Inflater#inflate(byte[], int, int)} cannot write anything then. Any {@code maxAllocation} the user
     * configured is still applied by {@code prepareDecompressBuffer(...)} and so keeps its meaning.
     */
    private int PreferredOutputBufferSize()
    {
        return Math.Max(inflater.GetRemaining() << 1, MIN_OUTPUT_BUFFER_SIZE);
    }

    private bool HandleGzipFooter(ByteBuf @in)
    {
        if (ReadGZIPFooter(@in))
        {
            finished = !decompressConcatenated;

            if (!finished)
            {
                inflater.Reset();
                crc.Reset();
                xlen = -1;
                gzipState = GzipState.HEADER_START;
                return true;
            }
        }
        return false;
    }

    protected override void DecompressionBufferExhausted(ByteBuf buffer)
    {
        finished = true;
    }

    protected override void HandlerRemoved0(IChannelHandlerContext ctx)
    {
        base.HandlerRemoved0(ctx);
        if (inflater != null)
        {
            inflater.End();
        }
    }

    private bool ReadGZIPHeader(ByteBuf @in)
    {
        switch (gzipState)
        {
            case GzipState.HEADER_START:
                if (@in.ReadableBytes < 10)
                {
                    return false;
                }
                // read magic numbers
                int magic0 = unchecked((sbyte) @in.ReadByte());
                int magic1 = unchecked((sbyte) @in.ReadByte());

                if (magic0 != 31)
                {
                    throw new DecompressionException("Input is not in the GZIP format");
                }
                crc.Update(magic0);
                crc.Update(magic1);

                int method = @in.ReadByte();
                if (method != Deflater.DEFLATED)
                {
                    throw new DecompressionException("Unsupported compression method "
                            + method.ToString(CultureInfo.InvariantCulture) + " in the GZIP header");
                }
                crc.Update(method);

                flags = @in.ReadByte();
                crc.Update(flags);

                if ((flags & FRESERVED) != 0)
                {
                    throw new DecompressionException(
                            "Reserved flags are set in the GZIP header");
                }

                // mtime (int)
                crc.Update(@in, @in.ReaderIndex, 4);
                @in.SkipBytes(4);

                crc.Update(@in.ReadByte()); // extra flags
                crc.Update(@in.ReadByte()); // operating system

                gzipState = GzipState.FLG_READ;
                // fall through
                goto case GzipState.FLG_READ;
            case GzipState.FLG_READ:
                if ((flags & FEXTRA) != 0)
                {
                    if (@in.ReadableBytes < 2)
                    {
                        return false;
                    }
                    int xlen1 = @in.ReadByte();
                    int xlen2 = @in.ReadByte();
                    crc.Update(xlen1);
                    crc.Update(xlen2);

                    // XLEN is a little-endian unsigned 16-bit value (RFC 1952), so xlen1 is the
                    // low byte and xlen2 the high byte. This must be an assignment, not |=: xlen
                    // starts at the -1 "no extra field" sentinel, and OR-ing into -1 (0xFFFFFFFF)
                    // would leave it -1, so the extra field was never skipped.
                    xlen = xlen2 << 8 | xlen1;
                }
                gzipState = GzipState.XLEN_READ;
                // fall through
                goto case GzipState.XLEN_READ;
            case GzipState.XLEN_READ:
                if (xlen != -1)
                {
                    if (@in.ReadableBytes < xlen)
                    {
                        return false;
                    }
                    crc.Update(@in, @in.ReaderIndex, xlen);
                    @in.SkipBytes(xlen);
                }
                gzipState = GzipState.SKIP_FNAME;
                // fall through
                goto case GzipState.SKIP_FNAME;
            case GzipState.SKIP_FNAME:
                if (!SkipIfNeeded(@in, FNAME))
                {
                    return false;
                }
                gzipState = GzipState.SKIP_COMMENT;
                // fall through
                goto case GzipState.SKIP_COMMENT;
            case GzipState.SKIP_COMMENT:
                if (!SkipIfNeeded(@in, FCOMMENT))
                {
                    return false;
                }
                gzipState = GzipState.PROCESS_FHCRC;
                // fall through
                goto case GzipState.PROCESS_FHCRC;
            case GzipState.PROCESS_FHCRC:
                if ((flags & FHCRC) != 0)
                {
                    if (!VerifyCrc16(@in))
                    {
                        return false;
                    }
                }
                crc.Reset();
                gzipState = GzipState.HEADER_END;
                // fall through
                goto case GzipState.HEADER_END;
            case GzipState.HEADER_END:
                return true;
            default:
                throw new InvalidOperationException();
        }
    }

    /**
     * Skip bytes in the input if needed until we find the end marker {@code 0x00}.
     * @param   in the input
     * @param   flagMask the mask that should be present in the {@code flags} when we need to skip bytes.
     * @return  {@code true} if the operation is complete and we can move to the next state, {@code false} if we need
     *          the retry again once we have more readable bytes.
     */
    private bool SkipIfNeeded(ByteBuf @in, int flagMask)
    {
        if ((flags & flagMask) != 0)
        {
            for (;;)
            {
                if (!@in.IsReadable())
                {
                    // We didnt find the end yet, need to retry again once more data is readable
                    return false;
                }
                int b = @in.ReadByte();
                crc.Update(b);
                if (b == 0x00)
                {
                    break;
                }
            }
        }
        // Skip is handled, we can move to the next processing state.
        return true;
    }

    /**
     * Read the GZIP footer.
     *
     * @param   in the input.
     * @return  {@code true} if the footer could be read, {@code false} if the read could not be performed as
     *          the input {@link ByteBuf} doesn't have enough readable bytes (8 bytes).
     */
    private bool ReadGZIPFooter(ByteBuf @in)
    {
        if (@in.ReadableBytes < 8)
        {
            return false;
        }

        bool enoughData = VerifyCrc(@in);
        Debug.Assert(enoughData);

        // read ISIZE and verify
        int dataLength = @in.ReadIntLE();
        int readLength = inflater.GetTotalOut();
        if (dataLength != readLength)
        {
            throw new DecompressionException(
                    "Number of bytes mismatch. Expected: " + dataLength.ToString(CultureInfo.InvariantCulture) +
                    ", Got: " + readLength.ToString(CultureInfo.InvariantCulture));
        }
        return true;
    }

    /**
     * Verifies CRC.
     *
     * @param   in the input.
     * @return  {@code true} if verification could be performed, {@code false} if verification could not be performed as
     *          the input {@link ByteBuf} doesn't have enough readable bytes (4 bytes).
     */
    private bool VerifyCrc(ByteBuf @in)
    {
        if (@in.ReadableBytes < 4)
        {
            return false;
        }
        long crcValue = @in.ReadUnsignedIntLE();

        long readCrc = crc.GetValue();
        if (crcValue != readCrc)
        {
            throw new DecompressionException(
                    "CRC value mismatch. Expected: " + crcValue.ToString(CultureInfo.InvariantCulture) +
                    ", Got: " + readCrc.ToString(CultureInfo.InvariantCulture));
        }
        return true;
    }

    private bool VerifyCrc16(ByteBuf @in)
    {
        if (@in.ReadableBytes < 2)
        {
            return false;
        }

        int crc16Value = @in.ReadUnsignedShortLE();
        // the two least significant bytes from the CRC32
        int readCrc16 = (int) (crc.GetValue() & 0xFFFF);

        if (crc16Value != readCrc16)
        {
            throw new DecompressionException(
                    "CRC16 value mismatch. Expected: " + crc16Value.ToString(CultureInfo.InvariantCulture) +
                    ", Got: " + readCrc16.ToString(CultureInfo.InvariantCulture));
        }
        return true;
    }

    /*
     * Returns true if the cmf_flg parameter (think: first two bytes of a zlib stream)
     * indicates that this is a zlib stream.
     * <p>
     * You can lookup the details in the ZLIB RFC:
     * <a href="https://tools.ietf.org/html/rfc1950#section-2.2">RFC 1950</a>.
     */
    // CLR: getShort returns the signed short in the Buffer port too, so cmf_flg % 31 sees the Java value.
    private static bool LooksLikeZlib(short cmf_flg)
    {
        return (cmf_flg & 0x7800) == 0x7800 &&
                cmf_flg % 31 == 0;
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        // Discard bytes of the cumulation buffer if needed.
        DiscardSomeReadBytes();

        if (needsRead && !ctx.Channel.Config.IsAutoRead())
        {
            ctx.Read();
        }
        ctx.FireChannelReadComplete();
    }

    // CLR: hasArray(), array() and arrayOffset() + readerIndex of the readable range: one segment backed by a managed
    // array (AsReadOnlySequence and MemoryMarshal.TryGetArray, docs/buffer-porting.md, Array extraction). As the Buffer
    // port records, a range in one array of a composite buffer also takes this branch where Java's hasArray() is false;
    // both branches give the inflater the same bytes.
    private static bool TryGetArray(ByteBuf @in, out ArraySegment<byte> array)
    {
        ReadOnlySequence<byte> range = @in.AsReadOnlySequence(@in.ReaderIndex, @in.ReadableBytes);
        if (range.IsSingleSegment && MemoryMarshal.TryGetArray(range.First, out array))
        {
            return true;
        }
        array = default;
        return false;
    }

    // CLR: array() and arrayOffset() + index of the heap output buffer: the array segment that starts at buffer index
    // index. Like array(), it throws NotSupportedException (Java: UnsupportedOperationException) when the range is not
    // backed by one managed array; the buffers come from heapBuffer(...).
    private static ArraySegment<byte> ArrayOf(ByteBuf buf, int index, int length)
    {
        ReadOnlySequence<byte> range = buf.AsReadOnlySequence(index, length);
        if (range.IsSingleSegment && MemoryMarshal.TryGetArray(range.First, out ArraySegment<byte> array))
        {
            return array;
        }
        throw new NotSupportedException("the buffer range is not backed by one managed array");
    }
}
