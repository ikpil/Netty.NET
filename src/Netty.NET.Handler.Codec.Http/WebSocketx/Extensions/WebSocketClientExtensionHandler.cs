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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;

/**
 * This handler negotiates and initializes the WebSocket Extensions.
 *
 * This implementation negotiates the extension with the server in a defined order,
 * ensures that the successfully negotiated extensions are consistent between them,
 * and initializes the channel pipeline with the extension decoder and encoder.
 *
 * Find a basic implementation for compression extensions at
 * <tt>io.netty.handler.codec.http.websocketx.extensions.compression.WebSocketClientCompressionHandler</tt>.
 */
// CLR: write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken), whose Task replaces the promise
// (docs/codec-base-porting.md, Outbound operations). The varargs constructor takes a params array, and
// Arrays.asList(checkNonEmpty(...)) is the array itself as an IList (ArgumentException for an empty array, as
// checkNonEmpty's IllegalArgumentException). HttpRequest/HttpResponse are the core IHttpRequest/IHttpResponse, the
// handler names decoder.getClass().getName() are Type.FullName, and ctx.name()/ctx.pipeline() are the Name and Pipeline
// properties. Iterator over the handshakers is IEnumerator (hasNext()/next() are MoveNext()/Current).
public class WebSocketClientExtensionHandler : ChannelDuplexHandler
{

    private readonly IList<IWebSocketClientExtensionHandshaker> extensionHandshakers;

    /**
     * Constructor
     *
     * @param extensionHandshakers
     *      The extension handshaker in priority order. A handshaker could be repeated many times
     *      with fallback configuration.
     */
    public WebSocketClientExtensionHandler(params IWebSocketClientExtensionHandshaker[] extensionHandshakers)
    {
        this.extensionHandshakers = CheckNonEmpty(extensionHandshakers, "extensionHandshakers");
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (msg is IHttpRequest && WebSocketExtensionUtil.IsWebsocketUpgrade(((IHttpRequest) msg).Headers()))
        {
            IHttpRequest request = (IHttpRequest) msg;
            string headerValue = request.Headers().GetAsString(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS);
            IList<WebSocketExtensionData> extraExtensions =
              new List<WebSocketExtensionData>(extensionHandshakers.Count);

            foreach (IWebSocketClientExtensionHandshaker extensionHandshaker in extensionHandshakers)
            {
                extraExtensions.Add(extensionHandshaker.NewRequestData());
            }

            string newHeaderValue = WebSocketExtensionUtil
              .ComputeMergeExtensionsHeaderValue(headerValue, extraExtensions);

            request.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS, newHeaderValue);
        }

        return base.WriteAsync(ctx, msg, cancellationToken);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IHttpResponse)
        {
            IHttpResponse response = (IHttpResponse) msg;

            if (WebSocketExtensionUtil.IsWebsocketUpgrade(response.Headers()))
            {
                string extensionsHeader = response.Headers().GetAsString(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS);

                if (extensionsHeader != null)
                {
                    IList<WebSocketExtensionData> extensions =
                            WebSocketExtensionUtil.ExtractExtensions(extensionsHeader);
                    IList<IWebSocketClientExtension> validExtensions =
                            new List<IWebSocketClientExtension>(extensions.Count);
                    int rsv = 0;

                    foreach (WebSocketExtensionData extensionData in extensions)
                    {
                        IEnumerator<IWebSocketClientExtensionHandshaker> extensionHandshakersIterator =
                                extensionHandshakers.GetEnumerator();
                        IWebSocketClientExtension validExtension = null;

                        while (validExtension == null && extensionHandshakersIterator.MoveNext())
                        {
                            IWebSocketClientExtensionHandshaker extensionHandshaker =
                                    extensionHandshakersIterator.Current;
                            validExtension = extensionHandshaker.HandshakeExtension(extensionData);
                        }

                        if (validExtension != null && ((validExtension.Rsv() & rsv) == 0))
                        {
                            rsv = rsv | validExtension.Rsv();
                            validExtensions.Add(validExtension);
                        }
                        else
                        {
                            throw new CodecException(
                                    "invalid WebSocket Extension handshake for \"" + extensionsHeader + '"');
                        }
                    }

                    foreach (IWebSocketClientExtension validExtension in validExtensions)
                    {
                        WebSocketExtensionDecoder decoder = validExtension.NewExtensionDecoder();
                        WebSocketExtensionEncoder encoder = validExtension.NewExtensionEncoder();
                        ctx.Pipeline.AddAfter(ctx.Name, decoder.GetType().FullName, decoder);
                        ctx.Pipeline.AddAfter(ctx.Name, encoder.GetType().FullName, encoder);
                    }
                }

                ctx.Pipeline.Remove(ctx.Name);
            }
        }

        base.ChannelRead(ctx, msg);
    }
}
