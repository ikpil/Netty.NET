/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Stomp.WebSocket;

// CLR: the Java interface SocketChannel is ISocketChannel; addLast(handler) chains through
// IChannelPipeline.AddLast(params IChannelHandler[]), which returns the pipeline as addLast does.
public class StompWebSocketChatServerInitializer : ChannelInitializer<ISocketChannel>
{

    private readonly string chatPath;
    private readonly StompWebSocketProtocolCodec stompWebSocketProtocolCodec;

    public StompWebSocketChatServerInitializer(string chatPath)
    {
        this.chatPath = ObjectUtil.CheckNotNull(chatPath, "chatPath");
        stompWebSocketProtocolCodec = new StompWebSocketProtocolCodec();
    }

    protected override void InitChannel(ISocketChannel channel)
    {
        channel.Pipeline
               .AddLast(new HttpServerCodec())
               .AddLast(new HttpObjectAggregator(65536))
               .AddLast(StompWebSocketClientPageHandler.INSTANCE)
               .AddLast(new WebSocketServerProtocolHandler(chatPath, StompVersion.SUB_PROTOCOLS))
               .AddLast(stompWebSocketProtocolCodec);
    }
}
