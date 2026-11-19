/*
 * Copyright 2016 The Netty Project
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
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Smtp;

/**
 * Decoder for SMTP responses.
 */
// CLR: @UnstableApi is the Common [UnstableApi] attribute. decode(ctx, buffer) overrides the codec-base
// LineBasedFrameDecoder.Decode(IChannelHandlerContext, ByteBuf) (protected internal there, so protected from this
// assembly) with the covariant return type ISmtpResponse. CharsetUtil.US_ASCII is Encoding.ASCII and the String detail
// is its ICharSequence wrapper StringCharSequence; Collections.emptyList()/singletonList(x) are read-only collections
// (ReadOnlyCollection<T>.Empty and a one-element ReadOnlyCollection<T>); CharSequence.length() is ICharSequence.Length().
// readByte() returns the unsigned System.Byte: the separator is compared only with ' ' and '-', so its sign does not
// change the branch taken. Numbers in messages use the invariant culture
// (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring). 'throws Exception' has no C# counterpart.
[UnstableApi]
public sealed class SmtpResponseDecoder : LineBasedFrameDecoder
{

    private const int DEFAULT_MAX_RESPONSE_SIZE = 64 * 1024;

    // Charged per accumulated line so the limit bounds retained heap rather than payload bytes alone.
    private const int DETAIL_ENTRY_OVERHEAD = 48;

    private readonly int maxResponseSize;

    private List<ICharSequence> details;
    private long responseSize;

    /**
     * Creates a new instance that enforces the given {@code maxLineLength} and a default limit of 64 KiB on the
     * accumulated size of a multi-line response.
     *
     * @param maxLineLength the maximum length of a single response line, in bytes.
     */
    public SmtpResponseDecoder(int maxLineLength) : this(maxLineLength, DEFAULT_MAX_RESPONSE_SIZE)
    {
    }

    /**
     * Creates a new instance that enforces the given {@code maxLineLength} and {@code maxResponseSize}.
     * <p>
     * The lines of a multi-line response are buffered until its terminating line arrives.
     * {@code maxResponseSize} bounds that buffering. Once it is exceeded, decoding fails with a
     * {@link TooLongFrameException}. The limit is approximate and tracks retained memory rather than bytes
     * received, as it charges each buffered line a fixed object overhead in addition to its detail bytes, and
     * ignores the response code, the separator and the terminating line.
     *
     * @param maxLineLength   the maximum length of a single response line, in bytes.
     * @param maxResponseSize the maximum accumulated size of a multi-line response, in bytes.
     */
    public SmtpResponseDecoder(int maxLineLength, int maxResponseSize) : base(maxLineLength)
    {
        this.maxResponseSize = ObjectUtil.CheckPositive(maxResponseSize, "maxResponseSize");
    }

    protected override ISmtpResponse Decode(IChannelHandlerContext ctx, ByteBuf buffer)
    {
        ByteBuf frame = (ByteBuf) base.Decode(ctx, buffer);
        if (frame == null)
        {
            // No full line received yet.
            return null;
        }
        try
        {
            int readable = frame.ReadableBytes;
            int readerIndex = frame.ReaderIndex;
            if (readable < 4)
            {
                Reset();
                throw NewDecoderException(buffer, readerIndex, readable);
            }
            int code = ParseCode(frame);
            int separator = frame.ReadByte();
            ICharSequence detail = frame.IsReadable() ? new StringCharSequence(frame.ToString(Encoding.ASCII)) : null;

            IList<ICharSequence> details = this.details;

            switch (separator)
            {
            case ' ':
                // Marks the end of a response.
                Reset();
                if (details != null)
                {
                    if (detail != null)
                    {
                        details.Add(detail);
                    }
                }
                else
                {
                    if (detail == null)
                    {
                        details = ReadOnlyCollection<ICharSequence>.Empty;
                    }
                    else
                    {
                        details = new ReadOnlyCollection<ICharSequence>(new ICharSequence[] { detail });
                    }
                }
                return new DefaultSmtpResponse(code, details);
            case '-':
                // Multi-line response.
                if (detail != null)
                {
                    responseSize += (long) detail.Length() + DETAIL_ENTRY_OVERHEAD;
                    if (responseSize > maxResponseSize)
                    {
                        Reset();
                        throw new TooLongFrameException("SMTP response exceeds " +
                                                        maxResponseSize.ToString(CultureInfo.InvariantCulture) + " bytes");
                    }
                    if (details == null)
                    {
                        // Using initial capacity as it is very unlikely that we will receive a multi-line response
                        // with more then 3 lines.
                        details = this.details = new List<ICharSequence>(4);
                    }
                    details.Add(detail);
                }
                break;
            default:
                Reset();
                throw NewDecoderException(buffer, readerIndex, readable);
            }
        }
        finally
        {
            frame.Release();
        }
        return null;
    }

    private void Reset()
    {
        this.details = null;
        this.responseSize = 0;
    }

    private static DecoderException NewDecoderException(ByteBuf buffer, int readerIndex, int readable)
    {
        return new DecoderException(
                "Received invalid line: '" + buffer.ToString(readerIndex, readable, Encoding.ASCII) + '\'');
    }

    /**
     * Parses the io.netty.handler.codec.smtp code without any allocation, which is three digits.
     */
    private static int ParseCode(ByteBuf buffer)
    {
        int first = ParseNumber(buffer.ReadByte()) * 100;
        int second = ParseNumber(buffer.ReadByte()) * 10;
        int third = ParseNumber(buffer.ReadByte());
        return first + second + third;
    }

    // CLR: (char) b of a Java byte first sign-extends to int and then narrows to char (JLS 5.1.4,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.4), so a byte 0x80..0xFF becomes
    // ﾀ..￿; the unsigned System.Byte is reinterpreted as sbyte before the char conversion to keep that char.
    // Character.digit(ch, 10) returns the decimal digit value when "isDigit is true of the character" (general
    // category DECIMAL_DIGIT_NUMBER) and -1 otherwise
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Character.html); for radix 10 that is
    // CharUnicodeInfo.GetDecimalDigitValue, which returns "-1, if the specified character is not a decimal digit"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.globalization.charunicodeinfo.getdecimaldigitvalue?view=net-10.0).
    private static int ParseNumber(byte b)
    {
        return CharUnicodeInfo.GetDecimalDigitValue(unchecked((char) (sbyte) b));
    }
}
