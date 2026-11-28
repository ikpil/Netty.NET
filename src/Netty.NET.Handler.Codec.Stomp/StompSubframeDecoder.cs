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

using System;
using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.ByteBufUtil;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Stomp;

// CLR: the decoder uses its own @Deprecated State, which javac does not report because "the declaration where the use
// appears and the declaration of the ordinarily deprecated program element are both within the same outermost class"
// (JLS 9.6.4.6: https://docs.oracle.com/javase/specs/jls/se17/html/jls-9.html#jls-9.6.4.6); C# reports every reference
// to an [Obsolete] member (CS0618: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0618),
// so the warning is disabled for this file only.
#pragma warning disable CS0618

/**
 * Decodes {@link ByteBuf}s into {@link StompHeadersSubframe}s and {@link StompContentSubframe}s.
 *
 * <h3>Parameters to control memory consumption: </h3>
 * {@code maxLineLength} the maximum length of line - restricts length of command and header lines If the length of the
 * initial line exceeds this value, a {@link TooLongFrameException} will be raised.
 * <br>
 * {@code maxChunkSize} The maximum length of the content or each chunk.  If the content length (or the length of each
 * chunk) exceeds this value, the content or chunk ill be split into multiple {@link StompContentSubframe}s whose length
 * is {@code maxChunkSize} at maximum.
 * <br>
 * {@code maxNumHeaders} The maximum number of headers per frame.
 * If this limit exceeded a {@link TooLongFrameException} will be raised.
 *
 * <h3>Chunked Content</h3>
 * <p>
 * If the content of a stomp message is greater than {@code maxChunkSize} the transfer encoding of the HTTP message is
 * 'chunked', this decoder generates multiple {@link StompContentSubframe} instances to avoid excessive memory
 * consumption. Note, that every message, even with no content decodes with {@link LastStompContentSubframe} at the end
 * to simplify upstream message parsing.
 */
// CLR: Java keeps types and methods in separate name spaces, but the nested type State hides the inherited
// ReplayingDecoder.State() methods in C# ("a constant, field, property, or type... hides all base class members
// that share its name", https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/new-modifier),
// so the enum is declared new and state() is called as base.State(). Signal is an Exception here but a
// java.lang.Error in the source, which catch (Exception) does not catch, so the two catch (Exception) blocks
// filter when (e is not Signal) and the REPLAY signal reaches ReplayingDecoder as in Java. The C#
// ReplayingDecoderByteBuf view reads WriterIndex as int.MaxValue while not terminated, where the Java view returns
// the cumulation's writer index, so in.writerIndex() is the cumulation's InternalBuffer().WriterIndex. The switch
// sections that fall through end with goto case
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements). The static
// import ByteBufUtil.* is using static ByteBufUtil: ReadBytes(ctx.Alloc(), in, length) reads through
// in.ReadBytes(ByteBuf dst), which the ReplayingDecoderByteBuf view supports, when the allocator returns exactly
// length writable bytes (src/Netty.NET.Buffer/ByteBufUtil.cs; the owner-revisit item of 2026-11-19, done
// 2026-11-24).
public class StompSubframeDecoder : ReplayingDecoder<StompSubframeDecoder.State>
{

    private const int DEFAULT_CHUNK_SIZE = 8132;
    private const int DEFAULT_MAX_LINE_LENGTH = 1024;
    private const int DEFAULT_MAX_NUMBER_HEADERS = 128;

    /**
     * @deprecated this should never be used by an user!
     */
    [Obsolete("this should never be used by an user!")]
    public new enum State
    {
        SKIP_CONTROL_CHARACTERS,
        READ_HEADERS,
        READ_CONTENT,
        FINALIZE_FRAME_READ,
        BAD_FRAME,
        INVALID_CHUNK
    }

    private readonly Utf8LineParser commandParser;
    private readonly HeaderParser headerParser;
    private readonly int maxChunkSize;
    private int alreadyReadChunkSize;
    private ILastStompContentSubframe lastContent;
    private long contentLength = -1;

