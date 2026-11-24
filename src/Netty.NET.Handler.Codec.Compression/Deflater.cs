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

// CLR: CLR-only internal stand-in for java.util.zip.Deflater
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/zip/Deflater.html), named after the JDK type so
// that JdkZlibEncoder keeps the Java call shape (setInput, deflate with SYNC_FLUSH, needsInput, finish, finished,
// getTotalIn, end) and its constants. Owner decision 2026-11-23: the solution stays on .NET 10 and java.util.zip is
// replaced by the BCL System.IO.Compression streams. The stand-in writes through a raw DeflateStream (nowrap) or a
// ZLibStream (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibstream?view=net-10.0) into the
// private Sink, from which deflate() copies the compressed bytes.
// - The level is ZLibCompressionOptions.CompressionLevel, which "Can accept any value between -1 and 9 (inclusive). 0
//   gives no compression, 1 gives best speed, 9 gives best compression, and -1 requests the default compression level,
//   which is equivalent to 6", so Java's levels keep their meaning; the page applies to .NET 9, 10 and 11
//   (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibcompressionoptions.compressionlevel?view=net-10.0).
// - deflate(..., SYNC_FLUSH) is the stream's Flush(). The Flush page only says that it "flushes the internal buffer to
//   the underlying stream" (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.deflatestream.flush?view=net-10.0);
//   the source shows that it calls zlib's deflate with ZFlushCode.SyncFlush (FlushBuffers in DeflateStream.cs and
//   Deflater.Flush in Deflater.cs,
//   https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/System.IO.Compression/src/System/IO/Compression/DeflateZLib/DeflateStream.cs,
//   https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/System.IO.Compression/src/System/IO/Compression/DeflateZLib/Deflater.cs),
//   the flush mode Java's SYNC_FLUSH selects.
// - finish() makes the next deflate() call dispose the stream (leaveOpen), which finishes it (PurgeBuffers). A stream
//   that never received a byte writes nothing when it is disposed ("make sure to only flush when we actually had some
//   input", PurgeBuffers), while java.util.zip writes a complete empty stream. In that case the stand-in writes the
//   empty stream itself: a final fixed-Huffman block holding only the end-of-block code (BFINAL 1, BTYPE 01 and the
//   seven-bit code 0000000 of symbol 256, RFC 1951 sections 3.2.3 and 3.2.6, https://www.rfc-editor.org/rfc/rfc1951),
//   that is 0x03 0x00, and for the ZLIB wrapper the RFC 1950 header and the Adler-32 of no data, 1
//   (https://www.rfc-editor.org/rfc/rfc1950), with the header zlib writes (CMF 0x78, FLEVEL from the level,
//   "header += 31 - (header % 31)", https://raw.githubusercontent.com/madler/zlib/v1.3.1/deflate.c).
// - Input is written in chunks of at most MAX_WRITE bytes and only while the Sink is empty, so the Sink holds about one
//   chunk of output, as zlib's pending buffer does; getTotalIn() counts the written bytes.
// - setDictionary throws NotSupportedException: the BCL streams have no dictionary API.
// - The compressed bytes can differ from Java's zlib (.NET uses zlib-ng); the format and the inflated data are the same.
internal sealed class Deflater
{
    internal const int DEFLATED = 8;
    internal const int DEFAULT_COMPRESSION = -1;
    internal const int BEST_COMPRESSION = 9;
    internal const int SYNC_FLUSH = 2;

    private const int MAX_WRITE = 64 * 1024;

    private readonly int level;
    private readonly bool nowrap;
    private readonly Sink sink = new Sink();
    private Stream stream;
    private byte[] input = Array.Empty<byte>();
    private int inputOffset;
    private int inputLength;
    private bool flushPending;
    private bool finish;
    private bool closed;
    private bool finished;
    private long bytesRead;

    internal Deflater(int level) : this(level, false)
    {
    }

    internal Deflater(int level, bool nowrap)
    {
        this.level = level;
        this.nowrap = nowrap;
        ZLibCompressionOptions options = new ZLibCompressionOptions { CompressionLevel = level };
        stream = nowrap ? new DeflateStream(sink, options, true) : new ZLibStream(sink, options, true);
    }

    internal void SetInput(byte[] input, int off, int len)
    {
        this.input = input;
        inputOffset = off;
        inputLength = len;
    }

    internal void SetInput(byte[] input)
    {
        SetInput(input, 0, input.Length);
    }

    internal void SetDictionary(byte[] dictionary)
    {
        throw new NotSupportedException(
                "preset dictionaries are not supported: System.IO.Compression has no dictionary API");
    }

    internal bool NeedsInput()
    {
        return inputLength == 0;
    }

    internal void Finish()
    {
        finish = true;
    }

    internal bool Finished()
    {
        return finished;
    }

    internal int GetTotalIn()
    {
        return unchecked((int) bytesRead);
    }

    internal int Deflate(byte[] b, int off, int len, int flush)
    {
        int n = 0;
        for (;;)
        {
            n += sink.Take(b, off + n, len - n);
            if (n == len)
            {
                break;
            }
            if (inputLength > 0)
            {
                int chunk = Math.Min(inputLength, MAX_WRITE);
                stream.Write(input, inputOffset, chunk);
                inputOffset += chunk;
                inputLength -= chunk;
                bytesRead += chunk;
                flushPending = true;
            }
            else if (finish && !closed)
            {
                Close();
            }
            else if (flush == SYNC_FLUSH && flushPending && !closed)
            {
                stream.Flush();
                flushPending = false;
            }
            else
            {
                break;
            }
        }
        finished = closed && sink.Available == 0;
        return n;
    }

    private void Close()
    {
        stream.Dispose();
        stream = null;
        closed = true;
        if (bytesRead == 0)
        {
            if (!nowrap)
            {
                int header = (DEFLATED + ((15 - 8) << 4)) << 8;
                int levelFlags = level == DEFAULT_COMPRESSION || level == 6 ? 2 : level < 2 ? 0 : level < 6 ? 1 : 3;
                header |= levelFlags << 6;
                header += 31 - header % 31;
                sink.Write(new[] { (byte) (header >> 8), (byte) header });
            }
            sink.Write(new byte[] { 0x03, 0x00 });
            if (!nowrap)
            {
                sink.Write(new byte[] { 0, 0, 0, 1 });
            }
        }
    }

    internal void End()
    {
        if (stream != null)
        {
            stream.Dispose();
            stream = null;
        }
        closed = true;
    }

    // The destination of the BCL stream: keeps the compressed bytes until deflate() takes them.
    private sealed class Sink : Stream
    {
        private byte[] buffer = new byte[256];
        private int readIndex;
        private int writeIndex;

        internal int Available => writeIndex - readIndex;

        internal int Take(byte[] b, int off, int len)
        {
            int n = Math.Min(len, Available);
            if (n > 0)
            {
                buffer.AsSpan(readIndex, n).CopyTo(b.AsSpan(off));
                readIndex += n;
                if (readIndex == writeIndex)
                {
                    readIndex = writeIndex = 0;
                }
            }
            return n;
        }

        public override void Write(byte[] source, int offset, int count)
        {
            Write(source.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> source)
        {
            if (source.Length > buffer.Length - writeIndex)
            {
                int available = Available;
                byte[] target = available + source.Length > buffer.Length
                        ? new byte[Math.Max(buffer.Length * 2, available + source.Length)]
                        : buffer;
                buffer.AsSpan(readIndex, available).CopyTo(target);
                buffer = target;
                readIndex = 0;
                writeIndex = available;
            }
            source.CopyTo(buffer.AsSpan(writeIndex));
            writeIndex += source.Length;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
