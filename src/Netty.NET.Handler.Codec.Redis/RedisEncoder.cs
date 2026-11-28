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
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * Encodes {@link RedisMessage} into bytes following
 * <a href="https://redis.io/topics/protocol">RESP (REdis Serialization Protocol)</a>.
 */
// CLR: RedisMessage.class is typeof(IRedisMessage) and ByteBufAllocator is IByteBufAllocator (docs/codec-base-porting.md);
// content() of a holder is the Content property (docs/buffer-porting.md, Buffer holders and conversion).
[UnstableApi]
public class RedisEncoder : MessageToMessageEncoder<IRedisMessage>
{

    private readonly IRedisMessagePool messagePool;

    /**
     * Creates a new instance with default {@code messagePool}.
     */
    public RedisEncoder() : this(FixedRedisMessagePool.INSTANCE)
    {
    }

    /**
     * Creates a new instance.
     * @param messagePool the predefined message pool.
     */
    public RedisEncoder(IRedisMessagePool messagePool) : base(typeof(IRedisMessage))
    {
        this.messagePool = ObjectUtil.CheckNotNull(messagePool, "messagePool");
    }

    protected override void Encode(IChannelHandlerContext ctx, IRedisMessage msg, IList<object> @out)
    {
        try
        {
            WriteRedisMessage(ctx.Alloc(), msg, @out);
        }
        catch (CodecException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new CodecException(e);
        }
    }

    private void WriteRedisMessage(IByteBufAllocator allocator, IRedisMessage msg, IList<object> @out)
    {
        if (msg is InlineCommandRedisMessage)
        {
            WriteInlineCommandMessage(allocator, (InlineCommandRedisMessage) msg, @out);
        }
        else if (msg is SimpleStringRedisMessage)
        {
            WriteSimpleStringMessage(allocator, (SimpleStringRedisMessage) msg, @out);
        }
        else if (msg is ErrorRedisMessage)
        {
            WriteErrorMessage(allocator, (ErrorRedisMessage) msg, @out);
        }
        else if (msg is IntegerRedisMessage)
        {
            WriteIntegerMessage(allocator, (IntegerRedisMessage) msg, @out);
        }
        else if (msg is FullBulkStringRedisMessage)
        {
            WriteFullBulkStringMessage(allocator, (FullBulkStringRedisMessage) msg, @out);
        }
        else if (msg is IBulkStringRedisContent)
        {
            WriteBulkStringContent(allocator, (IBulkStringRedisContent) msg, @out);
        }
        else if (msg is BulkStringHeaderRedisMessage)
        {
            WriteBulkStringHeader(allocator, (BulkStringHeaderRedisMessage) msg, @out);
        }
        else if (msg is ArrayHeaderRedisMessage)
        {
            WriteArrayHeader(allocator, (ArrayHeaderRedisMessage) msg, @out);
        }
        else if (msg is ArrayRedisMessage)
        {
            WriteArrayMessage(allocator, (ArrayRedisMessage) msg, @out);
        }
        else
        {
            throw new CodecException("unknown message type: " + msg);
        }
    }

    private static void WriteInlineCommandMessage(IByteBufAllocator allocator, InlineCommandRedisMessage msg,
                                                 IList<object> @out)
    {
        WriteString(allocator, RedisMessageType.INLINE_COMMAND, msg.Content(), @out);
    }

    private static void WriteSimpleStringMessage(IByteBufAllocator allocator, SimpleStringRedisMessage msg,
                                                 IList<object> @out)
    {
        WriteString(allocator, RedisMessageType.SIMPLE_STRING, msg.Content(), @out);
    }

    private static void WriteErrorMessage(IByteBufAllocator allocator, ErrorRedisMessage msg, IList<object> @out)
    {
        WriteString(allocator, RedisMessageType.ERROR, msg.Content(), @out);
    }

    private static void WriteString(IByteBufAllocator allocator, RedisMessageType type, string content,
                                    IList<object> @out)
    {
        if (type.IsInline())
        {
            // Inline, or "simple" messages do not permit CRLF bytes in their contents.
            if (content.IndexOf('\r') != -1 || content.IndexOf('\n') != -1)
            {
                throw new CodecException("Line breaks are not permitted in 'simple' messages");
            }
        }

        ByteBuf buf = allocator.IoBuffer(type.Length() + ByteBufUtil.Utf8MaxBytes(content) +
                                         RedisConstants.EOL_LENGTH);
        type.WriteTo(buf);
        ByteBufUtil.WriteUtf8(buf, content);
        buf.WriteShort(RedisConstants.EOL_SHORT);
        @out.Add(buf);
    }

    private void WriteIntegerMessage(IByteBufAllocator allocator, IntegerRedisMessage msg, IList<object> @out)
    {
        ByteBuf buf = allocator.IoBuffer(RedisConstants.TYPE_LENGTH + RedisConstants.LONG_MAX_LENGTH +
                                         RedisConstants.EOL_LENGTH);
        RedisMessageType.INTEGER.WriteTo(buf);
        buf.WriteBytes(NumberToBytes(msg.Value()));
        buf.WriteShort(RedisConstants.EOL_SHORT);
        @out.Add(buf);
    }

