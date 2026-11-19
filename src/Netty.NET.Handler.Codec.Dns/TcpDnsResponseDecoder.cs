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

using System.Net;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Dns;

// CLR: the anonymous DnsResponseDecoder<SocketAddress> subclass is the named nested class TcpResponseDecoder over
// EndPoint, the type of IChannel.RemoteAddress/LocalAddress (ctx.channel().remoteAddress() is
// ctx.Channel.RemoteAddress); decode(ctx, in) and extractFrame override the codec-base LengthFieldBasedFrameDecoder
// members, and 'in' is @in. 'throws Exception' has no C# counterpart.
public sealed class TcpDnsResponseDecoder : LengthFieldBasedFrameDecoder
{

    private readonly DnsResponseDecoder<EndPoint> responseDecoder;

    /**
     * Creates a new decoder with {@linkplain DnsRecordDecoder#DEFAULT the default record decoder}.
     */
    public TcpDnsResponseDecoder() : this(IDnsRecordDecoder.DEFAULT, 64 * 1024)
    {
    }

    /**
     * Creates a new decoder with the specified {@code recordDecoder} and {@code maxFrameLength}
     */
    public TcpDnsResponseDecoder(IDnsRecordDecoder recordDecoder, int maxFrameLength) :
        // Length is two octets as defined by RFC-7766
        // See https://tools.ietf.org/html/rfc7766#section-8
        base(maxFrameLength, 0, 2, 0, 2)
    {

        this.responseDecoder = new TcpResponseDecoder(recordDecoder);
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
            return responseDecoder.Decode(ctx.Channel.RemoteAddress, ctx.Channel.LocalAddress, frame.Slice());
        }
        finally
        {
            frame.Release();
        }
    }

    protected override ByteBuf ExtractFrame(IChannelHandlerContext ctx, ByteBuf buffer, int index, int length)
    {
        return buffer.Copy(index, length);
    }

    private sealed class TcpResponseDecoder : DnsResponseDecoder<EndPoint>
    {
        internal TcpResponseDecoder(IDnsRecordDecoder recordDecoder) : base(recordDecoder)
        {
        }

        protected override IDnsResponse NewResponse(EndPoint sender, EndPoint recipient,
                                                    int id, DnsOpCode opCode, DnsResponseCode responseCode)
        {
            return new DefaultDnsResponse(id, opCode, responseCode);
        }
    }
}
