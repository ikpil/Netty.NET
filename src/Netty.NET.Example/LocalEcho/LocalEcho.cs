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
using System;
using System.IO;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Example.LocalEcho;

// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments).
// main blocks on bind(addr).sync() and connect(addr).sync(); the async Main awaits those Tasks
// (docs/transport-porting.md: sync() is awaiting the Task), and connect(addr).sync().channel() is the result of
// ConnectAsync. The anonymous ChannelInitializers are ActionChannelInitializers. new BufferedReader(new
// InputStreamReader(System.in)) is Console.In, whose ReadLine also returns null at the end of the input, and
// "quit".equalsIgnoreCase(line) is an ordinal case-insensitive comparison. lastWriteFuture.awaitUninterruptibly() waits
// for the write without throwing its failure, which awaiting Task.WhenAny(lastWriteFuture) does.
// shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
public sealed class LocalEcho
{

    internal static readonly string PORT = ExampleLauncher.GetProperty("port", "test_port");

    public static async Task Main(string[] args)
    {
        // Address to bind on / connect to.
        LocalAddress addr = new LocalAddress(PORT);

        IEventLoopGroup serverGroup = new MultiThreadIoEventLoopGroup(LocalIoHandler.NewFactory());
        IEventLoopGroup clientGroup =  new MultiThreadIoEventLoopGroup(LocalIoHandler.NewFactory());
        try
        {
            // Note that we can use any event loop to ensure certain local channels
            // are handled by the same event loop thread which drives a certain socket channel
            // to reduce the communication latency between socket channels and local channels.
            ServerBootstrap sb = new ServerBootstrap();
            sb.Group(serverGroup)
              .Channel<LocalServerChannel>()
              .Handler(new ActionChannelInitializer<LocalServerChannel>(ch =>
              {
                  ch.Pipeline.AddLast(new LoggingHandler(LogLevel.INFO));
              }))
              .ChildHandler(new ActionChannelInitializer<LocalChannel>(ch =>
              {
                  ch.Pipeline.AddLast(
                          new LoggingHandler(LogLevel.INFO),
                          new LocalEchoServerHandler());
              }));

            Bootstrap cb = new Bootstrap();
            cb.Group(clientGroup)
              .Channel<LocalChannel>()
              .Handler(new ActionChannelInitializer<LocalChannel>(ch =>
              {
                  ch.Pipeline.AddLast(
                          new LoggingHandler(LogLevel.INFO),
                          new LocalEchoClientHandler());
              }));

            // Start the server.
            await sb.BindAsync(addr);

            // Start the client.
            IChannel ch = await cb.ConnectAsync(addr);

            // Read commands from the stdin.
            Console.WriteLine("Enter text (quit to end)");
            Task lastWriteFuture = null;
            TextReader @in = Console.In;
            for (;;)
            {
                string line = @in.ReadLine();
                if (line == null || string.Equals("quit", line, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                // Sends the received line to the server.
                lastWriteFuture = ch.WriteAndFlushAsync(line);
            }

            // Wait until all messages are flushed before closing the channel.
            if (lastWriteFuture != null)
            {
                await Task.WhenAny(lastWriteFuture);
            }
        }
        finally
        {
            _ = serverGroup.ShutdownGracefullyAsync();
            _ = clientGroup.ShutdownGracefullyAsync();
        }
    }
}
