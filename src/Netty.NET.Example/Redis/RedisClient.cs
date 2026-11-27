/*
 * Copyright 2016 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Redis;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Redis;

/**
 * Simple Redis client that demonstrates Redis commands against a Redis server.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments)
// and Integer.parseInt is int.Parse with an optional sign and the invariant culture. main blocks on connect(...).sync(),
// close().sync() and lastWriteFuture.sync(); the async Main awaits those Tasks (docs/transport-porting.md: sync() is
// awaiting the Task), and connect(...).sync().channel() is the result of ConnectAsync. group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future. The anonymous ChannelInitializer is an
// ActionChannelInitializer, the literal 1_000_000 is 1_000_000, and new BufferedReader(new InputStreamReader(System.in))
// is Console.In, whose ReadLine also returns null at the end of the input. String.trim() removes the characters up to
// U+0020, which Trim() over those characters does; "quit".equalsIgnoreCase(line) is an ordinal case-insensitive
// comparison. lastWriteFuture.addListener is a continuation of the write Task on the channel's event loop
// (EventExecutorTaskScheduler, the timing of DefaultPromise.notifyListeners()); future.isSuccess() is
// IsCompletedSuccessfully, future.cause() the Task's exception (a TaskCanceledException for a canceled Task), and
// printStackTrace(System.err) writes Exception.ToString() to Console.Error.
public class RedisClient
{
    private static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    private static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "6379"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    // CLR: the characters String.trim() removes (code points up to U+0020).
    private static readonly char[] JavaTrimChars = CreateJavaTrimChars();

    public static async Task Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group)
             .Channel<NioSocketChannel>()
             .Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
             {
                 IChannelPipeline p = ch.Pipeline;
                 p.AddLast(new RedisDecoder());
                 p.AddLast(new RedisBulkStringAggregator());
                 p.AddLast(new RedisArrayAggregator(1_000_000, 1024));
                 p.AddLast(new RedisEncoder());
                 p.AddLast(new RedisClientHandler());
             }));

            // Start the connection attempt.
            IChannel ch = await b.ConnectAsync(HOST, PORT);

            // Read commands from the stdin.
            Console.WriteLine("Enter Redis commands (quit to end)");
            Task lastWriteFuture = null;
            TextReader @in = Console.In;
            for (;;)
            {
                string input = @in.ReadLine();
                string line = input != null ? input.Trim(JavaTrimChars) : null;
                if (line == null || string.Equals("quit", line, StringComparison.OrdinalIgnoreCase))
                { // EOF or "quit"
                    await ch.CloseAsync();
                    break;
                }
                else if (line.Length == 0)
                { // skip `enter` or `enter` with spaces.
                    continue;
                }
                // Sends the received line to the server.
                lastWriteFuture = ch.WriteAndFlushAsync(line);
                _ = lastWriteFuture.ContinueWith(future =>
                {
                    if (!future.IsCompletedSuccessfully)
                    {
                        Console.Error.Write("write failed: ");
                        Console.Error.WriteLine(future.Exception?.InnerException ?? new TaskCanceledException(future));
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => ch.EventLoop));
            }

            // Wait until all messages are flushed before closing the channel.
            if (lastWriteFuture != null)
            {
                await lastWriteFuture;
            }
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private static char[] CreateJavaTrimChars()
    {
        char[] chars = new char[0x21];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = (char) i;
        }
        return chars;
    }
}
