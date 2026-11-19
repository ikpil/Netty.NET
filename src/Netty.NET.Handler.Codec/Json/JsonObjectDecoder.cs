/*
 * Copyright 2014 The Netty Project
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

using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Json;

/**
 * Splits a byte stream of JSON objects and arrays into individual objects/arrays and passes them up the
 * {@link ChannelPipeline}.
 * <p>
 * The byte stream is expected to be in UTF-8 character encoding or ASCII. The current implementation
 * uses direct {@code byte} to {@code char} cast and then compares that {@code char} to a few low range
 * ASCII characters like {@code '{'}, {@code '['} or {@code '"'}. UTF-8 is not using low range [0..0x7F]
 * byte values for multibyte codepoint representations therefore fully supported by this implementation.
 * <p>
 * This class does not do any real parsing or validation. A sequence of bytes is considered a JSON object/array
 * if it contains a matching number of opening and closing braces/brackets. It's up to a subsequent
 * {@link ChannelHandler} to parse the JSON text into a more usable form i.e. a POJO.
 */
public class JsonObjectDecoder : ByteToMessageDecoder
{
    private const int ST_CORRUPTED = -1;
    private const int ST_INIT = 0;
    private const int ST_DECODING_NORMAL = 1;
    private const int ST_DECODING_ARRAY_STREAM = 2;

    private int openBraces;
    private int idx;

    private int lastReaderIndex;

    private int state;
    private bool insideString;

    private readonly int maxObjectLength;
    private readonly bool streamArrayElements;

    public JsonObjectDecoder()
        // 1 MB
        : this(1024 * 1024)
    {
    }

    public JsonObjectDecoder(int maxObjectLength) : this(maxObjectLength, false)
    {
    }

    public JsonObjectDecoder(bool streamArrayElements) : this(1024 * 1024, streamArrayElements)
    {
    }

    /**
     * @param maxObjectLength   maximum number of bytes a JSON object/array may use (including braces and all).
     *                             Objects exceeding this length are dropped and an {@link TooLongFrameException}
     *                             is thrown.
     * @param streamArrayElements   if set to true and the "top level" JSON object is an array, each of its entries
     *                                  is passed through the pipeline individually and immediately after it was fully
     *                                  received, allowing for arrays with "infinitely" many elements.
     *
     */
    public JsonObjectDecoder(int maxObjectLength, bool streamArrayElements)
    {
        this.maxObjectLength = CheckPositive(maxObjectLength, "maxObjectLength");
        this.streamArrayElements = streamArrayElements;
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        if (state == ST_CORRUPTED)
        {
            @in.SkipBytes(@in.ReadableBytes);
            return;
        }

        // CLR: a C# local's scope is its whole block and it cannot be used before its declarator
        // (https://learn.microsoft.com/dotnet/csharp/language-reference/language-specification/basic-concepts#77-scopes),
        // so the field that the source reads as 'idx' before declaring the local below is written this.idx.
        if (this.idx > @in.ReaderIndex && lastReaderIndex != @in.ReaderIndex)
        {
            this.idx = @in.ReaderIndex + (this.idx - lastReaderIndex);
        }

        // index of next byte to process.
        int idx = this.idx;
        int wrtIdx = @in.WriterIndex;

        if (wrtIdx > maxObjectLength)
        {
            // buffer size exceeded maxObjectLength; discarding the complete buffer.
            @in.SkipBytes(@in.ReadableBytes);
            Reset();
            throw new TooLongFrameException(
                            "object length exceeds " + maxObjectLength.ToString(CultureInfo.InvariantCulture) + ": " +
                            wrtIdx.ToString(CultureInfo.InvariantCulture) + " bytes discarded");
        }

        for (/* use current idx */; idx < wrtIdx; idx++)
        {
            byte c = @in.GetByte(idx);
            if (state == ST_DECODING_NORMAL)
            {
                DecodeByte(c, @in, idx);

                // All opening braces/brackets have been closed. That's enough to conclude
                // that the JSON object/array is complete.
                if (openBraces == 0)
                {
                    ByteBuf json = ExtractObject(ctx, @in, @in.ReaderIndex, idx + 1 - @in.ReaderIndex);
                    if (json != null)
                    {
                        @out.Add(json);
                    }

                    // The JSON object/array was extracted => discard the bytes from
                    // the input buffer.
                    @in.ReaderIndex = idx + 1;
                    // Reset the object state to get ready for the next JSON object/text
                    // coming along the byte stream.
                    Reset();
                }
            }
            else if (state == ST_DECODING_ARRAY_STREAM)
            {
                DecodeByte(c, @in, idx);

                if (!insideString && (openBraces == 1 && c == ',' || openBraces == 0 && c == ']'))
                {
                    // skip leading spaces. No range check is needed and the loop will terminate
                    // because the byte at position idx is not a whitespace.
                    for (int i = @in.ReaderIndex; IsWhitespace(@in.GetByte(i)); i++)
                    {
                        @in.SkipBytes(1);
                    }

                    // skip trailing spaces.
                    int idxNoSpaces = idx - 1;
                    while (idxNoSpaces >= @in.ReaderIndex && IsWhitespace(@in.GetByte(idxNoSpaces)))
                    {
                        idxNoSpaces--;
                    }

                    ByteBuf json = ExtractObject(ctx, @in, @in.ReaderIndex, idxNoSpaces + 1 - @in.ReaderIndex);
                    if (json != null)
                    {
                        @out.Add(json);
                    }

                    @in.ReaderIndex = idx + 1;

                    if (c == ']')
                    {
                        Reset();
                    }
                }
            }
            // JSON object/array detected. Accumulate bytes until all braces/brackets are closed.
            else if (c == '{' || c == '[')
            {
                InitDecoding(c);

                if (state == ST_DECODING_ARRAY_STREAM)
                {
                    // Discard the array bracket
                    @in.SkipBytes(1);
                }
            }
            // Discard leading spaces in front of a JSON object/array.
            else if (IsWhitespace(c))
            {
                @in.SkipBytes(1);
            }
            else
            {
                state = ST_CORRUPTED;
                throw new CorruptedFrameException(
                        "invalid JSON received at byte position " + idx.ToString(CultureInfo.InvariantCulture) + ": " +
                        ByteBufUtil.HexDump(@in));
            }
        }

        if (@in.ReadableBytes == 0)
        {
            this.idx = 0;
        }
        else
        {
            this.idx = idx;
        }
        this.lastReaderIndex = @in.ReaderIndex;
    }

