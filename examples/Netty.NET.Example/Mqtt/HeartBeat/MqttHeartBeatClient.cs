/*
 * Copyright 2019 The Netty Project
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
using System;
using System.Globalization;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Mqtt;
using Netty.NET.Handler.Timeout;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Mqtt.HeartBeat;

// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is
// int.Parse with an optional sign and the invariant culture. main blocks on connect(...).sync() and
// closeFuture().sync(); the async Main awaits the Task<IChannel> of ConnectAsync (ChannelFuture f; f.channel() is
// its result) and the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task).
// group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
public sealed class MqttHeartBeatClient
{
    private MqttHeartBeatClient()
    {
    }

    private static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    private static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "1883"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    private static readonly string CLIENT_ID = ExampleLauncher.GetProperty("clientId", "guestClient");
    private static readonly string USER_NAME = ExampleLauncher.GetProperty("userName", "guest");
    private static readonly string PASSWORD = ExampleLauncher.GetProperty("password", "guest");

    public static async Task Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group);
            b.Channel<NioSocketChannel>();
            b.Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
            {
                ch.Pipeline.AddLast("encoder", MqttEncoder.INSTANCE);
                ch.Pipeline.AddLast("decoder", new MqttDecoder());
                ch.Pipeline.AddLast("heartBeatHandler", new IdleStateHandler(TimeSpan.Zero, TimeSpan.FromSeconds(20), TimeSpan.Zero));
                ch.Pipeline.AddLast("handler", new MqttHeartBeatClientHandler(CLIENT_ID, USER_NAME, PASSWORD));
            }));

            IChannel f = await b.ConnectAsync(HOST, PORT);
            Console.WriteLine("Client connected");
            await f.CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
