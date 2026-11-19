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

using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socksx.V4;

/**
 * Encodes a {@link Socks4CommandResponse} into a {@link ByteBuf}.
 */
// CLR: the Class argument is a Type (MessageToByteEncoder in codec-base) and @Sharable is [Sharable].
[Sharable]
public sealed class Socks4ServerEncoder : MessageToByteEncoder<ISocks4CommandResponse>
{

    public static readonly Socks4ServerEncoder INSTANCE = new Socks4ServerEncoder();

    private static readonly byte[] IPv4_HOSTNAME_ZEROED = { 0x00, 0x00, 0x00, 0x00 };

    private Socks4ServerEncoder() : base(typeof(ISocks4CommandResponse))
    {
    }

    protected override void Encode(IChannelHandlerContext ctx, ISocks4CommandResponse msg, ByteBuf @out)
    {
        @out.WriteByte(0);
        @out.WriteByte(msg.Status().ByteValue());
        ByteBufUtil.WriteShortBE(@out, msg.DstPort());
        @out.WriteBytes(msg.DstAddr() == null? IPv4_HOSTNAME_ZEROED
                                            : NetUtil.CreateByteArrayFromIpAddressString(msg.DstAddr()));
    }
}
