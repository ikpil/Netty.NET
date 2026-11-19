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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * Encodes a {@link DatagramDnsResponse} (or an {@link AddressedEnvelope} of {@link DnsResponse}} into a
 * {@link DatagramPacket}.
 */
// CLR: @ChannelHandler.Sharable is [Sharable]; List<Object> out is IList<object> @out and 'in' is @in.
// AddressedEnvelope<DnsResponse, InetSocketAddress> is IAddressedEnvelope<IDnsResponse, IPEndPoint>, with the Content
// and Recipient properties of the Transport port. The message matcher is the reified type argument and
// Type.IsInstanceOfType, which follows the covariant 'out' parameters of IAddressedEnvelope, so a DatagramDnsResponse
// matches (docs/codec-base-porting.md, "Type matching";
// https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance); Java's matcher compares the
// erased AddressedEnvelope class. checkNotNull throws ArgumentNullException. 'throws Exception' has no C# counterpart.
[Sharable]
public class DatagramDnsResponseEncoder
    : MessageToMessageEncoder<IAddressedEnvelope<IDnsResponse, IPEndPoint>>
{

    private readonly IDnsRecordEncoder recordEncoder;

    /**
     * Creates a new encoder with {@linkplain DnsRecordEncoder#DEFAULT the default record encoder}.
     */
    public DatagramDnsResponseEncoder() : this(IDnsRecordEncoder.DEFAULT)
    {
    }

    /**
     * Creates a new encoder with the specified {@code recordEncoder}.
     */
    public DatagramDnsResponseEncoder(IDnsRecordEncoder recordEncoder)
    {
        this.recordEncoder = CheckNotNull(recordEncoder, "recordEncoder");
    }

    protected override void Encode(IChannelHandlerContext ctx,
                          IAddressedEnvelope<IDnsResponse, IPEndPoint> @in, IList<object> @out)
    {

        IPEndPoint recipient = @in.Recipient;
        IDnsResponse response = @in.Content;
        ByteBuf buf = AllocateBuffer(ctx, @in);

        DnsMessageUtil.EncodeDnsResponse(recordEncoder, response, buf);

        @out.Add(new DatagramPacket(buf, recipient, null));
    }

    /**
     * Allocate a {@link ByteBuf} which will be used for constructing a datagram packet.
     * Sub-classes may override this method to return a {@link ByteBuf} with a perfect matching initial capacity.
     */
    protected virtual ByteBuf AllocateBuffer(
        IChannelHandlerContext ctx,
        IAddressedEnvelope<IDnsResponse, IPEndPoint> msg)
    {
        return ctx.Alloc().IoBuffer(1024);
    }
}
