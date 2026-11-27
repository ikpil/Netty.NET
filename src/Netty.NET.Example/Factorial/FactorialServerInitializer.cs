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
using Netty.NET.Handler.Codec.Compression;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Factorial;

/**
 * Creates a newly configured {@link ChannelPipeline} for a server-side channel.
 */
// CLR: SocketChannel is ISocketChannel and ChannelPipeline is IChannelPipeline. The public initChannel(SocketChannel)
// widens the protected ChannelInitializer.initChannel; a C# override cannot change the accessibility of the virtual
// method, so it stays protected.
public class FactorialServerInitializer : ChannelInitializer<ISocketChannel>
{
    private const int MAX_CONTENT_LENGTH = 65536;

    private readonly SslContext sslCtx;

    public FactorialServerInitializer(SslContext sslCtx)
    {
        this.sslCtx = sslCtx;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        IChannelPipeline pipeline = ch.Pipeline;

        if (sslCtx != null)
        {
            pipeline.AddLast(sslCtx.NewHandler(ch.Alloc()));
        }

        // Enable stream compression (you can remove these two if unnecessary)
        pipeline.AddLast(ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.GZIP));
        pipeline.AddLast(ZlibCodecFactory.NewZlibDecoder(ZlibWrapper.GZIP, MAX_CONTENT_LENGTH));

        // Add the number codec first,
        pipeline.AddLast(new BigIntegerDecoder());
        pipeline.AddLast(new NumberEncoder());

        // and then business logic.
        // Please note we create a handler for every new channel
        // because it has stateful properties.
        pipeline.AddLast(new FactorialServerHandler());
    }
}
