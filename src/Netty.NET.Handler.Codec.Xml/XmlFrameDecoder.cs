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

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Xml;

/**
 * A frame decoder for single separate XML based message streams.
 * <p/>
 * A couple examples will better help illustrate
 * what this decoder actually does.
 * <p/>
 * Given an input array of bytes split over 3 frames like this:
 * <pre>
 * +-----+-----+-----------+
 * | &lt;an | Xml | Element/&gt; |
 * +-----+-----+-----------+
 * </pre>
 * <p/>
 * this decoder would output a single frame:
 * <p/>
 * <pre>
 * +-----------------+
 * | &lt;anXmlElement/&gt; |
 * +-----------------+
 * </pre>
 *
 * Given an input array of bytes split over 5 frames like this:
 * <pre>
 * +-----+-----+-----------+-----+----------------------------------+
 * | &lt;an | Xml | Element/&gt; | &lt;ro | ot&gt;&lt;child&gt;content&lt;/child&gt;&lt;/root&gt; |
 * +-----+-----+-----------+-----+----------------------------------+
 * </pre>
 * <p/>
 * this decoder would output two frames:
 * <p/>
 * <pre>
 * +-----------------+-------------------------------------+
 * | &lt;anXmlElement/&gt; | &lt;root&gt;&lt;child&gt;content&lt;/child&gt;&lt;/root&gt; |
 * +-----------------+-------------------------------------+
 * </pre>
 *
 * <p/>
 * The byte stream is expected to be in UTF-8 character encoding or ASCII. The current implementation
 * uses direct {@code byte} to {@code char} cast and then compares that {@code char} to a few low range
 * ASCII characters like {@code '<'}, {@code '>'} or {@code '/'}. UTF-8 is not using low range [0..0x7F]
 * byte values for multibyte codepoint representations therefore fully supported by this implementation.
 * <p/>
 * Please note that this decoder is not suitable for
 * xml streaming protocols such as
 * <a href="https://xmpp.org/rfcs/rfc6120.html">XMPP</a>,
 * where an initial xml element opens the stream and only
 * gets closed at the end of the session, although this class
 * could probably allow for such type of message flow with
 * minor modifications.
 */
// CLR: List<Object> out is IList<object> @out and 'in' is @in (C# keywords; docs/codec-base-porting.md), ByteBuf
// readerIndex()/writerIndex()/readableBytes() are the Buffer port's properties and checkPositive is the Common
// ObjectUtil.CheckPositive, which throws ArgumentException (IllegalArgumentException). ByteBuf.getByte returns an
// unsigned System.Byte (owner decision), while the Java byte widens to int with sign extension ("A widening conversion
// of a signed integer value to an integral type T simply sign-extends",
// https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.2): bytes 0x80-0xFF are negative in Java and
// 128-255 here. Every comparison of a read byte is with an ASCII constant ('<', '>', '/', '!', '?', '-', '[', ']', the
// CDATA[ letters and the a-z/A-Z/':'/'_' name start ranges, all at most 0x7A), which a byte of 0x80-0xFF never equals
// or falls into in either language, so the results are the Java results without a cast. Numbers in messages use the
// invariant culture, because Int32/Int64.ToString() use "the NumberFormatInfo object of the current culture"
// (https://learn.microsoft.com/en-us/dotnet/api/system.int64.tostring?view=net-10.0) while Java decimal text does not
// depend on a culture.
public class XmlFrameDecoder : ByteToMessageDecoder
{

    private readonly int maxFrameLength;

    private bool openingBracketFound;
    private bool atLeastOneXmlElementFound;
    private bool inCDATASection;
    private bool inCommentBlock;
    private bool inProcessingInstruction;
    private bool inClosingTag;
    private long openBracketsCount;

    /**
     * Index, relative to the current reader index, of the byte right after the last {@code '>'}
     * seen so far. Netty's cumulation buffer may be compacted (rebasing both the reader index and
     * the bytes) in between two {@link #decode(ChannelHandlerContext, ByteBuf, List)} invocations,
     * which would silently invalidate an absolute buffer index stored across calls; storing this
     * relative to the reader index keeps it valid across such compactions, just like {@link
     * #scanOffset}.
     */
    private int length;
    private int leadingWhiteSpaceCount;

