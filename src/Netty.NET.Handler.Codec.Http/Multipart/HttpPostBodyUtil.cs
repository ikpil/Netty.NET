/*
 * Copyright 2012 The Netty Project
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
using System.Runtime.InteropServices;
using System.Text;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Shared Static object between HttpMessageDecoder, HttpPostRequestDecoder and HttpPostRequestEncoder
 */
// CLR: The constants keep their Java names. TransferEncodingMechanism has a field, a constructor and methods, so
// it is a sealed class with static readonly instances. Character.isWhitespace is core's
// HttpVersion.IsJavaWhitespace. The Buffer port throws ArgumentOutOfRangeException where a Java ByteBuf throws
// IndexOutOfBoundsException. CharsetName is a CLR-only helper that the multipart classes share for Charset.name()
// (see its comment).
internal static class HttpPostBodyUtil
{

    public const int chunkSize = 8096;

    /**
     * Default Content-Type in binary form
     */
    public const string DEFAULT_BINARY_CONTENT_TYPE = "application/octet-stream";

    /**
     * Default Content-Type in Text form
     */
    public const string DEFAULT_TEXT_CONTENT_TYPE = "text/plain";

    /**
     * Allowed mechanism for multipart
     * mechanism := "7bit"
                  / "8bit"
                  / "binary"
       Not allowed: "quoted-printable"
                  / "base64"
     */
    public sealed class TransferEncodingMechanism
    {
        /**
         * Default encoding
         */
        public static readonly TransferEncodingMechanism BIT7 = new TransferEncodingMechanism("7bit");
        /**
         * Short lines but not in ASCII - no encoding
         */
        public static readonly TransferEncodingMechanism BIT8 = new TransferEncodingMechanism("8bit");
        /**
         * Could be long text not in ASCII - no encoding
         */
        public static readonly TransferEncodingMechanism BINARY = new TransferEncodingMechanism("binary");

        private readonly string value;

        private TransferEncodingMechanism(string value)
        {
            this.value = value;
        }

        public string Value()
        {
            return value;
        }

        public override string ToString()
        {
            return value;
        }
    }

    /**
    * This class intends to decrease the CPU in seeking ahead some bytes in
    * HttpPostRequestDecoder
    */
    // CLR: ByteBuf.hasArray()/array()/arrayOffset() are replaced by MemoryMarshal.TryGetArray over the readable
    // memory. The segment offset is arrayOffset() + readerIndex() and its end arrayOffset() + writerIndex(); a
    // buffer whose readable memory is not backed by a managed array has no backing array, and the constructor
    // throws ArgumentException for it.
    internal sealed class SeekAheadOptimize
    {
        internal byte[] bytes;
        internal int readerIndex;
        internal int pos;
        internal int origPos;
        internal int limit;
        internal ByteBuf buffer;

        /**
         * @param buffer buffer with a backing byte array
         */
        internal SeekAheadOptimize(ByteBuf buffer)
        {
            if (!TryGetBackingArray(buffer, out ArraySegment<byte> segment))
            {
                throw new ArgumentException("buffer hasn't backing byte array");
            }

            this.buffer = buffer;
            bytes = segment.Array;
            readerIndex = buffer.ReaderIndex;
            origPos = pos = segment.Offset;
            limit = segment.Offset + segment.Count;
        }

        /**
        *
        * @param minus this value will be used as (currentPos - minus) to set
        * the current readerIndex in the buffer.
        */
        internal void SetReadPosition(int minus)
        {
            pos -= minus;
            readerIndex = GetReadPosition(pos);
            buffer.ReaderIndex = readerIndex;
        }

        /**
        *
        * @param index raw index of the array (pos in general)
        * @return the value equivalent of raw index to be used in readerIndex(value)
        */
        internal int GetReadPosition(int index)
        {
            return index - origPos + readerIndex;
        }
    }

    // CLR: ByteBuf.hasArray() (see SeekAheadOptimize).
    internal static bool HasArray(ByteBuf buffer)
    {
        return TryGetBackingArray(buffer, out _);
    }

    private static bool TryGetBackingArray(ByteBuf buffer, out ArraySegment<byte> segment)
    {
        ReadOnlyMemory<byte> memory = buffer.AsReadOnlyMemory(buffer.ReaderIndex, buffer.ReadableBytes);
        return MemoryMarshal.TryGetArray(memory, out segment);
    }

    /**
     * Find the first non whitespace
     * @return the rank of the first non whitespace
     */
    internal static int FindNonWhitespace(string sb, int offset)
    {
        int result;

        for (result = offset; result < sb.Length; result ++)
        {
            if (!HttpVersion.IsJavaWhitespace(sb[result]))
            {
                break;
            }
        }

        return result;
    }

    /**
     * Find the end of String
     * @return the rank of the end of string
     */
    internal static int FindEndOfString(string sb)
    {
        int result;

        for (result = sb.Length; result > 0; result --)
        {
            if (!HttpVersion.IsJavaWhitespace(sb[result - 1]))
            {
                break;
            }
        }

        return result;
    }

    /**
     * Try to find first LF or CRLF as Line Breaking
     *
     * @param buffer the buffer to search in
     * @param index the index to start from in the buffer
     * @return a relative position from index > 0 if LF or CRLF is found
     *         or < 0 if not found
     */
    internal static int FindLineBreak(ByteBuf buffer, int index)
    {
        int toRead = buffer.ReadableBytes - (index - buffer.ReaderIndex);
        int posFirstChar = buffer.BytesBefore(index, toRead, HttpConstants.LF);
        if (posFirstChar == -1)
        {
            // No LF, so neither CRLF
            return -1;
        }

        if (posFirstChar > 0 && buffer.GetByte(index + posFirstChar - 1) == HttpConstants.CR)
        {
            posFirstChar--;
        }

        return posFirstChar;
    }

