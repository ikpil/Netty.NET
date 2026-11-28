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
using Netty.NET.Handler.Codec.Socksx;
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.SocksProxy;

// CLR: initChannel is protected as the ChannelInitializer method it overrides (an override cannot change the
// accessibility). The Java interface SocketChannel is ISocketChannel.
public sealed class SocksServerInitializer : ChannelInitializer<ISocketChannel>
{
    protected override void InitChannel(ISocketChannel ch)
    {
        ch.Pipeline.AddLast(
                new LoggingHandler(LogLevel.DEBUG),
                new SocksPortUnificationServerHandler(),
                SocksServerHandler.INSTANCE);
    }
}