    public StompSubframeDecoder() : this(DEFAULT_MAX_LINE_LENGTH, DEFAULT_CHUNK_SIZE)
    {
    }

    public StompSubframeDecoder(bool validateHeaders)
        : this(DEFAULT_MAX_LINE_LENGTH, DEFAULT_CHUNK_SIZE, DEFAULT_MAX_NUMBER_HEADERS, validateHeaders)
    {
    }

    public StompSubframeDecoder(int maxLineLength, int maxChunkSize) : this(maxLineLength, maxChunkSize, false)
    {
    }

    public StompSubframeDecoder(int maxLineLength, int maxChunkSize, bool validateHeaders)
        : this(maxLineLength, maxChunkSize, DEFAULT_MAX_NUMBER_HEADERS, validateHeaders)
    {
    }

    public StompSubframeDecoder(int maxLineLength, int maxChunkSize, int maxNumHeaders, bool validateHeaders)
        : base(State.SKIP_CONTROL_CHARACTERS)
    {
        CheckPositive(maxLineLength, "maxLineLength");
        CheckPositive(maxChunkSize, "maxChunkSize");
        CheckPositive(maxNumHeaders, "maxNumHeaders");

        this.maxChunkSize = maxChunkSize;
        commandParser = new Utf8LineParser(new AppendableCharSequence(16), maxLineLength);
        headerParser = new HeaderParser(new AppendableCharSequence(128), maxLineLength, maxNumHeaders, validateHeaders);
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        switch (base.State())
        {
            case State.SKIP_CONTROL_CHARACTERS:
                SkipControlCharacters(@in);
                Checkpoint(State.READ_HEADERS);
                // Fall through.
                goto case State.READ_HEADERS;
            case State.READ_HEADERS:
                StompCommand command = StompCommand.UNKNOWN;
                IStompHeadersSubframe frame = null;

                try
                {
                    command = ReadCommand(@in);
                    frame = new DefaultStompHeadersSubframe(command);
                    Checkpoint(ReadHeaders(@in, frame));
                    @out.Add(frame);
                }
                catch (Exception e) when (e is not Signal)
                {
                    if (frame == null)
                    {
                        frame = new DefaultStompHeadersSubframe(command);
                    }

                    frame.SetDecoderResult(DecoderResult.Failure(e));
                    @out.Add(frame);
                    Checkpoint(State.BAD_FRAME);
                    return;
                }

                break;
            case State.BAD_FRAME:
                @in.SkipBytes(ActualReadableBytes());
                return;
        }

        try
        {
            switch (base.State())
            {
                case State.READ_CONTENT:
                    int toRead = @in.ReadableBytes;
                    if (toRead == 0)
                    {
                        return;
                    }

                    if (toRead > maxChunkSize)
                    {
                        toRead = maxChunkSize;
                    }

                    if (contentLength >= 0)
                    {
                        int remainingLength = (int) (contentLength - alreadyReadChunkSize);
                        if (toRead > remainingLength)
                        {
                            toRead = remainingLength;
                        }

                        ByteBuf chunkBuffer = ReadBytes(ctx.Alloc(), @in, toRead);

                        if ((alreadyReadChunkSize += toRead) >= contentLength)
                        {
                            lastContent = new DefaultLastStompContentSubframe(chunkBuffer);
                            Checkpoint(State.FINALIZE_FRAME_READ);
                        }
                        else
                        {
                            @out.Add(new DefaultStompContentSubframe(chunkBuffer));
                            return;
                        }
                    }
                    else
                    {
                        int nulIndex = ByteBufUtil.IndexOf(@in, @in.ReaderIndex, InternalBuffer().WriterIndex,
                                StompConstants.NUL);
                        if (nulIndex == @in.ReaderIndex)
                        {
                            Checkpoint(State.FINALIZE_FRAME_READ);
                        }
                        else
                        {
                            if (nulIndex > 0)
                            {
                                toRead = nulIndex - @in.ReaderIndex;
                            }
                            else
                            {
                                toRead = InternalBuffer().WriterIndex - @in.ReaderIndex;
                            }

                            ByteBuf chunkBuffer = ReadBytes(ctx.Alloc(), @in, toRead);
                            alreadyReadChunkSize += toRead;

                            if (nulIndex > 0)
                            {
                                lastContent = new DefaultLastStompContentSubframe(chunkBuffer);
                                Checkpoint(State.FINALIZE_FRAME_READ);
                            }
                            else
                            {
                                @out.Add(new DefaultStompContentSubframe(chunkBuffer));
                                return;
                            }
                        }
                    }

                    // Fall through.
                    goto case State.FINALIZE_FRAME_READ;
                case State.FINALIZE_FRAME_READ:
                    SkipNullCharacter(@in);

                    if (lastContent == null)
                    {
                        lastContent = ILastStompContentSubframe.EMPTY_LAST_CONTENT;
                    }

                    @out.Add(lastContent);
                    ResetDecoder();
                    break;
            }
        }
        catch (Exception e) when (e is not Signal)
        {
            ReleaseLastContentIfNeeded();

            IStompContentSubframe errorContent = new DefaultLastStompContentSubframe(Unpooled.EmptyBuffer);
            errorContent.SetDecoderResult(DecoderResult.Failure(e));
            @out.Add(errorContent);
            Checkpoint(State.BAD_FRAME);
        }
    }