    private void WriteBulkStringHeader(IByteBufAllocator allocator, BulkStringHeaderRedisMessage msg, IList<object> @out)
    {
        ByteBuf buf = allocator.IoBuffer(RedisConstants.TYPE_LENGTH +
                                        (msg.IsNull() ? RedisConstants.NULL_LENGTH :
                                                        RedisConstants.LONG_MAX_LENGTH + RedisConstants.EOL_LENGTH));
        RedisMessageType.BULK_STRING.WriteTo(buf);

        if (msg.IsNull())
        {
            buf.WriteShort(RedisConstants.NULL_SHORT);
        }
        else
        {
            buf.WriteBytes(NumberToBytes(msg.BulkStringLength()));
            buf.WriteShort(RedisConstants.EOL_SHORT);
        }

        @out.Add(buf);
    }

    private static void WriteBulkStringContent(IByteBufAllocator allocator, IBulkStringRedisContent msg,
                                               IList<object> @out)
    {
        @out.Add(msg.Content.Retain());

        if (msg is ILastBulkStringRedisContent)
        {
            @out.Add(allocator.IoBuffer(RedisConstants.EOL_LENGTH).WriteShort(RedisConstants.EOL_SHORT));
        }
    }

    private void WriteFullBulkStringMessage(IByteBufAllocator allocator, FullBulkStringRedisMessage msg,
                                            IList<object> @out)
    {
        if (msg.IsNull())
        {
            ByteBuf buf = allocator.IoBuffer(RedisConstants.TYPE_LENGTH + RedisConstants.NULL_LENGTH +
                                             RedisConstants.EOL_LENGTH);
            RedisMessageType.BULK_STRING.WriteTo(buf);
            buf.WriteShort(RedisConstants.NULL_SHORT);
            buf.WriteShort(RedisConstants.EOL_SHORT);
            @out.Add(buf);
        }
        else
        {
            ByteBuf headerBuf = allocator.IoBuffer(RedisConstants.TYPE_LENGTH + RedisConstants.LONG_MAX_LENGTH +
                                                   RedisConstants.EOL_LENGTH);
            RedisMessageType.BULK_STRING.WriteTo(headerBuf);
            headerBuf.WriteBytes(NumberToBytes(msg.Content.ReadableBytes));
            headerBuf.WriteShort(RedisConstants.EOL_SHORT);
            @out.Add(headerBuf);
            @out.Add(msg.Content.Retain());
            @out.Add(allocator.IoBuffer(RedisConstants.EOL_LENGTH).WriteShort(RedisConstants.EOL_SHORT));
        }
    }

    /**
     * Write array header only without body. Use this if you want to write arrays as streaming.
     */
    private void WriteArrayHeader(IByteBufAllocator allocator, ArrayHeaderRedisMessage msg, IList<object> @out)
    {
        WriteArrayHeader(allocator, msg.IsNull(), msg.Length(), @out);
    }

    /**
     * Write full constructed array message.
     */
    private void WriteArrayMessage(IByteBufAllocator allocator, ArrayRedisMessage msg, IList<object> @out)
    {
        if (msg.IsNull())
        {
            WriteArrayHeader(allocator, msg.IsNull(), RedisConstants.NULL_VALUE, @out);
        }
        else
        {
            WriteArrayHeader(allocator, msg.IsNull(), msg.Children().Count, @out);

            foreach (IRedisMessage child in msg.Children())
            {
                WriteRedisMessage(allocator, child, @out);
            }
        }
    }

    private void WriteArrayHeader(IByteBufAllocator allocator, bool isNull, long length, IList<object> @out)
    {
        if (isNull)
        {
            ByteBuf buf = allocator.IoBuffer(RedisConstants.TYPE_LENGTH + RedisConstants.NULL_LENGTH +
                                                   RedisConstants.EOL_LENGTH);
            RedisMessageType.ARRAY_HEADER.WriteTo(buf);
            buf.WriteShort(RedisConstants.NULL_SHORT);
            buf.WriteShort(RedisConstants.EOL_SHORT);
            @out.Add(buf);
        }
        else
        {
            ByteBuf buf = allocator.IoBuffer(RedisConstants.TYPE_LENGTH + RedisConstants.LONG_MAX_LENGTH +
                                                   RedisConstants.EOL_LENGTH);
            RedisMessageType.ARRAY_HEADER.WriteTo(buf);
            buf.WriteBytes(NumberToBytes(length));
            buf.WriteShort(RedisConstants.EOL_SHORT);
            @out.Add(buf);
        }
    }

    private byte[] NumberToBytes(long value)
    {
        byte[] bytes = messagePool.GetByteBufOfInteger(value);
        return bytes != null ? bytes : RedisCodecUtil.LongToAsciiBytes(value);
    }
}
