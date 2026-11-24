/*
 * Copyright 2025 The Netty Project
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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Decompress a {@link ByteBuf} using the inflate algorithm.
 */
// CLR: @UnstableApi is the Common [UnstableApi] attribute. Decompressor is IDecompressor, its nested Status enum and
// AbstractDecompressorBuilder are written IDecompressor.Status and IDecompressor.AbstractDecompressorBuilder (a class
// does not inherit the nested types of its interfaces), and status() is Status_() (owner decision, as the core's
// IDecompressor). The package-private constructor, processInput and processOutput are internal, as in
// InputBufferingDecompressor. builder() is Builder_(): a method named Builder cannot be declared next to the nested
// Builder class ("The type 'type name' already contains a definition for 'identifier'",
// https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0102), as SnappyFrameDecompressor.builder() is Builder_().
// java.util.zip.Inflater is the internal stand-in Inflater (Inflater.cs, owner decision 2026-11-23) over the BCL
// DeflateStream/ZLibStream, and DataFormatException is the InvalidDataException those streams throw. The GZIP wrapper
// creates it with exactRemaining (one byte per Read), so that getRemaining() is exact at the end of the deflate body and
// consumeInput() leaves the trailer in the buffer; ZLIB and NONE feed whole buffers, so after the end of such a stream the
// bytes the BCL stream read past it count as consumed (the decompressor is COMPLETE then). Preset dictionaries: with no
// dictionary a stream with the zlib FDICT flag fails with Netty's DecompressionException; with one,
// Inflater.SetDictionary throws NotSupportedException (the BCL streams have no dictionary API). java.util.zip.CRC32 is
// System.IO.Hashing.Crc32 (owner decision 2026-11-23), wrapped by ByteBufChecksum.WrapChecksum; Deflater.DEFLATED is
// the stand-in Deflater's constant. Objects.requireNonNull(wrapper) has no counterpart because ZlibWrapper is a C# enum
// (a value type). The Buffer port has no hasArray()/array()/arrayOffset(); borrowed memory and MemoryMarshal.TryGetArray
// replace them (docs/buffer-porting.md, Array extraction), see TryGetArray and ArrayOf below. readUnsignedByte() is
// ReadByte(), which is unsigned in the Buffer port (owner decision). C# switch sections cannot fall through, so each
// "fall through" is a goto case
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements). assert is
// Debug.Assert, IllegalStateException is InvalidOperationException, IllegalArgumentException is ArgumentException, and
// numbers in exception messages use the invariant culture (docs/codec-base-porting.md). C# has no throws clauses.
[UnstableApi]
public sealed class JdkZlibDecompressor : InputBufferingDecompressor
{
    private const int FHCRC = 0x02;
    private const int FEXTRA = 0x04;
    private const int FNAME = 0x08;
    private const int FCOMMENT = 0x10;
    private const int FRESERVED = 0xE0;

    /**
     * Smallest output buffer we hand to {@link Inflater#inflate(byte[], int, int)}. The number of remaining input
     * bytes is only a hint for how much output to expect: the inflater may still hold decoded data that did not fit
     * into the previous output buffer, and by then it may have consumed all input bytes already
     * ({@link Inflater#getRemaining()} == 0). Inflating into a zero-sized buffer can never make progress.
     */
    private const int MIN_OUTPUT_BUFFER_SIZE = 512;

    private Inflater inflater;
    private readonly int maxAllocation;
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

    private bool decideZlibOrNone;
    private bool finished;
    private bool gzipMemberFinished;
    /**
     * If this is true, part of the input buffer is still in use by the {@link #inflater}, so we shouldn't touch that
     * buffer too much (e.g. compact it).
     */
    private bool inputBufferInInflater;
    /**
     * If this is true, the last {@link Inflater#inflate(byte[], int, int)} filled the output buffer completely, so
     * the inflater may still hold decoded data even if {@link Inflater#needsInput()} reports that all input was
     * consumed.
     */
    private bool inflaterHasPendingOutput;