    private void ReleaseLastContentIfNeeded()
    {
        if (lastContent != null)
        {
            lastContent.Release();
            lastContent = null;
        }
    }

    protected override void HandlerRemoved0(IChannelHandlerContext ctx)
    {
        ReleaseLastContentIfNeeded();
    }

    // CLR: StompCommand.valueOf(String) accepts a name only if it "must match exactly an identifier used to declare an
    // enum constant" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Enum.html#valueOf(java.lang.Class,java.lang.String)),
    // while Enum.Parse also accepts "the name or numeric value of one or more enumerated constants"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.enum.parse); the name is therefore checked with
    // Enum.IsDefined (https://learn.microsoft.com/en-us/dotnet/api/system.enum.isdefined) and an undefined one throws the
    // ArgumentException that IllegalArgumentException maps to (docs/codec-base-porting.md, Shared CLR adaptations).
    private StompCommand ReadCommand(ByteBuf @in)
    {
        ICharSequence commandSequence = commandParser.Parse(@in);
        if (commandSequence == null)
        {
            throw new DecoderException("Failed to read command from channel");
        }

        string commandStr = commandSequence.ToString();

        try
        {
            return Enum.IsDefined(typeof(StompCommand), commandStr)
                    ? Enum.Parse<StompCommand>(commandStr)
                    : throw new ArgumentException("No enum constant StompCommand." + commandStr);
        }
        catch (ArgumentException)
        {
            throw new DecoderException("Cannot to parse command " + commandStr);
        }
    }

    private State ReadHeaders(ByteBuf buffer, IStompHeadersSubframe headersSubframe)
    {
        IStompHeaders headers = headersSubframe.Headers();
        // If the headers were not complete before, ReplayingDecoder parses all of them again,
        // so count them from zero.
        headerParser.ResetNumHeaders();

        for (;;)
        {
            bool headerRead = headerParser.ParseHeader(headersSubframe, buffer);
            if (!headerRead)
            {
                if (headers.Contains(IStompHeaders.CONTENT_LENGTH))
                {
                    contentLength = GetContentLength(headers);
                    if (contentLength == 0)
                    {
                        return State.FINALIZE_FRAME_READ;
                    }
                }

                return State.READ_CONTENT;
            }
        }
    }

    // CLR: the number in the message uses the invariant culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
    private static long GetContentLength(IStompHeaders headers)
    {
        long contentLength = headers.GetLong(IStompHeaders.CONTENT_LENGTH, 0L);
        if (contentLength < 0)
        {
            throw new DecoderException(IStompHeaders.CONTENT_LENGTH + " must be non-negative");
        }
        if (contentLength > int.MaxValue)
        {
            throw new TooLongFrameException(IStompHeaders.CONTENT_LENGTH + " exceeds the maximum allowed value: "
                    + contentLength.ToString(CultureInfo.InvariantCulture));
        }

        return contentLength;
    }

