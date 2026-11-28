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

using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Dns;

// CLR: @ChannelHandler.Sharable is [Sharable]; Class<?> is Type; List<Object> out is IList<object> @out;
// checkNotNull throws ArgumentNullException. 'throws Exception' has no C# counterpart.
[Sharable]
public sealed class TcpDnsResponseEncoder : MessageToMessageEncoder<IDnsResponse>
{
    private readonly IDnsRecordEncoder encoder;

    /**
     * Creates a new encoder with {@linkplain DnsRecordEncoder#DEFAULT the default record encoder}.
     */
    public TcpDnsResponseEncoder() : this(IDnsRecordEncoder.DEFAULT)
    {
    }

    /**
     * Creates a new encoder with the specified {@code encoder}.
     */
    public TcpDnsResponseEncoder(IDnsRecordEncoder encoder) : base(typeof(IDnsResponse))
    {
        this.encoder = ObjectUtil.CheckNotNull(encoder, "encoder");
    }

    protected override void Encode(IChannelHandlerContext ctx, IDnsResponse response, IList<object> @out)
    {
        ByteBuf buf = ctx.Alloc().IoBuffer(1024);

        buf.WriterIndex = buf.WriterIndex + 2;
        DnsMessageUtil.EncodeDnsResponse(encoder, response, buf);
        buf.SetShort(0, buf.ReadableBytes - 2);

        @out.Add(buf);
    }
}
