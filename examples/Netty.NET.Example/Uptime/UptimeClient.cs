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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Timeout;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Uptime;


/**
 * Connects to a server periodically to measure and print the uptime of the
 * server.  This example demonstrates how to implement reliable reconnection
 * mechanism in Netty.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is
// int.Parse with an optional sign and the invariant culture. As in Java, main returns after starting the first
// connection attempt; the event loop threads are foreground threads (DefaultThreadFactory creates non-daemon
// threads), so the process keeps running as the JVM does. NioSocketChannel.class is Channel<NioSocketChannel>(),
// the anonymous ChannelInitializer is an ActionChannelInitializer, and connect() discards its Task where Java
// discards the future. The listener of connect() is a continuation of the connect Task; the Task does not carry
// the channel, so it runs synchronously on the thread that completes it (the channel's event loop, where Java
// notifies it). future.cause() is the Task's exception (a TaskCanceledException for a canceled Task, as cause() is
// a CancellationException there) and null on success.
public sealed class UptimeClient
{

    internal static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8080"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    // Sleep 5 seconds before a reconnection attempt.
    internal static readonly int RECONNECT_DELAY = int.Parse(ExampleLauncher.GetProperty("reconnectDelay", "5"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    // Reconnect when the server sends nothing for 10 seconds.
    private static readonly int READ_TIMEOUT = int.Parse(ExampleLauncher.GetProperty("readTimeout", "10"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static readonly UptimeClientHandler handler = new UptimeClientHandler();
    private static readonly Bootstrap bs = new Bootstrap();

    public static void Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        bs.Group(group)
                .Channel<NioSocketChannel>()
                .RemoteAddress(HOST, PORT)
                .Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
                {
                    ch.Pipeline.AddLast(new IdleStateHandler(READ_TIMEOUT, 0, 0), handler);
                }));
        _ = bs.ConnectAsync();
    }

    internal static void Connect()
    {
        _ = bs.ConnectAsync().ContinueWith(future =>
        {
            Exception cause = future.IsCompletedSuccessfully ? null
                    : future.Exception?.InnerException ?? new TaskCanceledException(future);
            if (cause != null)
            {
                handler.startTime = -1;
                handler.Println("Failed to connect: " + cause);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
