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

using System;
using System.Collections.Generic;
using System.Net;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * Decodes a {@link DatagramPacket} into a {@link DatagramDnsResponse}.
 */
// CLR: @ChannelHandler.Sharable is [Sharable]; Class<?> is Type; List<Object> out is IList<object> @out;
// packet.content()/sender()/recipient() are the DatagramPacket properties Content/Sender/Recipient of the
// Transport port. ByteBuf reads past the readable bytes throw ArgumentOutOfRangeException, the Buffer port's
// replacement of IndexOutOfBoundsException ("ArgumentOutOfRangeException follows the existing CLR
// argument-validation policy in place of IndexOutOfBoundsException", docs/buffer-porting.md), so that is the
// exception wrapped into CorruptedFrameException. 'throws Exception' has no C# counterpart.
[Sharable]
public class DatagramDnsResponseDecoder : MessageToMessageDecoder<DatagramPacket>
{

    private readonly DnsResponseDecoder<IPEndPoint> responseDecoder;

    /**
     * Creates a new decoder with {@linkplain DnsRecordDecoder#DEFAULT the default record decoder}.
     */
    public DatagramDnsResponseDecoder() : this(IDnsRecordDecoder.DEFAULT)
    {
    }

    /**
     * Creates a new decoder with the specified {@code recordDecoder}.
     */
    public DatagramDnsResponseDecoder(IDnsRecordDecoder recordDecoder) : base(typeof(DatagramPacket))
    {
        this.responseDecoder = new DatagramResponseDecoder(recordDecoder);
    }

    protected override void Decode(IChannelHandlerContext ctx, DatagramPacket packet, IList<object> @out)
    {
        try
        {
            @out.Add(DecodeResponse(ctx, packet));
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new CorruptedFrameException("Unable to decode response", e);
        }
    }

    protected virtual IDnsResponse DecodeResponse(IChannelHandlerContext ctx, DatagramPacket packet)
    {
        return responseDecoder.Decode(packet.Sender, packet.Recipient, packet.Content);
    }

    private sealed class DatagramResponseDecoder : DnsResponseDecoder<IPEndPoint>
    {
        internal DatagramResponseDecoder(IDnsRecordDecoder recordDecoder) : base(recordDecoder)
        {
        }

        protected override IDnsResponse NewResponse(IPEndPoint sender, IPEndPoint recipient,
                                                    int id, DnsOpCode opCode, DnsResponseCode responseCode)
        {
            return new DatagramDnsResponse(sender, recipient, id, opCode, responseCode);
        }
    }
}
