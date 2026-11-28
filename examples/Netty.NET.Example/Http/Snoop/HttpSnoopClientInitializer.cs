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
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Http.Snoop;

// CLR: SocketChannel is ISocketChannel and ChannelPipeline is IChannelPipeline. The public initChannel(SocketChannel)
// widens the protected ChannelInitializer.initChannel; a C# override cannot change the accessibility of the virtual
// method, so it stays protected.
public class HttpSnoopClientInitializer : ChannelInitializer<ISocketChannel>
{
    private const int MAX_CONTENT_LENGTH = 65536;

    private readonly SslContext sslCtx;

    public HttpSnoopClientInitializer(SslContext sslCtx)
    {
        this.sslCtx = sslCtx;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        IChannelPipeline p = ch.Pipeline;

        // Enable HTTPS if necessary.
        if (sslCtx != null)
        {
            p.AddLast(sslCtx.NewHandler(ch.Alloc()));
        }

        p.AddLast(new HttpClientCodec());

        // Remove the following line if you don't want automatic content decompression.
        p.AddLast(new HttpContentDecompressor(MAX_CONTENT_LENGTH));

        // Uncomment the following line if you don't want to handle HttpContents.
        //p.addLast(new HttpObjectAggregator(1048576));

        p.AddLast(new HttpSnoopClientHandler());
    }
}
