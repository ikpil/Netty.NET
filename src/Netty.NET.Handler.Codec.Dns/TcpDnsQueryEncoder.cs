/*
 * Copyright 2019 The Netty Project
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

namespace Netty.NET.Handler.Codec.Dns;

// CLR: @ChannelHandler.Sharable is [Sharable]; Class<?> is Type; 'out' is @out.
[Sharable]
public sealed class TcpDnsQueryEncoder : MessageToByteEncoder<IDnsQuery>
{

    private readonly DnsQueryEncoder encoder;

    /**
     * Creates a new encoder with {@linkplain DnsRecordEncoder#DEFAULT the default record encoder}.
     */
    public TcpDnsQueryEncoder() : this(IDnsRecordEncoder.DEFAULT)
    {
    }

    /**
     * Creates a new encoder with the specified {@code recordEncoder}.
     */
    public TcpDnsQueryEncoder(IDnsRecordEncoder recordEncoder) : base(typeof(IDnsQuery))
    {
        this.encoder = new DnsQueryEncoder(recordEncoder);
    }

    protected override void Encode(IChannelHandlerContext ctx, IDnsQuery msg, ByteBuf @out)
    {
        // Length is two octets as defined by RFC-7766
        // See https://tools.ietf.org/html/rfc7766#section-8
        @out.WriterIndex = @out.WriterIndex + 2;
        encoder.Encode(msg, @out);

        // Now fill in the correct length based on the amount of data that we wrote the ByteBuf.
        @out.SetShort(0, @out.ReadableBytes - 2);
    }

    protected override ByteBuf AllocateBuffer(IChannelHandlerContext ctx, IDnsQuery msg,
                                     bool preferDirect)
    {
        if (preferDirect)
        {
            return ctx.Alloc().IoBuffer(1024);
        }
        else
        {
            return ctx.Alloc().HeapBuffer(1024);
        }
    }
}
