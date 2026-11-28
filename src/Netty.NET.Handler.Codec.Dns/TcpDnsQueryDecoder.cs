/*
 * Copyright 2021 The Netty Project
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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Dns;

public sealed class TcpDnsQueryDecoder : LengthFieldBasedFrameDecoder
{
    private readonly IDnsRecordDecoder decoder;

    /**
     * Creates a new decoder with {@linkplain DnsRecordDecoder#DEFAULT the default record decoder}.
     */
    public TcpDnsQueryDecoder() : this(IDnsRecordDecoder.DEFAULT, 65535)
    {
    }

    /**
     * Creates a new decoder with the specified {@code decoder}.
     */
    public TcpDnsQueryDecoder(IDnsRecordDecoder decoder, int maxFrameLength) : base(maxFrameLength, 0, 2, 0, 2)
    {
        this.decoder = ObjectUtil.CheckNotNull(decoder, "decoder");
    }

    protected override object Decode(IChannelHandlerContext ctx, ByteBuf @in)
    {
        ByteBuf frame = (ByteBuf) base.Decode(ctx, @in);
        if (frame == null)
        {
            return null;
        }

        try
        {
            return DnsMessageUtil.DecodeDnsQuery(decoder, frame.Slice(), new TcpDnsQueryFactory());
        }
        finally
        {
            frame.Release();
        }
    }

    private sealed class TcpDnsQueryFactory : DnsMessageUtil.IDnsQueryFactory
    {
        public IDnsQuery NewQuery(int id, DnsOpCode dnsOpCode)
        {
            return new DefaultDnsQuery(id, dnsOpCode);
        }
    }
}
