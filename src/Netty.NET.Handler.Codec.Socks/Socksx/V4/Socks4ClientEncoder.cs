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
 * Encodes a {@link Socks4CommandRequest} into a {@link ByteBuf}.
 */
// CLR: the Class argument is a Type (MessageToByteEncoder in codec-base) and @Sharable is [Sharable]. sanitize
// returns its String argument typed as CharSequence; here it is the string, which ByteBufUtil.WriteAscii takes as a
// character span.
[Sharable]
public sealed class Socks4ClientEncoder : MessageToByteEncoder<ISocks4CommandRequest>
{

    /**
     * The singleton instance of {@link Socks4ClientEncoder}
     */
    public static readonly Socks4ClientEncoder INSTANCE = new Socks4ClientEncoder();

    private static readonly byte[] IPv4_DOMAIN_MARKER = { 0x00, 0x00, 0x00, 0x01 };

    private Socks4ClientEncoder() : base(typeof(ISocks4CommandRequest))
    {
    }

    protected override void Encode(IChannelHandlerContext ctx, ISocks4CommandRequest msg, ByteBuf @out)
    {
        @out.WriteByte(msg.Version().ByteValue());
        @out.WriteByte(msg.Type().ByteValue());
        ByteBufUtil.WriteShortBE(@out, msg.DstPort());
        if (NetUtil.IsValidIpV4Address(msg.DstAddr()))
        {
            @out.WriteBytes(NetUtil.CreateByteArrayFromIpAddressString(msg.DstAddr()));
            ByteBufUtil.WriteAscii(@out, Sanitize("userId", msg.UserId()));
            @out.WriteByte(0);
        }
        else
        {
            @out.WriteBytes(IPv4_DOMAIN_MARKER);
            ByteBufUtil.WriteAscii(@out, Sanitize("userId", msg.UserId()));
            @out.WriteByte(0);
            ByteBufUtil.WriteAscii(@out, Sanitize("dstAddr", msg.DstAddr()));
            @out.WriteByte(0);
        }
    }

    private string Sanitize(string fieldName, string strValue)
    {
        for (int i = 0, len = strValue.Length; i < len; i++)
        {
            char c = strValue[i];
            // SOCKS4 uses NUL-bytes as field delimiters.
            if (AsciiString.C2b(c) == 0)
            {
                throw new EncoderException("Illegal character in " + fieldName + " field.");
            }
        }
        return strValue;
    }
}
