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

namespace Netty.NET.Handler.Codec.Compression;

// CLR: CLR-only internal stand-in for java.util.zip.Inflater
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/zip/Inflater.html), named after the JDK type so
// that JdkZlibDecoder and JdkZlibDecompressor keep the Java call shape (setInput, inflate, needsInput, finished,
// getRemaining, getTotalOut, needsDictionary, setDictionary, reset, end). Owner decision 2026-11-23: the solution stays
// on .NET 10 and java.util.zip is replaced by the BCL System.IO.Compression streams. The stand-in reads a raw
// DeflateStream (nowrap) or a ZLibStream
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
// - Preset dictionaries: the BCL streams have no dictionary API, and ZLibStream reports zlib's Z_NEED_DICT as an
//   unexpected ZLibException (an IOException). The Feed therefore hands the two zlib header bytes to the ZLibStream on
//   their own and checks the FDICT flag of RFC 1950 (https://www.rfc-editor.org/rfc/rfc1950, section 2.2: "If FDICT is
//   set, a DICT dictionary identifier is present immediately after the FLG byte"; only a header with CM 8, CINFO up to 7
//   and a valid FCHECK, the ones zlib accepts before it asks for a dictionary). With FDICT set it stops feeding, and
//   needsDictionary() becomes true once the header is seen (zlib first reads the four DICTID bytes as well).
//   setDictionary throws NotSupportedException.
// - The "System.IO.Compression.UseStrictValidation" AppContext switch (off by default, DeflateStream.cs) makes a Read
//   whose source returns 0 before the end throw InvalidDataException; that exception, thrown right after an empty Feed
//   read, is the "needs input" answer and is not a data error.
// - reset() creates a new BCL stream (the BCL streams cannot be reset); end() disposes it.
internal sealed class Inflater
{
    private readonly bool nowrap;
    private readonly bool exactRemaining;
    private readonly byte[] lookaheadBuffer = new byte[1];
    private Feed feed;
    private Stream stream;
    private bool finished;
    private bool needDict;
    private bool hasLookahead;
    private long bytesWritten;

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
        throw new NotSupportedException(
                "preset dictionaries are not supported: System.IO.Compression has no dictionary API");
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
                finished = true;
            }
            else if (feed.dictionaryHeader)
            {
                needDict = true;
            }
        }
        return read;
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
    // zlib header bytes on their own.
    private sealed class Feed : Stream
    {
        private readonly bool oneByte;
        private byte[] input = Array.Empty<byte>();
        private int position;
        private int limit;
        private int headerLeft;
        private int header;
        internal bool exhausted = true;
        internal bool dictionaryHeader;
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
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

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