    internal JdkZlibDecompressor(Builder builder, IByteBufAllocator allocator) : base(allocator)
    {
        maxAllocation = builder.maxAllocation;
        dictionary = builder.dictionary;
        this.decompressConcatenated = builder.decompressConcatenated;
        switch (builder.wrapper)
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
                throw new ArgumentException("Only GZIP or ZLIB is supported, but you used " + builder.wrapper);
        }
    }

    public override IDecompressor.Status Status_()
    {
        if (finished)
        {
            return IDecompressor.Status.COMPLETE;
        }
        else if (inflaterHasPendingOutput)
        {
            return IDecompressor.Status.NEED_OUTPUT;
        }
        else if (inflater == null || inflater.NeedsInput() || gzipState == GzipState.FOOTER_START)
        {
            return IDecompressor.Status.NEED_INPUT;
        }
        else
        {
            return IDecompressor.Status.NEED_OUTPUT;
        }
    }

    public override void EndOfInput()
    {
        if (finished)
        {
            return;
        }
        if (crc != null && decompressConcatenated && gzipMemberFinished &&
                gzipState == GzipState.HEADER_START && Available() == 0)
        {
            finished = true;
            return;
        }
        throw new DecompressionException("Compressed stream ended before the end-of-stream marker");
    }

    internal override void ProcessInput(ByteBuf buf)
    {
        if (inputBufferInInflater)
        {
            throw new InvalidOperationException("Not in state NEED_INPUT");
        }

        int readableBytes = buf.ReadableBytes;
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

            bool nowrap = !LooksLikeZlib(buf.GetShort(buf.ReaderIndex));
            inflater = new Inflater(nowrap);
            decideZlibOrNone = false;
        }

        if (crc != null)
        {
            if (gzipState != GzipState.HEADER_END)
            {
                if (gzipState == GzipState.FOOTER_START)
                {
                    if (!HandleGzipFooter(buf))
                    {
                        // Either there was not enough data or the input is finished.
                        return;
                    }
                    // If we consumed the footer we will start with the header again.
                    Debug.Assert(gzipState == GzipState.HEADER_START);
                }
                if (!ReadGZIPHeader(buf))
                {
                    // There was not enough data readable to read the GZIP header.
                    return;
                }
                // Some bytes may have been consumed, and so we must re-set the number of readable bytes.
                readableBytes = buf.ReadableBytes;
                if (readableBytes == 0)
                {
                    return;
                }
            }
        }

        if (inflater.NeedsInput())
        {
            if (TryGetArray(buf, out ArraySegment<byte> array))
            {
                inflater.SetInput(array.Array, array.Offset, readableBytes);
            }
            else
            {
                inflater.SetInput(ByteBufUtil.GetBytes(buf));
            }
            inputBufferInInflater = true;
        }
    }

    private void ConsumeInput(ByteBuf buf)
    {
        if (inputBufferInInflater)
        {
            buf.ReaderIndex = buf.WriterIndex - inflater.GetRemaining();
            if (inflater.GetRemaining() == 0)
            {
                inputBufferInInflater = false;
            }
        }
    }

    internal override ByteBuf ProcessOutput(ByteBuf buf)
    {
        int proposedCapacity = Math.Max(inflater.GetRemaining() << 1, MIN_OUTPUT_BUFFER_SIZE);
        int targetCapacity = maxAllocation == 0
                ? proposedCapacity : Math.Min(maxAllocation, proposedCapacity);
        ByteBuf decompressed = allocator.HeapBuffer(targetCapacity);
        bool success = false;
        try
        {
            int writerIndex = decompressed.WriterIndex;
            int writableBytes = decompressed.WritableBytes;
            ArraySegment<byte> outSegment = ArrayOf(decompressed, writerIndex, writableBytes);
            byte[] outArray = outSegment.Array;
            int outIndex = outSegment.Offset;
            int outputLength;
            try
            {
                outputLength = inflater.Inflate(outArray, outIndex, writableBytes);
            }
            catch (InvalidDataException e)
            {
                throw new DecompressionException("decompression failure", e);
            }
            ConsumeInput(buf);
            if (outputLength > 0)
            {
                decompressed.WriterIndex = writerIndex + outputLength;
                if (crc != null)
                {
                    crc.Update(outArray, outIndex, outputLength);
                }
            }
            if (inflater.NeedsDictionary())
            {
                if (dictionary == null)
                {
                    throw new DecompressionException(
                            "decompression failure, unable to set dictionary as none was specified");
                }
                inflater.SetDictionary(dictionary);
            }
            if (inflater.Finished())
            {
                inputBufferInInflater = false;
                if (crc == null)
                {
                    finished = true; // Do not decode anymore.
                }
                else
                {
                    gzipState = GzipState.FOOTER_START;
                    // potentially consume footer
                    ProcessInput(buf);
                }
            }
            // If we filled the whole buffer the inflater may still have decoded data left that it could not write.
            // In that case we must ask for another output buffer, as needsInput() only tells us that all input
            // bytes were consumed, not that all output was produced.
            inflaterHasPendingOutput = outputLength == writableBytes && !inflater.Finished();
            success = true;
            return decompressed;
        }
        finally
        {
            if (!success)
            {
                decompressed.Release();
            }
        }
    }

    private bool HandleGzipFooter(ByteBuf @in)
    {
        if (ReadGZIPFooter(@in))
        {
            gzipMemberFinished = true;
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
                int magic0 = @in.ReadByte();
                int magic1 = @in.ReadByte();

                if (magic0 != 31 || magic1 != 139)
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

    public override void Close()
    {
        try
        {
            base.Close();
        }
        finally
        {
            if (inflater != null)
            {
                inflater.End();
                inflater = null;
            }
        }
    }

    // CLR: hasArray(), array() and arrayOffset() + readerIndex of the readable range: one segment backed by a managed
    // array (AsReadOnlySequence and MemoryMarshal.TryGetArray, docs/buffer-porting.md, Array extraction). As the Buffer
    // port records, a range in one array of a composite buffer also takes this branch where Java's hasArray() is false;
    // both branches give the inflater the same bytes.
    private static bool TryGetArray(ByteBuf buf, out ArraySegment<byte> array)
    {
        ReadOnlySequence<byte> range = buf.AsReadOnlySequence(buf.ReaderIndex, buf.ReadableBytes);
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

    [UnstableApi]
    public static Builder Builder_()
    {
        return new Builder();
    }

    // CLR: the private fields wrapper, dictionary and maxAllocation and the package-private decompressConcatenated are
    // internal, because a C# enclosing class cannot read the private members of its nested class; the package-private
    // constructor is internal.
    [UnstableApi]
    public sealed class Builder : IDecompressor.AbstractDecompressorBuilder
    {
        internal ZlibWrapper wrapper = ZlibWrapper.ZLIB;
        internal byte[] dictionary;
        internal int maxAllocation = 1024 * 1024;
        internal bool decompressConcatenated;

        internal Builder()
        {
        }

        /**
         * Set the wrapper format for the deflated data. Defaults to {@link ZlibWrapper#ZLIB}.
         *
         * @param wrapper The wrapper format
         * @return This builder
         */
        public Builder Wrapper(ZlibWrapper wrapper)
        {
            this.wrapper = wrapper;
            return this;
        }

        /**
         * Set the preset dictionary to use. Defaults to no dictionary.
         *
         * @param dictionary The dictionary
         * @return This builder
         */
        // CLR: a stream that needs the dictionary fails with NotSupportedException (see the class comment).
        public Builder Dictionary(byte[] dictionary)
        {
            this.dictionary = dictionary;
            return this;
        }

        /**
         * Set the maximum output buffer size. Defaults to 1M.
         *
         * @param maxAllocation The maximum output buffer size.
         * @return This builder
         */
        public Builder MaxAllocation(int maxAllocation)
        {
            this.maxAllocation = ObjectUtil.CheckPositiveOrZero(maxAllocation, "maxAllocation");
            return this;
        }

        public Builder DecompressConcatenated(bool decompressConcatenated)
        {
            this.decompressConcatenated = decompressConcatenated;
            return this;
        }

        public override IDecompressor Build(IByteBufAllocator allocator)
        {
            return new DefensiveDecompressor(new JdkZlibDecompressor(this, allocator));
        }
    }
}
