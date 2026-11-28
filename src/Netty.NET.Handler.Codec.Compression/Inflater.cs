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

// CLR: BCL inflaters can read ahead. Feed GZIP bodies and zlib bodies with preset dictionaries one byte at a time
// so the caller can locate the trailer and report remaining input accurately.
// An empty feed read means more input is needed, even if strict validation reports truncation. When an output
// buffer fills, probe one byte to distinguish stream completion and save any byte read for the next call.
// Zlib dictionaries are verified against DICTID before their final 32 KiB are loaded as a stored block into the
// raw inflater. Discard that block's output and verify the data trailer separately. A failed dictionary check
// leaves the inflater waiting for a dictionary.
// A raw inflater can accept a preset dictionary only before consuming input.
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
