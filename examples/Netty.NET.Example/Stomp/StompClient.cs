/*
 * Copyright 2014 The Netty Project
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
using System.Globalization;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Stomp;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Stomp;


/**
 * very simple stomp client implementation example, requires running stomp server to actually work
 * uses default username/password and destination values from hornetq message broker
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is
// int.Parse with an optional sign and the invariant culture. main blocks on
// connect(...).sync().channel().closeFuture().sync(); the async Main awaits the Task<IChannel> of ConnectAsync and
// then the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task).
// group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
// SSL is read but unused, as in the source.
public sealed class StompClient
{

    internal static readonly bool SSL = ExampleLauncher.GetProperty("ssl") != null;
    internal static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "61613"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    internal static readonly string LOGIN = ExampleLauncher.GetProperty("login", "guest");
    internal static readonly string PASSCODE = ExampleLauncher.GetProperty("passcode", "guest");
    internal static readonly string TOPIC = ExampleLauncher.GetProperty("topic", "jms.topic.exampleTopic");

    public static async Task Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group).Channel<NioSocketChannel>();
            b.Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
            {
                IChannelPipeline pipeline = ch.Pipeline;
                pipeline.AddLast("decoder", new StompSubframeDecoder());
                pipeline.AddLast("encoder", new StompSubframeEncoder());
                pipeline.AddLast("aggregator", new StompSubframeAggregator(1048576));
                pipeline.AddLast("handler", new StompClientHandler());
            }));

            await (await b.ConnectAsync(HOST, PORT)).CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
