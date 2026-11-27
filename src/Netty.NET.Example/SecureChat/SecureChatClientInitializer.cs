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
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.String;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.SecureChat;

/**
 * Creates a newly configured {@link ChannelPipeline} for a new channel.
 */
// CLR: SocketChannel is ISocketChannel and ChannelPipeline IChannelPipeline. The public initChannel(SocketChannel) widens
// the protected ChannelInitializer.initChannel; a C# override cannot change the accessibility of the virtual method, so it
// stays protected.
public class SecureChatClientInitializer : ChannelInitializer<ISocketChannel>
{

    private readonly SslContext sslCtx;

    public SecureChatClientInitializer(SslContext sslCtx)
    {
        this.sslCtx = sslCtx;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        IChannelPipeline pipeline = ch.Pipeline;

        // Add SSL handler first to encrypt and decrypt everything.
        // In this example, we use a bogus certificate in the server side
        // and accept any invalid certificates in the client side.
        // You will need something more complicated to identify both
        // and server in the real world.
        pipeline.AddLast(sslCtx.NewHandler(ch.Alloc(), SecureChatClient.HOST, SecureChatClient.PORT));

        // On top of the SSL handler, add the text line codec.
        pipeline.AddLast(new DelimiterBasedFrameDecoder(8192, Delimiters.LineDelimiter()));
        pipeline.AddLast(new StringDecoder());
        pipeline.AddLast(new StringEncoder());

        // and then business logic.
        pipeline.AddLast(new SecureChatClientHandler());
    }
}
