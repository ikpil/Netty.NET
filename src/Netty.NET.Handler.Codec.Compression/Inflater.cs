/*
 * Copyright 2026 The Netty Project
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
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;

namespace Netty.NET.Handler.Codec.Compression;

// CLR: CLR-only internal stand-in for java.util.zip.Inflater
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/zip/Inflater.html), named after the JDK type so
// that JdkZlibDecoder, JdkZlibDecompressor and codec-http's SPDY header block decoder (InternalsVisibleTo) keep the Java
// call shape (setInput, inflate, needsInput, finished, getRemaining, getTotalOut, needsDictionary, setDictionary, reset,
// end). Owner decision 2026-11-23: the solution stays on .NET 10 and java.util.zip is replaced by the BCL
// System.IO.Compression streams. The stand-in reads a raw DeflateStream (nowrap) or a ZLibStream
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibstream?view=net-10.0) in decompression mode
// from the private Feed stream, which hands out the bytes that SetInput supplied. DataFormatException is the
// InvalidDataException the BCL streams throw for corrupt data, "The exception that is thrown when a data stream is in an
// invalid format" (https://learn.microsoft.com/en-us/dotnet/api/system.io.invaliddataexception?view=net-10.0; Inflater.cs
// maps zlib's Z_DATA_ERROR to it,
// https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/System.IO.Compression/src/System/IO/Compression/DeflateZLib/Inflater.cs).
//
// Facts this design rests on (DeflateStream.ReadCore,
// https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/System.IO.Compression/src/System/IO/Compression/DeflateZLib/DeflateStream.cs,
// and measured on this host with SP\zlibprobe\Program.cs, .NET 10.0.8):
// - DeflateStream reads its source only when its zlib stream has consumed all input (_inflater.NeedsInput()), into an
//   8192-byte buffer. Fed whole buffers it therefore reads past the end of the deflate data, so the number of unconsumed
//   input bytes (Inflater.getRemaining()) is unknown after the end; fed one byte per Read, the bytes it takes are exactly
//   the deflate data.
// - A finished stream returns 0 from Read without reading its source ("InflatorIsFinished"); a stream that needs input
//   returns 0 after one source Read that returned 0. Read returns as soon as zlib produced any byte.
// - Owner decision: the GZIP wrapper (JdkZlibDecoder/JdkZlibDecompressor parse the gzip header and trailer themselves and
//   inflate the body raw) feeds one byte per Read (exactRemaining), so getRemaining() is exact at the end of each member
//   and the trailer is read from the right offset; ZLIB and raw (NONE) input is fed in whole buffers, where the bytes
//   after the end are not used. Measured: whole buffers 381 MB/s, one byte per Read 21 MB/s of output.
//
// Mapping of the Inflater members:
// - inflate(b, off, len) reads until len bytes are produced or a Read returns 0, like zlib's inflate(), which "stops
//   when the input buffer becomes empty or the output buffer becomes full". A Read returning 0 after an empty source
//   Read means more input is needed; without one it means the end of the stream (finished()). When the output buffer
//   was filled, zlib may already have reached the end in the same call (it decodes the end-of-block code and the
//   trailer without output space), which Java reports through finished(); the stand-in reads one more byte to learn it
//   and keeps that byte for the next inflate() call when the stream goes on.
// - needsInput() is true when the Feed holds no bytes and the last source Read returned 0 (the BCL reads its source only
//   when its zlib stream needs input) or the stream is finished; getRemaining() is the number of bytes the Feed still
//   holds; getTotalOut() counts the bytes inflate() returned.
// - Preset dictionaries (owner decision 2026-11-24): the BCL streams have no dictionary API, and ZLibStream reports
//   zlib's Z_NEED_DICT as an unexpected ZLibException (an IOException), so the dictionary is implemented on the BCL
//   streams. RFC 1950 (https://www.rfc-editor.org/rfc/rfc1950, section 2.2): "If FDICT is set, a DICT dictionary
//   identifier is present immediately after the FLG byte. The dictionary is a sequence of bytes which are initially fed
//   to the compressor without producing any compressed output. DICT is the Adler-32 checksum of this sequence of
//   bytes", and ADLER32 is "a checksum value of the uncompressed data (excluding any dictionary data)".
//   - The Feed hands the two zlib header bytes to the ZLibStream on their own and checks the FDICT flag of a valid
//     header (CM 8, CINFO up to 7 and a valid FCHECK, the ones zlib accepts before it reads DICTID). With FDICT set it
//     stops feeding the ZLibStream; the stand-in reads the four DICTID bytes from the input itself and then reports
//     needsDictionary(), as zlib's inflate() returns Z_NEED_DICT after its DICTID state, having consumed the six header
//     bytes (inflate.c, https://raw.githubusercontent.com/madler/zlib/v1.3.1/inflate.c), so getRemaining() matches.
//   - setDictionary follows zlib's inflateSetDictionary (inflate.c): with the zlib wrapper it is allowed only while
//     the dictionary is needed ("if (state->wrap != 0 && state->mode != DICT) return Z_STREAM_ERROR") and the Adler-32
//     of the dictionary must equal DICTID ("if (dictid != state->check) return Z_DATA_ERROR"); Java throws
//     IllegalArgumentException for both (checkSetDictionaryResult in Inflater.c,
//     https://raw.githubusercontent.com/openjdk/jdk17u/jdk-17+35/src/java.base/share/native/libzip/Inflater.c), here
//     ArgumentException (docs/codec-base-porting.md), and needsDictionary() stays true after a wrong dictionary because
//     Java clears needDict only after the native call returned (Inflater.java,
//     https://raw.githubusercontent.com/openjdk/jdk17u/jdk-17+35/src/java.base/share/classes/java/util/zip/Inflater.java).
//     Java's message is zlib's strm->msg, which inflateSetDictionary does not set; the messages here say what failed.
//   - The dictionary goes into the window of a raw DeflateStream as one non-compressed block (BFINAL 0, BTYPE 00, then
//     LEN and NLEN, RFC 1951 section 3.2.4, https://www.rfc-editor.org/rfc/rfc1951), and setDictionary reads and drops
//     those bytes from its output, so the window holds the dictionary as zlib's updatewindow leaves it. Only the last
//     32 KiB are needed: a backward distance is "drawn from (1..32,768)" (RFC 1951, section 3.2.5), and updatewindow
//     also keeps only the last window size of the dictionary.
//   - The raw stream cannot check the zlib trailer, so the stand-in computes the Adler-32 of the inflated data with
//     System.IO.Hashing.Adler32 (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.adler32?view=net-10.0-pp)
//     and reads the four trailer bytes itself. Fed whole buffers, DeflateStream would keep the trailer in its own
//     buffer at the end, so after the dictionary the stand-in feeds the deflate data one byte per Read, as the GZIP
//     wrapper does; the end, the trailer and getRemaining() are then exact. Measured through the stand-in (scratch
//     Debug build, 1 MB of output, three runs): 16-29 MB/s for a dictionary stream, 250-420 MB/s for the same data
//     without one.
//     A mismatch throws InvalidDataException with zlib's message "incorrect data check" (inflate.c, CHECK state;
//     Java's DataFormatException), again on every later inflate() as zlib stays in its BAD state; finished() becomes
//     true after a matching trailer. Streams without FDICT are read by the ZLibStream exactly as before.
//   - In nowrap mode zlib allows inflateSetDictionary at any time and amends the window; on the BCL stream the stored
//     block can only be placed before the first deflate byte, so setDictionary after input was read throws
//     NotSupportedException. Before that it primes the window the same way, without the Adler-32 and trailer handling
//     (a raw stream has neither). needsDictionary() is never true in nowrap mode, as in zlib.
// - The "System.IO.Compression.UseStrictValidation" AppContext switch (off by default, DeflateStream.cs) makes a Read
//   whose source returns 0 before the end throw InvalidDataException; that exception, thrown right after an empty Feed
//   read, is the "needs input" answer and is not a data error.
// - reset() creates a new BCL stream (the BCL streams cannot be reset); end() disposes it.
internal sealed class Inflater
{
    // The largest backward distance of RFC 1951, the part of a dictionary that can be referenced.
    private const int WINDOW_SIZE = 32768;
    private const string INCORRECT_DATA_CHECK = "incorrect data check";

    private readonly bool nowrap;
    private readonly bool exactRemaining;
    private readonly byte[] lookaheadBuffer = new byte[1];
    private Feed feed;
    private Stream stream;
    private bool finished;
    private bool needDict;
    private bool hasLookahead;
    private long bytesWritten;
    // Dictionary streams (zlib wrapper with FDICT): the DICTID bytes still to read and their value, the Adler-32 of the
    // inflated data (null for every other stream), the trailer bytes still to read and their value, and whether the
    // trailer did not match.
    private int dictIdLeft;
    private uint dictId;
    private Adler32 adler;
    private int trailerLeft;
    private uint trailer;
    private bool badTrailer;

    internal Inflater() : this(false)
    {
    }

    internal Inflater(bool nowrap) : this(nowrap, false)
    {
    }

    // CLR: exactRemaining selects one byte per Read (the GZIP wrapper, see the class comment).
    internal Inflater(bool nowrap, bool exactRemaining)
    {
        this.nowrap = nowrap;
        this.exactRemaining = exactRemaining;
        Reset();
    }

    internal void SetInput(byte[] input, int off, int len)
    {
        feed.SetInput(input, off, len);
    }

    internal void SetInput(byte[] input)
    {
        SetInput(input, 0, input.Length);
    }

    internal bool NeedsInput()
    {
        return feed.Remaining == 0 && (finished || feed.exhausted);
    }

    internal bool NeedsDictionary()
    {
        return needDict;
    }

    internal void SetDictionary(byte[] dictionary)
    {
        if (nowrap)
        {
            if (feed.inputTaken || stream == null)
            {
                throw new NotSupportedException(
                        "a raw inflater takes a preset dictionary only before its first input byte");
            }

            Prime(dictionary);
            return;
        }

        if (!needDict)
        {
            throw new ArgumentException("no preset dictionary is needed");
        }
        if (Adler32.HashToUInt32(dictionary) != dictId)
        {
            throw new ArgumentException("the Adler-32 of the preset dictionary does not match the stream's DICTID");
        }

        stream.Dispose();
        feed.StartRawData();
        stream = new DeflateStream(feed, CompressionMode.Decompress, true);
        adler = new Adler32();
        needDict = false;
        Prime(dictionary);
    }

    // Puts the last WINDOW_SIZE bytes of the dictionary into the window of the raw stream as one non-compressed block
    // and drops them from the output.
    private void Prime(byte[] dictionary)
    {
        int length = Math.Min(dictionary.Length, WINDOW_SIZE);
        if (length == 0)
        {
            return;
        }

        // BFINAL 0 and BTYPE 00 in the first byte (its other bits are ignored), then LEN and NLEN, least significant
        // byte first.
        byte[] block = new byte[5 + length];
        block[1] = (byte) length;
        block[2] = (byte) (length >> 8);
        block[3] = (byte) ~length;
        block[4] = (byte) (~length >> 8);
        Array.Copy(dictionary, dictionary.Length - length, block, 5, length);
        feed.SetPrime(block);
        byte[] skipped = new byte[Math.Min(length, 8192)];

        while (length > 0)
        {
            int read = stream.Read(skipped, 0, Math.Min(length, skipped.Length));
            if (read <= 0)
            {
                throw new InvalidOperationException("the preset dictionary was not inflated");
            }

            length -= read;
        }
    }

    internal bool Finished()
    {
        return finished;
    }

    internal int GetRemaining()
    {
        return feed.Remaining;
    }

    internal int GetTotalOut()
    {
        return unchecked((int) bytesWritten);
    }

    internal int Inflate(byte[] b, int off, int len)
    {
        if (len == 0 || finished || needDict)
        {
            return 0;
        }

        int n = 0;

        if (hasLookahead)
        {
            b[off] = lookaheadBuffer[0];
            hasLookahead = false;
            n = 1;
        }

        while (n < len)
        {
            int read = Read(b, off + n, len - n);
            if (read <= 0)
            {
                break;
            }

            n += read;
        }

        if (n == len && !finished && !needDict && Read(lookaheadBuffer, 0, 1) == 1)
        {
            hasLookahead = true;
        }

        bytesWritten += n;
        return n;
    }

    // Returns the number of bytes read, or 0 when the stream needs input or a dictionary (then finished stays false) or
    // reached its end (then finished is true).
    private int Read(byte[] b, int off, int len)
    {
        if (dictIdLeft > 0)
        {
            ReadDictId();
            return 0;
        }

        if (trailerLeft > 0 || badTrailer)
        {
            ReadTrailer();
            return 0;
        }

        int emptyReads = feed.emptyReads;
        int read;

        try
        {
            read = stream.Read(b, off, len);
        }
        catch (InvalidDataException) when (feed.emptyReads != emptyReads)
        {
            // The strict validation of DeflateStream reports the empty Feed read as truncated data.
            read = 0;
        }

        if (read == 0)
        {
            if (feed.emptyReads == emptyReads)
            {
                if (adler != null)
                {
                    // The end of the raw deflate data of a dictionary stream: the zlib trailer follows.
                    trailerLeft = 4;
                    ReadTrailer();
                }
                else
                {
                    finished = true;
                }
            }
            else if (feed.dictionaryHeader)
            {
                dictIdLeft = 4;
                ReadDictId();
            }
        }
        else if (adler != null)
        {
            adler.Append(new ReadOnlySpan<byte>(b, off, read));
        }

        return read;
    }

    // Reads the DICTID bytes (most significant byte first, RFC 1950) that follow a header with FDICT.
    private void ReadDictId()
    {
        while (dictIdLeft > 0 && feed.TakeByte(out byte value))
        {
            dictId = dictId << 8 | value;
            dictIdLeft--;
        }

        if (dictIdLeft == 0)
        {
            needDict = true;
        }
    }

    // Reads the ADLER32 trailer (most significant byte first, RFC 1950) after the raw deflate data of a dictionary
    // stream and checks it against the Adler-32 of the inflated data.
    private void ReadTrailer()
    {
        if (badTrailer)
        {
            throw new InvalidDataException(INCORRECT_DATA_CHECK);
        }

        while (trailerLeft > 0 && feed.TakeByte(out byte value))
        {
            trailer = trailer << 8 | value;
            trailerLeft--;
        }

        if (trailerLeft == 0)
        {
            if (trailer != adler.GetCurrentHashAsUInt32())
            {
                badTrailer = true;
                throw new InvalidDataException(INCORRECT_DATA_CHECK);
            }

            finished = true;
        }
    }

    internal void Reset()
    {
        End();
        feed = new Feed(exactRemaining, nowrap ? 0 : 2);
        stream = nowrap ? new DeflateStream(feed, CompressionMode.Decompress, true)
                        : new ZLibStream(feed, CompressionMode.Decompress, true);
        finished = false;
        needDict = false;
        hasLookahead = false;
        bytesWritten = 0;
        dictIdLeft = 0;
        dictId = 0;
        adler = null;
        trailerLeft = 0;
        trailer = 0;
        badTrailer = false;
    }

    internal void End()
    {
        if (stream != null)
        {
            stream.Dispose();
            stream = null;
        }
    }

    // The source of the BCL stream: hands out the bytes of the last SetInput, one per Read when oneByte is set, and the
    // zlib header bytes on their own; a preset dictionary block (prime) goes first and is not input.
    private sealed class Feed : Stream
    {
        private bool oneByte;
        private byte[] input = Array.Empty<byte>();
        private int position;
        private int limit;
        private int headerLeft;
        private int header;
        private byte[] prime;
        private int primePosition;
        internal bool exhausted = true;
        internal bool dictionaryHeader;
        internal bool inputTaken;
        internal int emptyReads;

        internal Feed(bool oneByte, int headerLength)
        {
            this.oneByte = oneByte;
            headerLeft = headerLength;
        }

        internal int Remaining => limit - position;

        internal void SetInput(byte[] input, int off, int len)
        {
            this.input = input;
            position = off;
            limit = off + len;
        }

        internal void SetPrime(byte[] prime)
        {
            this.prime = prime;
            primePosition = 0;
        }

        // The raw deflate data of a dictionary stream follows: one byte per Read, so that the trailer starts at an exact
        // offset.
        internal void StartRawData()
        {
            dictionaryHeader = false;
            oneByte = true;
        }

        // Takes one input byte for the stand-in itself (DICTID, trailer); the BCL stream holds no unread input then.
        internal bool TakeByte(out byte value)
        {
            if (position == limit)
            {
                exhausted = true;
                value = 0;
                return false;
            }

            value = input[position++];
            inputTaken = true;
            exhausted = position == limit;
            return true;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (prime != null)
            {
                int p = Math.Min(buffer.Length, prime.Length - primePosition);
                prime.AsSpan(primePosition, p).CopyTo(buffer);
                primePosition += p;
                if (primePosition == prime.Length)
                {
                    prime = null;
                }

                return p;
            }

            int n = dictionaryHeader ? 0 : Math.Min(buffer.Length, limit - position);
            if (oneByte && n > 1)
            {
                n = 1;
            }

            if (headerLeft > 0 && n > headerLeft)
            {
                n = headerLeft;
            }

            if (n == 0)
            {
                exhausted = true;
                emptyReads++;
                return 0;
            }

            input.AsSpan(position, n).CopyTo(buffer);

            if (headerLeft > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    header = header << 8 | input[position + i];
                }

                headerLeft -= n;
                // RFC 1950, section 2.2: CMF (CM 8 = deflate, CINFO <= 7), FLG with FCHECK (CMF * 256 + FLG is a
                // multiple of 31) and FDICT (bit 5).
                dictionaryHeader = headerLeft == 0 && (header & 0x0F00) == 0x0800 && (header & 0xF000) <= 0x7000 &&
                                   header % 31 == 0 && (header & 0x20) != 0;
            }

            position += n;
            exhausted = false;
            inputTaken = true;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
