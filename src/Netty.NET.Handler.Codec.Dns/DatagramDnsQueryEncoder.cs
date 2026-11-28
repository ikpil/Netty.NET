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
using System.Net;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * Encodes a {@link DatagramDnsQuery} (or an {@link AddressedEnvelope} of {@link DnsQuery}} into a
 * {@link DatagramPacket}.
 */
// CLR: @ChannelHandler.Sharable is [Sharable]; List<Object> out is IList<object> @out and 'in' is @in.
// AddressedEnvelope<DnsQuery, InetSocketAddress> is IAddressedEnvelope<IDnsQuery, IPEndPoint>, with the Content
// and Recipient properties of the Transport port. The message matcher is the reified type argument and
// Type.IsInstanceOfType, which follows the covariant 'out' parameters of IAddressedEnvelope, so a DatagramDnsQuery
// (an IAddressedEnvelope<DatagramDnsQuery, IPEndPoint>) matches; Java's matcher compares the erased
// AddressedEnvelope class. 'throws Exception' has no C# counterpart.
[Sharable]
public class DatagramDnsQueryEncoder : MessageToMessageEncoder<IAddressedEnvelope<IDnsQuery, IPEndPoint>>
{

    private readonly DnsQueryEncoder encoder;

    /**
     * Creates a new encoder with {@linkplain DnsRecordEncoder#DEFAULT the default record encoder}.
     */
    public DatagramDnsQueryEncoder() : this(IDnsRecordEncoder.DEFAULT)
    {
    }

    /**
     * Creates a new encoder with the specified {@code recordEncoder}.
     */
    public DatagramDnsQueryEncoder(IDnsRecordEncoder recordEncoder)
    {
        this.encoder = new DnsQueryEncoder(recordEncoder);
    }

    protected override void Encode(
        IChannelHandlerContext ctx,
        IAddressedEnvelope<IDnsQuery, IPEndPoint> @in, IList<object> @out)
    {

        IPEndPoint recipient = @in.Recipient;
        IDnsQuery query = @in.Content;
        ByteBuf buf = AllocateBuffer(ctx, @in);

        bool success = false;

        try
        {
            encoder.Encode(query, buf);
            success = true;
        }
        finally
        {
            if (!success)
            {
                buf.Release();
            }
        }

        @out.Add(new DatagramPacket(buf, recipient, null));
    }

    /**
     * Allocate a {@link ByteBuf} which will be used for constructing a datagram packet.
     * Sub-classes may override this method to return a {@link ByteBuf} with a perfect matching initial capacity.
     */
    protected virtual ByteBuf AllocateBuffer(
        IChannelHandlerContext ctx,
        IAddressedEnvelope<IDnsQuery, IPEndPoint> msg)
    {
        return ctx.Alloc().IoBuffer(1024);
    }
}