    /**
     * Try to find last LF or CRLF as Line Breaking
     *
     * @param buffer the buffer to search in
     * @param index the index to start from in the buffer
     * @return a relative position from index > 0 if LF or CRLF is found
     *         or < 0 if not found
     */
    internal static int FindLastLineBreak(ByteBuf buffer, int index)
    {
        int candidate = FindLineBreak(buffer, index);
        int findCRLF = 0;

        if (candidate >= 0)
        {
            if (buffer.GetByte(index + candidate) == HttpConstants.CR)
            {
                findCRLF = 2;
            }
            else
            {
                findCRLF = 1;
            }

            candidate += findCRLF;
        }

        int next;

        while (candidate > 0 && (next = FindLineBreak(buffer, index + candidate)) >= 0)
        {
            candidate += next;
            if (buffer.GetByte(index + candidate) == HttpConstants.CR)
            {
                findCRLF = 2;
            }
            else
            {
                findCRLF = 1;
            }

            candidate += findCRLF;
        }

        return candidate - findCRLF;
    }

    /**
     * Try to find the delimiter, with LF or CRLF in front of it (added as delimiters) if needed
     *
     * @param buffer the buffer to search in
     * @param index the index to start from in the buffer
     * @param delimiter the delimiter as byte array
     * @param precededByLineBreak true if it must be preceded by LF or CRLF, else false
     * @return a relative position from index > 0 if delimiter found designing the start of it
     *         (including LF or CRLF is asked)
     *         or a number < 0 if delimiter is not found
     * @throws IndexOutOfBoundsException
     *         if {@code offset + delimiter.length} is greater than {@code buffer.capacity}
     */
    // CLR: GetByte and the delimiter octets are both unsigned bytes, so the != comparison gives the Java result for
    // every octet (owner byte rule; both sides are Java signed bytes there).
    internal static int FindDelimiter(ByteBuf buffer, int index, byte[] delimiter, bool precededByLineBreak)
    {
        int delimiterLength = delimiter.Length;
        int readerIndex = buffer.ReaderIndex;
        int writerIndex = buffer.WriterIndex;
        int toRead = writerIndex - index;
        int newOffset = index;
        bool delimiterNotFound = true;

        while (delimiterNotFound && delimiterLength <= toRead)
        {
            // Find first position: delimiter
            int posDelimiter = buffer.BytesBefore(newOffset, toRead, delimiter[0]);
            if (posDelimiter < 0)
            {
                return -1;
            }

            newOffset += posDelimiter;
            toRead -= posDelimiter;
            // Now check for delimiter
            if (toRead >= delimiterLength)
            {
                delimiterNotFound = false;

                for (int i = 0; i < delimiterLength; i++)
                {
                    if (buffer.GetByte(newOffset + i) != delimiter[i])
                    {
                        newOffset++;
                        toRead--;
                        delimiterNotFound = true;
                        break;
                    }
                }
            }

            if (!delimiterNotFound)
            {
                // Delimiter found, find if necessary: LF or CRLF
                if (precededByLineBreak && newOffset > readerIndex)
                {
                    if (buffer.GetByte(newOffset - 1) == HttpConstants.LF)
                    {
                        newOffset--;

                        // Check if CR before: not mandatory to be there
                        if (newOffset > readerIndex && buffer.GetByte(newOffset - 1) == HttpConstants.CR)
                        {
                            newOffset--;
                        }
                    }
                    else
                    {
                        // Delimiter with Line Break could be further: iterate after first char of delimiter
                        newOffset++;
                        toRead--;
                        delimiterNotFound = true;
                        continue;
                    }
                }

                return newOffset - readerIndex;
            }
        }

        return -1;
    }

    // CLR: Charset.name(), "this charset's canonical name", which is "by convention, usually in upper case"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html). Encoding.WebName is
    // the lower-case IANA name ("utf-8"; https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.webname?view=net-10.0),
    // so the encodings of the six standard Java charsets are named by their code page with the canonical java.nio names
    // (https://docs.oracle.com/en/java/javase/17/intl/supported-encodings.html); any other encoding keeps its WebName.
    internal static string CharsetName(Encoding charset)
    {
        switch (charset.CodePage)
        {
            case 65001:
                return "UTF-8";
            case 20127:
                return "US-ASCII";
            case 28591:
                return "ISO-8859-1";
            case 1201:
                return "UTF-16BE";
            case 1200:
                return "UTF-16LE";
            default:
                return charset.WebName;
        }
    }

    // CLR: catch (IllegalArgumentException e). IllegalArgumentException is ArgumentException, but the BCL also
    // derives ArgumentNullException, which checkNotNull throws where Java throws NullPointerException, and
    // ArgumentOutOfRangeException, which the Buffer port throws where Java throws IndexOutOfBoundsException, from
    // ArgumentException, so those two do not count as the Java type.
    internal static bool IsIllegalArgument(Exception e)
    {
        return e is ArgumentException && !(e is ArgumentNullException) && !(e is ArgumentOutOfRangeException);
    }

    // CLR: catch (NullPointerException e): the NullReferenceException of a null dereference or the
    // ArgumentNullException of ObjectUtil.CheckNotNull.
    internal static bool IsNullPointer(Exception e)
    {
        return e is NullReferenceException || e is ArgumentNullException;
    }
}
