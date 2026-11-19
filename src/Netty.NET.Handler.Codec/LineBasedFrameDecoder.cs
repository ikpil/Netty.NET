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

using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec;

/**
 * A decoder that splits the received {@link ByteBuf}s on line endings.
 * <p>
 * Both {@code "\n"} and {@code "\r\n"} are handled.
 * <p>
 * The byte stream is expected to be in UTF-8 character encoding or ASCII. The current implementation
 * uses direct {@code byte} to {@code char} cast and then compares that {@code char} to a few low range
 * ASCII characters like {@code '\n'} or {@code '\r'}. UTF-8 is not using low range [0..0x7F]
 * byte values for multibyte codepoint representations therefore fully supported by this implementation.
 * <p>
 * For a more general delimiter-based decoder, see {@link DelimiterBasedFrameDecoder}.
 * <p>
 * Users should be aware that used as is, the lenient approach on lone {@code '\n} might result on a parser
 * diffenrencial on line based protocols requiring the use of {@code "\r\n"} delimiters like SMTP and can
 * result in attacks similar to
 * <a href="https://sec-consult.com/blog/detail/smtp-smuggling-spoofing-e-mails-worldwide/">SMTP smuggling</a>.
 * Validating afterward the end of line pattern can be a possible mitigation.
 */
// CLR: List<Object> out is IList<object> @out and 'in' is @in (C# keywords); numbers in messages use the invariant
// culture (Int32.ToString() otherwise uses "the NumberFormatInfo object of the current culture",
// https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public class LineBasedFrameDecoder : ByteToMessageDecoder
{
    /** Maximum length of a frame we're willing to decode.  */
    private readonly int maxLength;
    /** Whether or not to throw an exception as soon as we exceed maxLength. */
    private readonly bool failFast;
    private readonly bool stripDelimiter;

    /** True if we're discarding input because we're already over maxLength.  */
    private bool discarding;
    private int discardedBytes;

    /** Last scan position. */
    private int offset;

    /**
     * Creates a new decoder.
     * @param maxLength  the maximum length of the decoded frame.
     *                   A {@link TooLongFrameException} is thrown if
     *                   the length of the frame exceeds this value.
     */
    public LineBasedFrameDecoder(int maxLength) : this(maxLength, true, false)
    {
    }

    /**
     * Creates a new decoder.
     * @param maxLength  the maximum length of the decoded frame.
     *                   A {@link TooLongFrameException} is thrown if
     *                   the length of the frame exceeds this value.
     * @param stripDelimiter  whether the decoded frame should strip out the
     *                        delimiter or not
     * @param failFast  If <tt>true</tt>, a {@link TooLongFrameException} is
     *                  thrown as soon as the decoder notices the length of the
     *                  frame will exceed <tt>maxFrameLength</tt> regardless of
     *                  whether the entire frame has been read.
     *                  If <tt>false</tt>, a {@link TooLongFrameException} is
     *                  thrown after the entire frame that exceeds
     *                  <tt>maxFrameLength</tt> has been read.
     */
    public LineBasedFrameDecoder(int maxLength, bool stripDelimiter, bool failFast)
    {
        this.maxLength = maxLength;
        this.failFast = failFast;
        this.stripDelimiter = stripDelimiter;
    }

    protected sealed override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        object decoded = Decode(ctx, @in);
        if (decoded != null)
        {
            @out.Add(decoded);
        }
    }

    /**
     * Create a frame out of the {@link ByteBuf} and return it.
     *
     * @param   ctx             the {@link ChannelHandlerContext} which this {@link ByteToMessageDecoder} belongs to
     * @param   buffer          the {@link ByteBuf} from which to read data
     * @return  frame           the {@link ByteBuf} which represent the frame or {@code null} if no frame could
     *                          be created.
     */
    // CLR: protected internal, since the source's protected also grants the package access that
    // DelimiterBasedFrameDecoder uses ("accessible from the current assembly or from types that are derived from the
    // containing class", https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal).
    protected internal virtual object Decode(IChannelHandlerContext ctx, ByteBuf buffer)
    {
        int eol = FindEndOfLine(buffer);
        if (!discarding)
        {
            if (eol >= 0)
            {
                ByteBuf frame;
                int length = eol - buffer.ReaderIndex;
                int delimLength = buffer.GetByte(eol) == '\r'? 2 : 1;

                if (length > maxLength)
                {
                    buffer.ReaderIndex = eol + delimLength;
                    Fail(ctx, length);
                    return null;
                }

                if (stripDelimiter)
                {
                    frame = buffer.ReadRetainedSlice(length);
                    buffer.SkipBytes(delimLength);
                }
                else
                {
                    frame = buffer.ReadRetainedSlice(length + delimLength);
                }

                return frame;
            }
            else
            {
                int length = buffer.ReadableBytes;
                if (length > maxLength)
                {
                    discardedBytes = length;
                    buffer.ReaderIndex = buffer.WriterIndex;
                    discarding = true;
                    offset = 0;
                    if (failFast)
                    {
                        Fail(ctx, "over " + discardedBytes.ToString(CultureInfo.InvariantCulture));
                    }
                }
                return null;
            }
        }
        else
        {
            if (eol >= 0)
            {
                int length = discardedBytes + eol - buffer.ReaderIndex;
                int delimLength = buffer.GetByte(eol) == '\r'? 2 : 1;
                buffer.ReaderIndex = eol + delimLength;
                discardedBytes = 0;
                discarding = false;
                if (!failFast)
                {
                    Fail(ctx, length);
                }
            }
            else
            {
                discardedBytes += buffer.ReadableBytes;
                buffer.ReaderIndex = buffer.WriterIndex;
                // We skip everything in the buffer, we need to set the offset to 0 again.
                offset = 0;
            }
            return null;
        }
    }

    private void Fail(IChannelHandlerContext ctx, int length)
    {
        Fail(ctx, length.ToString(CultureInfo.InvariantCulture));
    }

    private void Fail(IChannelHandlerContext ctx, string length)
    {
        ctx.FireExceptionCaught(
                new TooLongFrameException(
                        "frame length (" + length + ") exceeds the allowed maximum (" +
                        maxLength.ToString(CultureInfo.InvariantCulture) + ')'));
    }

    /**
     * Returns the index in the buffer of the end of line found.
     * Returns -1 if no end of line was found in the buffer.
     */
    private int FindEndOfLine(ByteBuf buffer)
    {
        int totalLength = buffer.ReadableBytes;
        int i = buffer.IndexOf(buffer.ReaderIndex + offset,
                               buffer.ReaderIndex + totalLength, (byte) '\n');
        if (i >= 0)
        {
            offset = 0;
            if (i > 0 && buffer.GetByte(i - 1) == '\r')
            {
                i--;
            }
        }
        else
        {
            offset = totalLength;
        }
        return i;
    }
}
