/*
 * Copyright 2016 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * Decodes the Redis protocol into {@link RedisMessage} objects following
 * <a href="https://redis.io/topics/protocol">RESP (REdis Serialization Protocol)</a>.
 *
 * {@link RedisMessage} parts can be aggregated to {@link RedisMessage} using
 * {@link RedisArrayAggregator} or processed directly.
 */
// CLR: RedisMessageType is a sealed class with static readonly constants (see RedisMessageType.cs), which are not
// compile-time constants, so the two switch statements on a RedisMessageType are if/else chains comparing the
// constants by reference (a switch case is a constant pattern,
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements). ByteProcessor is
// Func<byte, bool> (the Buffer ForEachByte parameter) and the processor passes its Process method group, as codec-base
// does. Numbers in messages use the invariant culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring),
// and CharsetUtil.UTF_8/US_ASCII are Encoding.UTF8/Encoding.ASCII (docs/codec-base-porting.md).
[UnstableApi]
public sealed class RedisDecoder : ByteToMessageDecoder
{

    private readonly ToPositiveLongProcessor toPositiveLongProcessor = new ToPositiveLongProcessor();

    private readonly bool decodeInlineCommands;
    private readonly int maxInlineMessageLength;
    private readonly IRedisMessagePool messagePool;

    // current decoding states
    private State state = State.DECODE_TYPE;
    private RedisMessageType type;
    private int remainingBulkLength;

    private enum State
    {
        DECODE_TYPE,
        DECODE_INLINE, // SIMPLE_STRING, ERROR, INTEGER
        DECODE_LENGTH, // BULK_STRING, ARRAY_HEADER
        DECODE_BULK_STRING_EOL,
        DECODE_BULK_STRING_CONTENT,
    }

    /**
     * Creates a new instance with default {@code maxInlineMessageLength} and {@code messagePool}
     * and inline command decoding disabled.
     */
    public RedisDecoder() : this(false)
    {
    }

    /**
     * Creates a new instance with default {@code maxInlineMessageLength} and {@code messagePool}.
     * @param decodeInlineCommands if {@code true}, inline commands will be decoded.
     */
    public RedisDecoder(bool decodeInlineCommands)
        : this(RedisConstants.REDIS_INLINE_MESSAGE_MAX_LENGTH, FixedRedisMessagePool.INSTANCE, decodeInlineCommands)
    {
    }

    /**
     * Creates a new instance with inline command decoding disabled.
     * @param maxInlineMessageLength the maximum length of inline message.
     * @param messagePool the predefined message pool.
     */
    public RedisDecoder(int maxInlineMessageLength, IRedisMessagePool messagePool)
        : this(maxInlineMessageLength, messagePool, false)
    {
    }

    /**
     * Creates a new instance.
     * @param maxInlineMessageLength the maximum length of inline message.
     * @param messagePool the predefined message pool.
     * @param decodeInlineCommands if {@code true}, inline commands will be decoded.
     */
    public RedisDecoder(int maxInlineMessageLength, IRedisMessagePool messagePool, bool decodeInlineCommands)
    {
        if (maxInlineMessageLength <= 0 || maxInlineMessageLength > RedisConstants.REDIS_MESSAGE_MAX_LENGTH)
        {
            throw new RedisCodecException("maxInlineMessageLength: " +
                                          maxInlineMessageLength.ToString(CultureInfo.InvariantCulture) +
                                          " (expected: <= " +
                                          RedisConstants.REDIS_MESSAGE_MAX_LENGTH.ToString(CultureInfo.InvariantCulture) +
                                          ")");
        }
        this.maxInlineMessageLength = maxInlineMessageLength;
        this.messagePool = messagePool;
        this.decodeInlineCommands = decodeInlineCommands;
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        try
        {
            for (;;)
            {
                switch (state)
                {
                case State.DECODE_TYPE:
                    if (!DecodeType(@in))
                    {
                        return;
                    }
                    break;
                case State.DECODE_INLINE:
                    if (!DecodeInline(@in, @out))
                    {
                        return;
                    }
                    break;
                case State.DECODE_LENGTH:
                    if (!DecodeLength(@in, @out))
                    {
                        return;
                    }
                    break;
                case State.DECODE_BULK_STRING_EOL:
                    if (!DecodeBulkStringEndOfLine(@in, @out))
                    {
                        return;
                    }
                    break;
                case State.DECODE_BULK_STRING_CONTENT:
                    if (!DecodeBulkStringContent(@in, @out))
                    {
                        return;
                    }
                    break;
                default:
                    throw new RedisCodecException("Unknown state: " + state);
                }
            }
        }
        catch (RedisCodecException)
        {
            // Let's discard everything
            @in.SkipBytes(@in.ReadableBytes);
            ResetDecoder();
            throw;
        }
        catch (Exception e)
        {
            // Let's discard everything
            @in.SkipBytes(@in.ReadableBytes);
            ResetDecoder();
            throw new RedisCodecException(e);
        }
    }