    // CLR: Java concatenates the signed byte (-128..127) into the message; the System.Byte that readByte() returns is
    // cast to sbyte to keep that text (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
    // IllegalStateException is InvalidOperationException (docs/codec-base-porting.md, Shared CLR adaptations).
    private static void SkipNullCharacter(ByteBuf buffer)
    {
        byte b = buffer.ReadByte();
        if (b != StompConstants.NUL)
        {
            throw new InvalidOperationException("unexpected byte in buffer " +
                    ((sbyte) b).ToString(CultureInfo.InvariantCulture) + " while expecting NULL byte");
        }
    }

    private static void SkipControlCharacters(ByteBuf buffer)
    {
        byte b;

        for (;;)
        {
            if (!buffer.IsReadable())
            {
                return;
            }

            b = buffer.ReadByte();
            if (b != StompConstants.CR && b != StompConstants.LF)
            {
                buffer.ReaderIndex = buffer.ReaderIndex - 1;
                break;
            }
        }
    }

    private void ResetDecoder()
    {
        Checkpoint(State.SKIP_CONTROL_CHARACTERS);
        contentLength = -1;
        alreadyReadChunkSize = 0;
        lastContent = null;
    }

    // CLR: the class implements ByteProcessor, which is Func<byte, bool> in the Buffer port, through its virtual
    // Process method group. Sign: the INITIAL BRANCH tests nextByte >= 0 on the signed Java byte (an ASCII byte);
    // the System.Byte is read as (sbyte) there
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
    // The other masks (& 0x3F, & 0xE0, & 0x1F, & 0x0F) give the same bits for the signed and the unsigned value.
    // The char compound assignment casts its int operand, which Java narrows implicitly. The line length in the
    // message uses the invariant culture.
    private class Utf8LineParser
    {

        private readonly AppendableCharSequence charSeq;
        private readonly int maxLineLength;

        private int lineLength;
        private char interim;
        private bool nextRead;

        internal Utf8LineParser(AppendableCharSequence charSeq, int maxLineLength)
        {
            this.charSeq = CheckNotNull(charSeq, "charSeq");
            this.maxLineLength = maxLineLength;
        }

        internal AppendableCharSequence Parse(ByteBuf byteBuf)
        {
            Reset();
            int offset = byteBuf.ForEachByte(Process);
            if (offset == -1)
            {
                return null;
            }

            byteBuf.ReaderIndex = offset + 1;
            return charSeq;
        }

        internal AppendableCharSequence CharSequence()
        {
            return charSeq;
        }

        public virtual bool Process(byte nextByte)
        {
            if (nextByte == StompConstants.CR)
            {
                interim = (char) 0;
                nextRead = false;
                ++lineLength;
                return true;
            }

            if (nextByte == StompConstants.LF)
            {
                return false;
            }

            if (++lineLength > maxLineLength)
            {
                throw new TooLongFrameException("An STOMP line is larger than " +
                        maxLineLength.ToString(CultureInfo.InvariantCulture) + " bytes.");
            }

            // 1 byte   -   0xxxxxxx                    -  7 bits
            // 2 byte   -   110xxxxx 10xxxxxx           -  11 bits
            // 3 byte   -   1110xxxx 10xxxxxx 10xxxxxx  -  16 bits
            if (nextRead)
            {
                interim |= (char) ((nextByte & 0x3F) << 6);
                nextRead = false;
            }
            else if (interim != 0)
            { // flush 2 or 3 byte
                AppendTo(charSeq, (char) (interim | (nextByte & 0x3F)));
                interim = (char) 0;
            }
            else if ((sbyte) nextByte >= 0)
            { // INITIAL BRANCH
                // The first 128 characters (US-ASCII) need one byte.
                AppendTo(charSeq, (char) nextByte);
            }
            else if ((nextByte & 0xE0) == 0xC0)
            {
                // The next 1920 characters need two bytes and we can define
                // a first byte by mask 110xxxxx.
                interim = (char) ((nextByte & 0x1F) << 6);
            }
            else
            {
                // The rest of characters need three bytes.
                interim = (char) ((nextByte & 0x0F) << 12);
                nextRead = true;
            }

            return true;
        }

