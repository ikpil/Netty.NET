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

// CLR: CLR-only internal stand-in for java.util.zip.Deflater
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/zip/Deflater.html), named after the JDK type so
// that JdkZlibEncoder and codec-http's SPDY header block encoder (InternalsVisibleTo) keep the Java call shape (setInput,
// setDictionary, deflate with SYNC_FLUSH, needsInput, finish, finished, getTotalIn, end) and its constants.
// Finish disposes the compression stream. Supply an empty final block and, for zlib, its header and checksum only
// when the BCL emitted no bytes.
// The BCL has no preset-dictionary API. Prime the raw deflater with the last 32 KiB of the dictionary, sync-flush,
// and discard that output. The zlib wrapper writes DICTID for the whole dictionary and a trailer checksum for the
// payload alone.
// Input is processed in bounded chunks while queued output is drained. Raw dictionaries can be installed only
// before the first payload byte.
internal sealed class Deflater
{
    internal const int DEFLATED = 8;
    internal const int DEFAULT_COMPRESSION = -1;
    internal const int BEST_COMPRESSION = 9;
    internal const int SYNC_FLUSH = 2;

    private const int MAX_WRITE = 64 * 1024;
    // The largest backward distance of RFC 1951, the part of a dictionary that can be referenced.
    private const int WINDOW_SIZE = 32768;
    // FDICT in the zlib header (zlib's PRESET_DICT).
    private const int PRESET_DICT = 0x20;

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
    private bool started;
    private long bytesRead;
    private bool hasDictionary;
    // The zlib wrapper with a preset dictionary: the Adler-32 of the dictionary (DICTID) and of the input (trailer).
    private Adler32 dictionaryAdler;
    private Adler32 adler;

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
        if (!nowrap && started)
        {
            throw new ArgumentException("the zlib wrapper takes a preset dictionary only before the first deflate()");
        }
        if (bytesRead > 0 || stream == null)
        {
            throw new NotSupportedException("a raw deflater takes a preset dictionary only before its first input byte");
        }
        if (dictionary.Length == 0)
        {
            return;
        }

        if (!hasDictionary)
        {
            hasDictionary = true;

            if (!nowrap)
            {
                // Replace the unused zlib stream. Its output is discarded with the dictionary's compressed bytes.
                stream.Dispose();
                stream = new DeflateStream(sink, new ZLibCompressionOptions { CompressionLevel = level }, true);
                dictionaryAdler = new Adler32();
                adler = new Adler32();
            }
        }

        dictionaryAdler?.Append(dictionary);
        int length = Math.Min(dictionary.Length, WINDOW_SIZE);
        stream.Write(dictionary, dictionary.Length - length, length);
        stream.Flush();
        sink.Clear();
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
        if (!started)
        {
            started = true;

            if (dictionaryAdler != null)
            {
                // The zlib header with FDICT and DICTID, which the raw stream does not write.
                WriteHeader(PRESET_DICT);
                WriteInt(dictionaryAdler.GetCurrentHashAsUInt32());
            }
        }

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
                adler?.Append(new ReadOnlySpan<byte>(input, inputOffset, chunk));
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

        if (adler != null)
        {
            // The raw stream received the dictionary and wrote the final block; the ADLER32 trailer follows.
            WriteInt(adler.GetCurrentHashAsUInt32());
        }
        else if (bytesRead == 0 && !hasDictionary && sink.Available == 0)
        {
            if (!nowrap)
            {
                WriteHeader(0);
            }

            sink.Write(new byte[] { 0x03, 0x00 });

            if (!nowrap)
            {
                sink.Write(new byte[] { 0, 0, 0, 1 });
            }
        }
    }

    // The zlib header as zlib's deflate() writes it: CMF 0x78, FLEVEL from the level, the given flags and FCHECK.
    private void WriteHeader(int flags)
    {
        int header = (DEFLATED + ((15 - 8) << 4)) << 8;
        int levelFlags = level == DEFAULT_COMPRESSION || level == 6 ? 2 : level < 2 ? 0 : level < 6 ? 1 : 3;
        header |= levelFlags << 6;
        header |= flags;
        header += 31 - header % 31;
        sink.Write(new[] { (byte) (header >> 8), (byte) header });
    }

    // An Adler-32 value, most significant byte first (RFC 1950).
    private void WriteInt(uint value)
    {
        sink.Write(new[] { (byte) (value >> 24), (byte) (value >> 16), (byte) (value >> 8), (byte) value });
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

        internal void Clear()
        {
            readIndex = writeIndex = 0;
        }

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

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }
}
