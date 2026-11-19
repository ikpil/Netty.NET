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

using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Memcache;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Memcache.Binary;

/**
 * A {@link MessageToByteEncoder} that encodes binary memcache messages into bytes.
 */
[UnstableApi]
public abstract class AbstractBinaryMemcacheEncoder<M>
    : AbstractMemcacheObjectEncoder<M>
    where M : IBinaryMemcacheMessage
{

    /**
     * Every binary memcache message has at least a 24 bytes header.
     */
    private const int MINIMUM_HEADER_SIZE = 24;

    protected override ByteBuf EncodeMessage(IChannelHandlerContext ctx, M msg)
    {
        // CLR: Java sign-extends the signed byte extrasLength() in this sum (JLS 5.6, 5.1.2:
        // https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.6), so an extras length of 128..255 lowers
        // the initial capacity; (sbyte) keeps that value for the System.Byte length
        // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
        ByteBuf buf = ctx.Alloc().Buffer(MINIMUM_HEADER_SIZE + (sbyte) msg.ExtrasLength()
            + msg.KeyLength());

        EncodeHeader(buf, msg);
        EncodeExtras(buf, msg.Extras());
        EncodeKey(buf, msg.Key());

        return buf;
    }

    /**
     * Encode the extras.
     *
     * @param buf    the {@link ByteBuf} to write into.
     * @param extras the extras to encode.
     */
    private static void EncodeExtras(ByteBuf buf, ByteBuf extras)
    {
        if (extras == null || !extras.IsReadable())
        {
            return;
        }

        buf.WriteBytes(extras);
    }

    /**
     * Encode the key.
     *
     * @param buf the {@link ByteBuf} to write into.
     * @param key the key to encode.
     */
    private static void EncodeKey(ByteBuf buf, ByteBuf key)
    {
        if (key == null || !key.IsReadable())
        {
            return;
        }

        buf.WriteBytes(key);
    }

    /**
     * Encode the header.
     * <p/>
     * This methods needs to be implemented by a sub class because the header is different
     * for both requests and responses.
     *
     * @param buf the {@link ByteBuf} to write into.
     * @param msg the message to encode.
     */
    protected abstract void EncodeHeader(ByteBuf buf, M msg);

}