    private void ResetDecoder()
    {
        state = State.DECODE_TYPE;
        remainingBulkLength = 0;
    }

    private bool DecodeType(ByteBuf @in)
    {
        if (!@in.IsReadable())
        {
            return false;
        }

        type = RedisMessageType.ReadFrom(@in, decodeInlineCommands);
        state = type.IsInline() ? State.DECODE_INLINE : State.DECODE_LENGTH;
        return true;
    }

    private bool DecodeInline(ByteBuf @in, IList<object> @out)
    {
        ByteBuf lineBytes = ReadLine(@in);
        if (lineBytes == null)
        {
            if (@in.ReadableBytes > maxInlineMessageLength)
            {
                throw new RedisCodecException("length: " + @in.ReadableBytes.ToString(CultureInfo.InvariantCulture) +
                                              " (expected: <= " +
                                              maxInlineMessageLength.ToString(CultureInfo.InvariantCulture) + ")");
            }
            return false;
        }
        @out.Add(NewInlineRedisMessage(type, lineBytes));
        ResetDecoder();
        return true;
    }

    private bool DecodeLength(ByteBuf @in, IList<object> @out)
    {
        ByteBuf lineByteBuf = ReadLine(@in);
        if (lineByteBuf == null)
        {
            int readableBytes = @in.ReadableBytes;
            if (readableBytes <= RedisConstants.POSITIVE_LONG_MAX_LENGTH)
            {
                // fast-path
                return false;
            }
            bool isNegative = @in.GetByte(@in.ReaderIndex) == '-';
            int capacity = RedisConstants.POSITIVE_LONG_MAX_LENGTH + (isNegative ? 1 : 0) + 1;
            if (readableBytes > capacity)
            {
                throw new RedisCodecException("too many characters to be a valid RESP Integer: " +
                                              readableBytes.ToString(CultureInfo.InvariantCulture));
            }
            return false;
        }
        long length = ParseRedisNumber(lineByteBuf);
        if (length < RedisConstants.NULL_VALUE)
        {
            throw new RedisCodecException("length: " + length.ToString(CultureInfo.InvariantCulture) +
                                          " (expected: >= " +
                                          RedisConstants.NULL_VALUE.ToString(CultureInfo.InvariantCulture) + ")");
        }
        if (type == RedisMessageType.ARRAY_HEADER)
        {
            @out.Add(new ArrayHeaderRedisMessage(length));
            ResetDecoder();
            return true;
        }
        else if (type == RedisMessageType.BULK_STRING)
        {
            if (length > RedisConstants.REDIS_MESSAGE_MAX_LENGTH)
            {
                throw new RedisCodecException("length: " + length.ToString(CultureInfo.InvariantCulture) +
                                              " (expected: <= " +
                                              RedisConstants.REDIS_MESSAGE_MAX_LENGTH.ToString(CultureInfo.InvariantCulture) +
                                              ")");
            }
            remainingBulkLength = (int) length; // range(int) is already checked.
            return DecodeBulkString(@in, @out);
        }
        else
        {
            throw new RedisCodecException("bad type: " + type);
        }
    }

    private bool DecodeBulkString(ByteBuf @in, IList<object> @out)
    {
        switch (remainingBulkLength)
        {
        case RedisConstants.NULL_VALUE: // $-1\r\n
            @out.Add(FullBulkStringRedisMessage.NULL_INSTANCE);
            ResetDecoder();
            return true;
        case 0:
            state = State.DECODE_BULK_STRING_EOL;
            return DecodeBulkStringEndOfLine(@in, @out);
        default: // expectedBulkLength is always positive.
            @out.Add(new BulkStringHeaderRedisMessage(remainingBulkLength));
            state = State.DECODE_BULK_STRING_CONTENT;
            return DecodeBulkStringContent(@in, @out);
        }
    }

    // $0\r\n <here> \r\n
    private bool DecodeBulkStringEndOfLine(ByteBuf @in, IList<object> @out)
    {
        if (@in.ReadableBytes < RedisConstants.EOL_LENGTH)
        {
            return false;
        }
        ReadEndOfLine(@in);
        @out.Add(FullBulkStringRedisMessage.EMPTY_INSTANCE);
        ResetDecoder();
        return true;
    }

