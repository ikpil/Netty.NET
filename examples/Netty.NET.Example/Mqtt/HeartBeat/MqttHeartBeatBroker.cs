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
using System.Threading.Tasks;
using Netty.NET.Handler.Codec.Mqtt;
using Netty.NET.Handler.Timeout;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Mqtt.HeartBeat;

// CLR: main blocks on bind(1883).sync() and closeFuture().sync(); the async Main awaits the Task<IChannel> of BindAsync
// (ChannelFuture f; f.channel() is its result) and the channel's CloseFuture (docs/transport-porting.md: sync() is
// awaiting the Task). group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards
// the future. The anonymous ChannelInitializer is an ActionChannelInitializer, and IdleStateHandler(45, 0, 0,
// TimeUnit.SECONDS) is the TimeSpan constructor, whose arguments carry their unit.
public sealed class MqttHeartBeatBroker
{

    private MqttHeartBeatBroker()
    {
    }

    public static async Task Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            ServerBootstrap b = new ServerBootstrap();
            b.Group(group);
            b.Option(ChannelOption.SO_BACKLOG, 1024);
            b.Channel<NioServerSocketChannel>();
            b.ChildHandler(new ActionChannelInitializer<ISocketChannel>(ch =>
            {
                ch.Pipeline.AddLast("encoder", MqttEncoder.INSTANCE);
                ch.Pipeline.AddLast("decoder", new MqttDecoder());
                ch.Pipeline.AddLast("heartBeatHandler", new IdleStateHandler(TimeSpan.FromSeconds(45), TimeSpan.Zero, TimeSpan.Zero));
                ch.Pipeline.AddLast("handler", MqttHeartBeatBrokerHandler.INSTANCE);
            }));

            IChannel f = await b.BindAsync(1883);
            Console.WriteLine("Broker initiated...");

            await f.CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
