/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http.Cors;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Stream;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Http.Cors;

/**
 * Please refer to the {@link CorsConfig} javadocs for information about all the
 * configuration options available.
 *
 * Below are some of configuration discussed in this example:
 * <h3>Support only a specific origin</h3>
 * To support a single origin instead of the wildcard use the following:
 * <pre>
 * CorsConfig corsConfig = CorsConfig.withOrigin("http://domain1.com")
 * </pre>
 *
 * <h3>Enable loading from the file system</h3>
 * To enable the server to handle an origin specified as 'null', which happens
 * when a web browser loads a file from the local file system use the following:
 * <pre>
 * corsConfig.isNullOriginAllowed()
 * </pre>
 *
 * <h3>Enable request headers</h3>
 * To enable additional request headers:
 * <pre>
 * corsConfig.allowedRequestHeaders("custom-request-header")
 * </pre>
 *
 * <h3>Expose response headers</h3>
 * By default a browser only exposes the following simple header:
 * <ul>
 * <li>Cache-Control</li>
 * <li>Content-Language</li>
 * <li>Content-Type</li>
 * <li>Expires</li>
 * <li>Last-Modified</li>
 * <li>Pragma</li>
 * </ul>
 * Any of the above response headers can be retrieved by:
 * <pre>
 * xhr.getResponseHeader("Content-Type");
 * </pre>
 * If you need to get access to other headers this must be enabled by the server, for example:
 * <pre>
 * corsConfig.exposedHeaders("custom-response-header");
 * </pre>
 */
// CLR: SocketChannel is ISocketChannel and ChannelPipeline is IChannelPipeline. The public initChannel(SocketChannel)
// widens the protected ChannelInitializer.initChannel; a C# override cannot change the accessibility of the virtual
// method, so it stays protected. ChunkedWriteHandler is in Netty.NET.Handler.Stream (io.netty.handler.stream).
public class HttpCorsServerInitializer : ChannelInitializer<ISocketChannel>
{
    private readonly SslContext sslCtx;

    public HttpCorsServerInitializer(SslContext sslCtx)
    {
        this.sslCtx = sslCtx;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        CorsConfig corsConfig = CorsConfigBuilder.ForAnyOrigin().AllowNullOrigin().AllowCredentials().Build();
        IChannelPipeline pipeline = ch.Pipeline;

        if (sslCtx != null)
        {
            pipeline.AddLast(sslCtx.NewHandler(ch.Alloc()));
        }

        pipeline.AddLast(new HttpResponseEncoder());
        pipeline.AddLast(new HttpRequestDecoder());
        pipeline.AddLast(new HttpObjectAggregator(65536));
        pipeline.AddLast(new ChunkedWriteHandler());
        pipeline.AddLast(new CorsHandler(corsConfig));
        pipeline.AddLast(new OkResponseHandler());
    }

}
