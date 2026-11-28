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
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Stream;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Http.Upload;

// CLR: SocketChannel is ISocketChannel and ChannelPipeline is IChannelPipeline. The public initChannel(SocketChannel)
// widens the protected ChannelInitializer.initChannel; a C# override cannot change the accessibility of the virtual
// method, so it stays protected. ChunkedWriteHandler is in Netty.NET.Handler.Stream (io.netty.handler.stream).
public class HttpUploadClientInitializer : ChannelInitializer<ISocketChannel>
{
    private const int MAX_CONTENT_LENGTH = 65536;

    private readonly SslContext sslCtx;

    public HttpUploadClientInitializer(SslContext sslCtx)
    {
        this.sslCtx = sslCtx;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        IChannelPipeline pipeline = ch.Pipeline;

        if (sslCtx != null)
        {
            pipeline.AddLast("ssl", sslCtx.NewHandler(ch.Alloc()));
        }

        pipeline.AddLast("codec", new HttpClientCodec());

        // Remove the following line if you don't want automatic content decompression.
        pipeline.AddLast("inflater", new HttpContentDecompressor(MAX_CONTENT_LENGTH));

        // to be used since huge file transfer
        pipeline.AddLast("chunkedWriter", new ChunkedWriteHandler());

        pipeline.AddLast("handler", new HttpUploadClientHandler());
    }
}
