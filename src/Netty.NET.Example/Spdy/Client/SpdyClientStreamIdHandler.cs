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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Spdy;
using Netty.NET.Transport.Channels;
using Names = Netty.NET.Handler.Codec.Spdy.SpdyHttpHeaders.Names;

namespace Netty.NET.Example.Spdy.Client;

/**
 * Adds a unique client stream ID to the SPDY header. Client stream IDs MUST be odd.
 */
// CLR: write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken), whose returned Task replaces the
// ChannelPromise (docs/codec-base-porting.md, Outbound operations). HttpMessage is IHttpMessage, and the nested
// SpdyHttpHeaders.Names is named by a using alias, as the Java import names it.
public class SpdyClientStreamIdHandler : ChannelOutboundHandlerAdapter
{
    private int currentStreamId = 1;

    public bool AcceptOutboundMessage(object msg)
    {
        return msg is IHttpMessage;
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (AcceptOutboundMessage(msg))
        {
            IHttpMessage httpMsg = (IHttpMessage) msg;
            if (!httpMsg.Headers().Contains(SpdyHttpHeaders.Names.STREAM_ID))
            {
                httpMsg.Headers().SetInt(Names.STREAM_ID, currentStreamId);
                // Client stream IDs are always odd
                currentStreamId += 2;
            }
        }
        return ctx.WriteAsync(msg, cancellationToken);
    }
}