    /**
     * Override this method if you want to filter the json objects/arrays that get passed through the pipeline.
     */
    protected virtual ByteBuf ExtractObject(IChannelHandlerContext ctx, ByteBuf buffer, int index, int length)
    {
        return buffer.RetainedSlice(index, length);
    }

    private void DecodeByte(byte c, ByteBuf @in, int idx)
    {
        if ((c == '{' || c == '[') && !insideString)
        {
            openBraces++;
        }
        else if ((c == '}' || c == ']') && !insideString)
        {
            openBraces--;
        }
        else if (c == '"')
        {
            // start of a new JSON string. It's necessary to detect strings as they may
            // also contain braces/brackets and that could lead to incorrect results.
            if (!insideString)
            {
                insideString = true;
            }
            else
            {
                int backslashCount = 0;
                idx--;
                while (idx >= 0)
                {
                    if (@in.GetByte(idx) == '\\')
                    {
                        backslashCount++;
                        idx--;
                    }
                    else
                    {
                        break;
                    }
                }
                // The double quote isn't escaped only if there are even "\"s.
                if (backslashCount % 2 == 0)
                {
                    // Since the double quote isn't escaped then this is the end of a string.
                    insideString = false;
                }
            }
        }
    }

    private void InitDecoding(byte openingBrace)
    {
        openBraces = 1;
        if (openingBrace == '[' && streamArrayElements)
        {
            state = ST_DECODING_ARRAY_STREAM;
        }
        else
        {
            state = ST_DECODING_NORMAL;
        }
    }

    private void Reset()
    {
        insideString = false;
        state = ST_INIT;
        openBraces = 0;
    }

    // CLR: the source calls Character.isWhitespace with a byte, which widens to the int code point overload with sign
    // extension (JLS 5.1.2: https://docs.oracle.com/javase/specs/jls/se21/html/jls-5.html#jls-5.1.2), so 0x80-0xFF are
    // negative and never white space. Java white space below 0x80 is U+0009-U+000D, U+001C-U+001F and the space separator
    // U+0020 (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Character.html#isWhitespace(int)).
    // char.IsWhiteSpace differs: it excludes U+001C-U+001F and includes U+0085 and U+00A0
    // (https://learn.microsoft.com/dotnet/api/system.char.iswhitespace), so the Java rule is written out here.
    private static bool IsWhitespace(byte c)
    {
        return c is >= 0x09 and <= 0x0D or >= 0x1C and <= 0x20;
    }
}
