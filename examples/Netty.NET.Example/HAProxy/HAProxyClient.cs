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
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.HAProxy;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Example.HAProxy.HAProxyServer;

namespace Netty.NET.Example.HAProxy;

// CLR: import static HAProxyServer.* is using static, which brings HAProxyServer.PORT into scope. System.getProperty is
// ExampleLauncher.GetProperty (the -Dname=value arguments). main blocks on
// connect(...).sync(), each writeAndFlush(...).sync() and close().sync(); the async Main awaits those Tasks
// (docs/transport-porting.md: sync() is awaiting the Task), and connect(...).sync().channel() is the result of
// ConnectAsync. group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the
// future. CharsetUtil.US_ASCII is Encoding.ASCII.
public sealed class HAProxyClient
{

    private static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");

    public static async Task Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group)
             .Channel<NioSocketChannel>()
             .Handler(new HAProxyHandler());

            // Start the connection attempt.
            IChannel ch = await b.ConnectAsync(HOST, PORT);

            HAProxyMessage message = new HAProxyMessage(
                    HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.TCP4,
                    "127.0.0.1", "127.0.0.2", 8000, 9000);

            await ch.WriteAndFlushAsync(message);
            await ch.WriteAndFlushAsync(Unpooled.CopiedBuffer("Hello World!", Encoding.ASCII));
            await ch.WriteAndFlushAsync(Unpooled.CopiedBuffer("Bye now!", Encoding.ASCII));
            await ch.CloseAsync();
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
