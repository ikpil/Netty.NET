/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * Decodes a {@link DatagramPacket} into a {@link DatagramDnsQuery}.
 */
// CLR: @ChannelHandler.Sharable is [Sharable]; Class<?> is Type; List<Object> out is IList<object> @out; the
// anonymous DnsMessageUtil.DnsQueryFactory is the named nested class DatagramDnsQueryFactory capturing the packet;
// packet.content()/sender()/recipient() are the DatagramPacket properties Content/Sender/Recipient of the Transport
// port (docs/codec-base-porting.md, Shared CLR adaptations; docs/transport-porting.md).
[Sharable]
public class DatagramDnsQueryDecoder : MessageToMessageDecoder<DatagramPacket>
{

    private readonly IDnsRecordDecoder recordDecoder;

    /**
     * Creates a new decoder with {@linkplain DnsRecordDecoder#DEFAULT the default record decoder}.
     */
    public DatagramDnsQueryDecoder() : this(IDnsRecordDecoder.DEFAULT)
    {
    }

    /**
     * Creates a new decoder with the specified {@code recordDecoder}.
     */
    public DatagramDnsQueryDecoder(IDnsRecordDecoder recordDecoder) : base(typeof(DatagramPacket))
    {
        this.recordDecoder = CheckNotNull(recordDecoder, "recordDecoder");
    }

    protected override void Decode(IChannelHandlerContext ctx, DatagramPacket packet, IList<object> @out)
    {
        IDnsQuery query = DnsMessageUtil.DecodeDnsQuery(recordDecoder, packet.Content,
                new DatagramDnsQueryFactory(packet));
        @out.Add(query);
    }

    private sealed class DatagramDnsQueryFactory : DnsMessageUtil.IDnsQueryFactory
    {
        private readonly DatagramPacket packet;

        internal DatagramDnsQueryFactory(DatagramPacket packet)
        {
            this.packet = packet;
        }

        public IDnsQuery NewQuery(int id, DnsOpCode dnsOpCode)
        {
            return new DatagramDnsQuery(packet.Sender, packet.Recipient, id, dnsOpCode);
        }
    }
}
