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

namespace Netty.NET.Example.Telnet;

/**
 * Creates a newly configured {@link ChannelPipeline} for a new channel.
 */
// CLR: SocketChannel is ISocketChannel and ChannelPipeline IChannelPipeline. The public initChannel(SocketChannel) widens
// the protected ChannelInitializer.initChannel; a C# override cannot change the accessibility of the virtual method, so it
// stays protected. StringDecoder()/StringEncoder() use the default charset (Encoding.Default), as Java's use
// Charset.defaultCharset().
public class TelnetClientInitializer : ChannelInitializer<ISocketChannel>
{

    private static readonly StringDecoder DECODER = new StringDecoder();
    private static readonly StringEncoder ENCODER = new StringEncoder();

    private static readonly TelnetClientHandler CLIENT_HANDLER = new TelnetClientHandler();

    private readonly SslContext sslCtx;

    public TelnetClientInitializer(SslContext sslCtx)
    {
        this.sslCtx = sslCtx;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        IChannelPipeline pipeline = ch.Pipeline;

        if (sslCtx != null)
        {
            pipeline.AddLast(sslCtx.NewHandler(ch.Alloc(), TelnetClient.HOST, TelnetClient.PORT));
        }

        // Add the text line codec combination first,
        pipeline.AddLast(new DelimiterBasedFrameDecoder(8192, Delimiters.LineDelimiter()));
        pipeline.AddLast(DECODER);
        pipeline.AddLast(ENCODER);

        // and then business logic.
        pipeline.AddLast(CLIENT_HANDLER);
    }
}