    /**
     * Index, relative to the current reader index, of the next byte that still needs to be scanned.
     * This lets {@link #decode(ChannelHandlerContext, ByteBuf, List)} resume scanning where the
     * previous invocation left off instead of rescanning the whole accumulated buffer every time,
     * which would otherwise make a slowly trickled, never-balanced element run in quadratic time.
     */
    private int scanOffset;

    public XmlFrameDecoder(int maxFrameLength)
    {
        this.maxFrameLength = CheckPositive(maxFrameLength, "maxFrameLength");
    }

    // CLR: the labeled "break scan" is "goto scan", whose label sits on the first statement after the scan loop, as the
    // codec-compression port does (docs/codec-compression-porting.md): a labeled break exists only "Starting in C# 15"
    // and "The goto statement transfers control to a statement that a label marks"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/jump-statements); the plain break
    // inside the switch only leaves the switch, as in Java. throw new Error() is UnreachableException: "The exception
    // that is thrown when the program executes an instruction that was thought to be unreachable"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.unreachableexception?view=net-10.0), the
    // java.lang.Error mapping of docs/codec-memcache-porting.md.
    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        int bufferLength = @in.WriterIndex;

        if (bufferLength > maxFrameLength)
        {
            // bufferLength exceeded maxFrameLength; dropping frame
            @in.SkipBytes(@in.ReadableBytes);
            ResetState();
            Fail(bufferLength);
            return;
        }

        int readerIndex = @in.ReaderIndex;
        int i;

        for (i = readerIndex + scanOffset; i < bufferLength; i++)
        {
            byte readByte = @in.GetByte(i);
            if (!openingBracketFound && IsWhitespace(readByte))
            {
                // xml has not started and whitespace char found
                leadingWhiteSpaceCount++;
            }
            else if (!openingBracketFound && readByte != '<')
            {
                // garbage found before xml start
                Fail(ctx);
                @in.SkipBytes(@in.ReadableBytes);
                ResetState();
                return;
            }
            else if (inClosingTag && readByte == '<')
            {
                Fail(ctx);
                @in.SkipBytes(@in.ReadableBytes);
                ResetState();
                return;
            }
            else if (!inCDATASection && !inCommentBlock && !inProcessingInstruction && readByte == '<')
            {
                openingBracketFound = true;

                if (i < bufferLength - 1)
                {
                    byte peekAheadByte = @in.GetByte(i + 1);
                    if (peekAheadByte == '/')
                    {
                        // found </, we must check if it is enclosed
                        inClosingTag = true;
                    }
                    else if (IsValidStartCharForXmlElement(peekAheadByte))
                    {
                        atLeastOneXmlElementFound = true;
                        // char after < is a valid xml element start char,
                        // incrementing openBracketsCount
                        openBracketsCount++;
                    }
                    else if (peekAheadByte == '!')
                    {
                        BangMarkupKind markupKind = ClassifyBangMarkup(@in, i, bufferLength);

                        switch (markupKind)
                        {
                            case BangMarkupKind.INDETERMINATE:
                                // Not enough data has been received yet to tell whether this is
                                // a <!-- comment -->, a <![CDATA[ block, or some other markup
                                // declaration (e.g. <!DOCTYPE ...> or an unknown <!x>); wait for
                                // more bytes before resuming the scan from this position.
                                goto scan;
                            case BangMarkupKind.COMMENT:
                                // <!-- comment --> start found
                                openBracketsCount++;
                                inCommentBlock = true;
                                break;
                            case BangMarkupKind.CDATA:
                                // <![CDATA[ start found
                                openBracketsCount++;
                                inCDATASection = true;
                                break;
                            case BangMarkupKind.NONE:
                                // some other markup declaration (e.g. <!DOCTYPE ...> or <!x>);
                                // treated as inert content, same as everything else between tags.
                                break;
                            default:
                                throw new UnreachableException();
                        }
                    }
                    else if (peekAheadByte == '?')
                    {
                        // <?xml ?> start found
                        openBracketsCount++;
                        inProcessingInstruction = true;
                    }
                }
                else
                {
                    // not enough data yet to peek at the byte following '<'; wait for more
                    // and resume the scan from this same position.
                    break;
                }
            }
            else if (!inCDATASection && !inCommentBlock && !inProcessingInstruction && readByte == '/')
            {
                if (i < bufferLength - 1)
                {
                    if (@in.GetByte(i + 1) == '>')
                    {
                        // found />, decrementing openBracketsCount
                        openBracketsCount--;
                    }
                }
                else
                {
                    // not enough data yet to peek at the byte following '/'; wait for more
                    // and resume the scan from this same position.
                    break;
                }
            }
            else if (readByte == '>')
            {
                length = i + 1 - readerIndex;

                if (i - 1 > -1)
                {
                    byte peekBehindByte = @in.GetByte(i - 1);

                    if (inCommentBlock)
                    {
                        if (peekBehindByte == '-' && i - 2 > -1 && @in.GetByte(i - 2) == '-')
                        {
                            // a <!-- comment --> was closed
                            openBracketsCount--;
                            inCommentBlock = false;
                        }
                    }
                    else if (inProcessingInstruction)
                    {
                        if (peekBehindByte == '?')
                        {
                            // an <?xml ?> tag was closed
                            openBracketsCount--;
                            inProcessingInstruction = false;
                        }
                    }
                    else if (inClosingTag)
                    {
                        openBracketsCount--;
                        inClosingTag = false;
                    }
                    else if (!inCDATASection)
                    {
                        if (peekBehindByte == '?')
                        {
                            // an <?xml ?> tag was closed
                            openBracketsCount--;
                        }
                        else if (peekBehindByte == '-' && i - 2 > -1 && @in.GetByte(i - 2) == '-')
                        {
                            // a <!-- comment --> was closed
                            openBracketsCount--;
                        }
                    }
                    else if (inCDATASection && peekBehindByte == ']' && i - 2 > -1 && @in.GetByte(i - 2) == ']')
                    {
                        // a <![CDATA[...]]> block was closed
                        openBracketsCount--;
                        inCDATASection = false;
                    }
                }

                if (atLeastOneXmlElementFound && openBracketsCount == 0)
                {
                    // xml is balanced, bailing out
                    break;
                }
            }
        }

