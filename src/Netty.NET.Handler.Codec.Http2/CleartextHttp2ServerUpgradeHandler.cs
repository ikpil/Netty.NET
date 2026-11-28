/*
 * Copyright 2017 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Performing cleartext upgrade, by h2c HTTP upgrade or Prior Knowledge.
 * This handler config pipeline for h2c upgrade when handler added.
 * And will update pipeline once it detect the connection is starting HTTP/2 by
 * prior knowledge or not.
 */
// CLR: ChannelHandler is IChannelHandler, List<Object> out is IList<object> @out and checkNotNull's
// NullPointerException is ArgumentNullException; ctx.pipeline() and ctx.name() are the Pipeline and Name
// properties, and the pipeline's remove(handler) returns the pipeline as in Java.
public sealed class CleartextHttp2ServerUpgradeHandler : ByteToMessageDecoder
{
    private static readonly ByteBuf CONNECTION_PREFACE = UnreleasableBuffer(ConnectionPrefaceBuf()).AsReadOnly();

    private readonly HttpServerCodec httpServerCodec;
    private readonly HttpServerUpgradeHandler httpServerUpgradeHandler;
    private readonly IChannelHandler http2ServerHandler;

    /**
     * Creates the channel handler provide cleartext HTTP/2 upgrade from HTTP
     * upgrade or prior knowledge
     *
     * @param httpServerCodec the http server codec
     * @param httpServerUpgradeHandler the http server upgrade handler for HTTP/2
     * @param http2ServerHandler the http2 server handler, will be added into pipeline
     *                           when starting HTTP/2 by prior knowledge
     */
    public CleartextHttp2ServerUpgradeHandler(HttpServerCodec httpServerCodec,
                                              HttpServerUpgradeHandler httpServerUpgradeHandler,
                                              IChannelHandler http2ServerHandler)
    {
        this.httpServerCodec = CheckNotNull(httpServerCodec, "httpServerCodec");
        this.httpServerUpgradeHandler = CheckNotNull(httpServerUpgradeHandler, "httpServerUpgradeHandler");
        this.http2ServerHandler = CheckNotNull(http2ServerHandler, "http2ServerHandler");
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        ctx.Pipeline
                .AddAfter(ctx.Name, null, httpServerUpgradeHandler)
                .AddAfter(ctx.Name, null, httpServerCodec);
    }

    /**
     * Peek inbound message to determine current connection wants to start HTTP/2
     * by HTTP upgrade or prior knowledge
     */
    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        int prefaceLength = CONNECTION_PREFACE.ReadableBytes;
        int bytesRead = Math.Min(@in.ReadableBytes, prefaceLength);

        if (!ByteBufUtil.Equals(CONNECTION_PREFACE, CONNECTION_PREFACE.ReaderIndex,
                @in, @in.ReaderIndex, bytesRead))
        {
            ctx.Pipeline.Remove(this);
        }
        else if (bytesRead == prefaceLength)
        {
            // Full h2 preface match, removed source codec, using http2 codec to handle
            // following network traffic
            ctx.Pipeline
                    .Remove(httpServerCodec)
                    .Remove(httpServerUpgradeHandler);

            ctx.Pipeline.AddAfter(ctx.Name, null, http2ServerHandler);
            ctx.Pipeline.Remove(this);

            ctx.FireUserEventTriggered(PriorKnowledgeUpgradeEvent.INSTANCE);
        }
    }

    /**
     * User event that is fired to notify about HTTP/2 protocol is started.
     */
    public sealed class PriorKnowledgeUpgradeEvent
    {
        // CLR: private in Java, where the enclosing class may read a nested class's private members; internal
        // here, because C# does not open a nested class's private members to the enclosing class.
        internal static readonly PriorKnowledgeUpgradeEvent INSTANCE = new PriorKnowledgeUpgradeEvent();

        private PriorKnowledgeUpgradeEvent()
        {
        }
    }
}
