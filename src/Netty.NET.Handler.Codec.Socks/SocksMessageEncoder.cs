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

using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socks;

/**
 * Encodes an {@link SocksMessage} into a {@link ByteBuf}.
 * {@link MessageToByteEncoder} implementation.
 * Use this with {@link SocksInitRequest}, {@link SocksInitResponse}, {@link SocksAuthRequest},
 * {@link SocksAuthResponse}, {@link SocksCmdRequest} and {@link SocksCmdResponse}
 */
// CLR: the Class argument is a Type (MessageToByteEncoder in codec-base); @ChannelHandler.Sharable is [Sharable].
[Sharable]
public class SocksMessageEncoder : MessageToByteEncoder<SocksMessage>
{
    public SocksMessageEncoder() : base(typeof(SocksMessage))
    {
    }

    // CLR: @SuppressWarnings("deprecation") is a CS0618 suppression around the call of the [Obsolete] EncodeAsByteBuf.
    protected override void Encode(IChannelHandlerContext ctx, SocksMessage msg, ByteBuf @out)
    {
#pragma warning disable CS0618
        msg.EncodeAsByteBuf(@out);
#pragma warning restore CS0618
    }
}