        protected virtual void AppendTo(AppendableCharSequence charSeq, char chr)
        {
            charSeq.Append(chr);
        }

        protected virtual void Reset()
        {
            charSeq.Reset();
            lineLength = 0;
            interim = (char) 0;
            nextRead = false;
        }
    }

    // CLR: Java String is a CharSequence, but C# string is not an ICharSequence, so the header name and value
    // Strings are added as the Common StringCharSequence, whose Equals matches String.equals.
    // IllegalArgumentException is ArgumentException, and the header count uses the invariant culture.
    private sealed class HeaderParser : Utf8LineParser
    {

        private readonly bool validateHeaders;
        private readonly int maxNumHeaders;
        private int numHeaders;
        private string name;
        private bool valid;

        private bool shouldUnescape;
        private bool unescapeInProgress;

        internal HeaderParser(AppendableCharSequence charSeq, int maxLineLength, int maxNumHeaders, bool validateHeaders)
            : base(charSeq, maxLineLength)
        {
            this.validateHeaders = validateHeaders;
            this.maxNumHeaders = maxNumHeaders;
        }

        internal void ResetNumHeaders()
        {
            numHeaders = 0;
        }

        internal bool ParseHeader(IStompHeadersSubframe headersSubframe, ByteBuf buf)
        {
            shouldUnescape = ShouldUnescape(headersSubframe.Command());
            AppendableCharSequence value = base.Parse(buf);
            if (value == null || (name == null && value.Length() == 0))
            {
                numHeaders = 0;
                return false;
            }

            numHeaders++;
            if (maxNumHeaders < numHeaders)
            {
                throw new TooLongFrameException("maximum number of headers exceeded: " +
                        maxNumHeaders.ToString(CultureInfo.InvariantCulture));
            }

            if (valid)
            {
                headersSubframe.Headers().Add(new StringCharSequence(name), new StringCharSequence(value.ToString()));
            }
            else if (validateHeaders)
            {
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException("received an invalid header line '" + value + '\'');
                }

                string line = name + ':' + value;
                throw new ArgumentException("a header value or name contains a prohibited character ':'"
                                                   + ", " + line);
            }

            return true;
        }

        public override bool Process(byte nextByte)
        {
            if (nextByte == StompConstants.COLON)
            {
                if (name == null)
                {
                    AppendableCharSequence charSeq = CharSequence();
                    if (charSeq.Length() != 0)
                    {
                        name = charSeq.Substring(0, charSeq.Length());
                        charSeq.Reset();
                        valid = true;
                        return true;
                    }
                    else
                    {
                        name = StringUtil.EMPTY_STRING;
                    }
                }
                else
                {
                    valid = false;
                }
            }

            return base.Process(nextByte);
        }

        protected override void AppendTo(AppendableCharSequence charSeq, char chr)
        {
            if (!shouldUnescape)
            {
                base.AppendTo(charSeq, chr);
                return;
            }

            if (chr == '\\')
            {
                if (unescapeInProgress)
                {
                    base.AppendTo(charSeq, chr);
                    unescapeInProgress = false;
                }
                else
                {
                    unescapeInProgress = true;
                }

                return;
            }

            if (unescapeInProgress)
            {
                if (chr == 'c')
                {
                    charSeq.Append(':');
                }
                else if (chr == 'r')
                {
                    charSeq.Append('\r');
                }
                else if (chr == 'n')
                {
                    charSeq.Append('\n');
                }
                else
                {
                    charSeq.Append('\\').Append(chr);
                    throw new ArgumentException("received an invalid escape header sequence '" + charSeq + '\'');
                }

                unescapeInProgress = false;
                return;
            }

            base.AppendTo(charSeq, chr);
        }

        protected override void Reset()
        {
            name = null;
            valid = false;
            unescapeInProgress = false;
            base.Reset();
        }

        private static bool ShouldUnescape(StompCommand command)
        {
            return command != StompCommand.CONNECT && command != StompCommand.CONNECTED;
        }
    }
}