    // ${expectedBulkLength}\r\n <here> {data...}\r\n
    private bool DecodeBulkStringContent(ByteBuf @in, IList<object> @out)
    {
        int readableBytes = @in.ReadableBytes;
        if (readableBytes == 0 || remainingBulkLength == 0 && readableBytes < RedisConstants.EOL_LENGTH)
        {
            return false;
        }

        // if this is last frame.
        if (readableBytes >= remainingBulkLength + RedisConstants.EOL_LENGTH)
        {
            ByteBuf content = @in.ReadSlice(remainingBulkLength);
            ReadEndOfLine(@in);
            // Only call retain after readEndOfLine(...) as the method may throw an exception.
            @out.Add(new DefaultLastBulkStringRedisContent(content.Retain()));
            ResetDecoder();
            return true;
        }

        // chunked write.
        int toRead = Math.Min(remainingBulkLength, readableBytes);
        remainingBulkLength -= toRead;
        @out.Add(new DefaultBulkStringRedisContent(@in.ReadSlice(toRead).Retain()));
        return true;
    }

    // CLR: Java concatenates the signed bytes of the delimiter as -128..127; the System.Byte values of shortToBytes are
    // cast to sbyte to keep that text (JLS 4.2.1: https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1,
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
    private static void ReadEndOfLine(ByteBuf @in)
    {
        short delim = @in.ReadShort();
        if (RedisConstants.EOL_SHORT == delim)
        {
            return;
        }
        byte[] bytes = RedisCodecUtil.ShortToBytes(delim);
        throw new RedisCodecException("delimiter: [" + ((sbyte) bytes[0]).ToString(CultureInfo.InvariantCulture) + "," +
                ((sbyte) bytes[1]).ToString(CultureInfo.InvariantCulture) + "] (expected: \\r\\n)");
    }

    private IRedisMessage NewInlineRedisMessage(RedisMessageType messageType, ByteBuf content)
    {
        if (messageType == RedisMessageType.INLINE_COMMAND)
        {
            return new InlineCommandRedisMessage(content.ToString(Encoding.UTF8));
        }
        else if (messageType == RedisMessageType.SIMPLE_STRING)
        {
            SimpleStringRedisMessage cached = messagePool.GetSimpleString(content);
            return cached != null ? cached : new SimpleStringRedisMessage(content.ToString(Encoding.UTF8));
        }
        else if (messageType == RedisMessageType.ERROR)
        {
            ErrorRedisMessage cached = messagePool.GetError(content);
            return cached != null ? cached : new ErrorRedisMessage(content.ToString(Encoding.UTF8));
        }
        else if (messageType == RedisMessageType.INTEGER)
        {
            IntegerRedisMessage cached = messagePool.GetInteger(content);
            return cached != null ? cached : new IntegerRedisMessage(ParseRedisNumber(content));
        }
        else
        {
            throw new RedisCodecException("bad type: " + messageType);
        }
    }

    private static ByteBuf ReadLine(ByteBuf @in)
    {
        if (!@in.IsReadable(RedisConstants.EOL_LENGTH))
        {
            return null;
        }
        int lfIndex = @in.IndexOf(@in.ReaderIndex, @in.WriterIndex, (byte) '\n');
        if (lfIndex < 0)
        {
            return null;
        }
        ByteBuf data = @in.ReadSlice(lfIndex - @in.ReaderIndex - 1); // `-1` is for CR
        ReadEndOfLine(@in); // validate CR LF
        return data;
    }

    private long ParseRedisNumber(ByteBuf byteBuf)
    {
        int readableBytes = byteBuf.ReadableBytes;
        bool negative = readableBytes > 0 && byteBuf.GetByte(byteBuf.ReaderIndex) == '-';
        int extraOneByteForNegative = negative ? 1 : 0;
        if (readableBytes <= extraOneByteForNegative)
        {
            throw new RedisCodecException("no number to parse: " + byteBuf.ToString(Encoding.ASCII));
        }
        if (readableBytes > RedisConstants.POSITIVE_LONG_MAX_LENGTH + extraOneByteForNegative)
        {
            throw new RedisCodecException("too many characters to be a valid RESP Integer: " +
                                          byteBuf.ToString(Encoding.ASCII));
        }
        if (negative)
        {
            return -ParsePositiveNumber(byteBuf.SkipBytes(extraOneByteForNegative));
        }
        return ParsePositiveNumber(byteBuf);
    }

    private long ParsePositiveNumber(ByteBuf byteBuf)
    {
        toPositiveLongProcessor.Reset();
        byteBuf.ForEachByte(toPositiveLongProcessor.Process);
        return toPositiveLongProcessor.Content();
    }

    // CLR: the class implements ByteProcessor, which is Func<byte, bool> in the Buffer port, through its Process method
    // group. A System.Byte of 0x80..0xFF is above '9' where the signed Java byte is below '0', so both reject it; the
    // message prints the Java signed value through (sbyte)
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
    private sealed class ToPositiveLongProcessor
    {
        private long result;

        public bool Process(byte value)
        {
            if (value < '0' || value > '9')
            {
                throw new RedisCodecException("bad byte in number: " + ((sbyte) value).ToString(CultureInfo.InvariantCulture));
            }
            result = result * 10 + (value - '0');
            return true;
        }

        public long Content()
        {
            return result;
        }

        public void Reset()
        {
            result = 0;
        }
    }
}
