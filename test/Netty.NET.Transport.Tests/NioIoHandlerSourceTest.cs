/*
 * Copyright 2025 The Netty Project
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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.ManualIoEventLoopSourceTest;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1031 // Drive the original scenario on the same physical owner thread.
public class NioIoHandlerSourceTest
{
    [Fact(Timeout = 5000)]
    public void TestNioHandlerCanBeDrivenByMainThread()
    {
        var cancellation = TestContext.Current.CancellationToken;
        Thread owner = Thread.CurrentThread;
        // Create our own IoExecutor that is driven by the main thread
        var loop = new ManualIoEventLoop(owner);
        // Open a ServerSocketChannel that we can connect to.
        var address = Socket.OSSupportsIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(address, 0));
        socket.Listen(128);
        var listener = new TcpServerSocketChannel(socket);
        listener.Config.SetAutoRead(false);
        var accepted = new Accepted();
        listener.Pipeline.AddLast(accepted);
        try
        {
            loop.RegisterAsync(listener, cancellation).GetAwaiter().GetResult();
            listener.Read();
            using var peer = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            // Do the connect in another thread so we don't block our io execution loop.
            var connect = Task.Run(async () => await peer.ConnectAsync(socket.LocalEndPoint, cancellation), cancellation);
            int count = 0;

            // Let's loop until our registration was cancelled.
            while (listener.IsRegistered)
            {
                cancellation.ThrowIfCancellationRequested();
                // Just busy spin.
                count += loop.RunNow(-1);
                // Execute all tasks that were dispatched.
                loop.RunNonBlockingTasks(0);
                if (accepted.Owner != null)
                {
                    // We accepted the connection, let's cancel the registration.
                    listener.DeregisterAsync(cancellation).GetAwaiter().GetResult();
                    Assert.False(listener.IsRegistered);
                }
                else
                    Thread.Yield();
            }
            Assert.Same(owner, accepted.Owner);
            Assert.Equal(1, count);
            // Wait until the connect thread is done.
            connect.GetAwaiter().GetResult();
        }
        finally
        {
            // Let's now close the ServerSocketChannel and after that destroy the IoHandler.
            var close = listener.CloseTransportAsync();
            loop.RunNow();
            close.GetAwaiter().GetResult();
            Terminate(loop);
        }
    }

    private sealed class Accepted : ChannelInboundHandlerAdapter
    {
        internal Thread Owner;
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            // Just accept the connection and close it.
            ((AbstractChannel)message).CloseForcibly();
            Owner = Thread.CurrentThread;
        }
    }
}
