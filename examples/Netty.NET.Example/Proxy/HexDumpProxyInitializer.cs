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
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Proxy;

// CLR: initChannel is protected as the ChannelInitializer method it overrides ("An override declaration can't change
// the accessibility of the virtual method",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). The Java interface
// SocketChannel is ISocketChannel.
public class HexDumpProxyInitializer : ChannelInitializer<ISocketChannel>
{

    private readonly string remoteHost;
    private readonly int remotePort;

    public HexDumpProxyInitializer(string remoteHost, int remotePort)
    {
        this.remoteHost = remoteHost;
        this.remotePort = remotePort;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        ch.Pipeline.AddLast(
                new LoggingHandler(LogLevel.INFO),
                new HexDumpProxyFrontendHandler(remoteHost, remotePort));
    }
}