        scan:
        int xmlElementLength = length;

        if (openBracketsCount == 0 && xmlElementLength > 0)
        {
            if (readerIndex + xmlElementLength >= bufferLength)
            {
                xmlElementLength = @in.ReadableBytes;
            }

            ByteBuf frame =
                    ExtractFrame(@in, readerIndex + leadingWhiteSpaceCount, xmlElementLength - leadingWhiteSpaceCount);
            @in.SkipBytes(xmlElementLength);
            // a full element was extracted; reset all parser state (including the scan offset)
            // so the next invocation starts scanning a fresh element from the current reader index.
            ResetState();
            @out.Add(frame);
        }
        else
        {
            // no complete, balanced element yet; remember how far we scanned so the next
            // invocation resumes from here instead of rescanning the whole buffer.
            scanOffset = i - readerIndex;
        }
    }

    private void ResetState()
    {
        openingBracketFound = false;
        atLeastOneXmlElementFound = false;
        inCDATASection = false;
        inCommentBlock = false;
        inProcessingInstruction = false;
        inClosingTag = false;
        openBracketsCount = 0;
        length = 0;
        leadingWhiteSpaceCount = 0;
        scanOffset = 0;
    }

    private void Fail(long frameLength)
    {
        if (frameLength > 0)
        {
            throw new TooLongFrameException(
                            "frame length exceeds " + maxFrameLength.ToString(CultureInfo.InvariantCulture) + ": " +
                            frameLength.ToString(CultureInfo.InvariantCulture) + " - discarded");
        }
        else
        {
            throw new TooLongFrameException(
                            "frame length exceeds " + maxFrameLength.ToString(CultureInfo.InvariantCulture) +
                            " - discarding");
        }
    }

    private static void Fail(IChannelHandlerContext ctx)
    {
        ctx.FireExceptionCaught(new CorruptedFrameException("frame contains content before the xml starts"));
    }

    private static ByteBuf ExtractFrame(ByteBuf buffer, int index, int length)
    {
        return buffer.Copy(index, length);
    }

    /**
     * Asks whether the given byte is a valid
     * start char for an xml element name.
     * <p/>
     * Please refer to the
     * <a href="https://www.w3.org/TR/2004/REC-xml11-20040204/#NT-NameStartChar">NameStartChar</a>
     * formal definition in the W3C XML spec for further info.
     *
     * @param b the input char
     * @return true if the char is a valid start char
     */
    private static bool IsValidStartCharForXmlElement(byte b)
    {
        return b >= 'a' && b <= 'z' || b >= 'A' && b <= 'Z' || b == ':' || b == '_';
    }

    // CLR: the source calls Character.isWhitespace with the byte, which widens with sign extension to the int code point
    // overload (JLS 5.1.2, https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.2), so 0x80-0xFF are
    // negative and never white space. Below 0x80 a Java whitespace character is U+0009-U+000D, U+001C-U+001F or a space
    // separator (U+0020)
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Character.html#isWhitespace(int)), the
    // rule codec-base's JsonObjectDecoder writes out instead of char.IsWhiteSpace (docs/codec-base-porting.md, Text), so
    // this helper keeps the Java result for every unsigned byte value.
    private static bool IsWhitespace(byte b)
    {
        return b is >= 0x09 and <= 0x0D or >= 0x1C and <= 0x20;
    }

    /**
     * The outcome of classifying a {@code '<!'} construct against the {@code <!--} and
     * {@code <![CDATA[} start markers.
     */
    private enum BangMarkupKind
    {
        /** Not enough data has been received yet to determine the kind of construct. */
        INDETERMINATE,
        /** Neither a comment nor a CDATA start, e.g. {@code <!DOCTYPE ...>} or {@code <!x>}. */
        NONE,
        /** A {@code <!-- comment -->} start. */
        COMMENT,
        /** A {@code <![CDATA[} start. */
        CDATA
    }

    // CLR: Java assigns the char constants to byte elements by a narrowing conversion of a constant expression ("if the
    // variable is of type byte, short, or char, and the value of the constant expression is representable",
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.2), while the C# implicit constant expression
    // conversion to byte covers only constants of type int and char converts to byte only explicitly
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/conversions), so each
    // element is cast with (byte). The static final array is static readonly.
    private static readonly byte[] CDATA_START_SUFFIX = { (byte) 'C', (byte) 'D', (byte) 'A', (byte) 'T', (byte) 'A', (byte) '[' };

    /**
     * Classifies the markup declaration starting at {@code in.getByte(i) == '<'} followed by
     * {@code '!'}, matching it incrementally against the {@code <!--} and {@code <![CDATA[}
     * prefixes so a genuinely different declaration (e.g. {@code <!DOCTYPE ...>} or an unknown
     * {@code <!x>}) is recognised as such immediately, without waiting for bytes that would only
     * be needed to confirm a comment or CDATA start.
     */
    private static BangMarkupKind ClassifyBangMarkup(ByteBuf @in, int i, int bufferLength)
    {
        int commentOrCDATAMarker = i + 2;
        if (commentOrCDATAMarker >= bufferLength)
        {
            return BangMarkupKind.INDETERMINATE;
        }

        byte b = @in.GetByte(commentOrCDATAMarker);
        if (b == '-')
        {
            int secondDash = i + 3;
            if (secondDash >= bufferLength)
            {
                return BangMarkupKind.INDETERMINATE;
            }

            return @in.GetByte(secondDash) == '-' ? BangMarkupKind.COMMENT : BangMarkupKind.NONE;
        }

        if (b == '[')
        {
            for (int k = 0; k < CDATA_START_SUFFIX.Length; k++)
            {
                int idx = i + 3 + k;
                if (idx >= bufferLength)
                {
                    return BangMarkupKind.INDETERMINATE;
                }
                if (@in.GetByte(idx) != CDATA_START_SUFFIX[k])
                {
                    return BangMarkupKind.NONE;
                }
            }

            return BangMarkupKind.CDATA;
        }

        return BangMarkupKind.NONE;
    }

}
