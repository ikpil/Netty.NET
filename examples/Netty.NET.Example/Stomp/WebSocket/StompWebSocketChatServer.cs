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
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Stomp.WebSocket;

// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is
// int.Parse with an optional sign and the invariant culture. start(port) blocks on
// bind(port).addListener(...).channel().closeFuture().sync(); StartAsync adds the listener as a continuation of
// the BindAsync Task and then awaits the bound channel's CloseFuture (docs/transport-porting.md: sync() is
// awaiting the Task). A failed bind closes its channel, so Java's closeFuture().sync() returns at once there;
// Task.WhenAny waits for the bind without rethrowing its failure, and only a bound channel's CloseFuture is
// awaited. The Task does not carry the channel, so the listener runs synchronously on the thread that completes it
// (the channel's event loop, where Java notifies it).
public class StompWebSocketChatServer
{

    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8080"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public async Task StartAsync(int port)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            ServerBootstrap bootstrap = new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new StompWebSocketChatServerInitializer("/chat"));
            Task<IChannel> bindFuture = bootstrap.BindAsync(port);
            _ = bindFuture.ContinueWith(future =>
            {
                if (future.IsCompletedSuccessfully)
                {
                    Console.WriteLine("Open your web browser and navigate to http://127.0.0.1:" + PORT + '/');
                }
                else
                {
                    Console.WriteLine("Cannot start server, follows exception " +
                            (future.Exception?.InnerException ?? new TaskCanceledException(future)));
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            await Task.WhenAny(bindFuture);

            if (bindFuture.IsCompletedSuccessfully)
            {
                await bindFuture.Result.CloseFuture;
            }
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    public static async Task Main(string[] args)
    {
        await new StompWebSocketChatServer().StartAsync(PORT);
    }
}
