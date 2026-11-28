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
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Tests;

public class NioIoHandlerSourceTest
{
    [Fact(Timeout = 5000)]
    public void TestNioHandlerCanBeDrivenByMainThread()
    {
        IIoHandlerFactory factory = NioIoHandler.NewFactory();
        Thread current = Thread.CurrentThread;
        ConcurrentQueue<Action> tasks = new ConcurrentQueue<Action>();
        // Create our own IoExecutor that is driven by the main thread
        IThreadAwareExecutor executor = new MainThreadExecutor(current, tasks);
        IIoHandlerContext context = new BusySpinContext();
        IIoHandler handler = factory.NewHandler(executor);

        // Open a ServerSocketChannel that we can connect to.
        Socket channel = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        channel.Blocking = false;
        channel.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        channel.Listen(128);
        // Do the connect in another thread so we don't block our io execution loop.
        Thread t = new Thread(() =>
        {
            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(channel.LocalEndPoint);
        });
        AtomicBoolean acceptedConnection = new AtomicBoolean();
        IIoRegistration registration = handler.Register(new AcceptingHandle(channel, acceptedConnection));
        registration.Submit(NioIoOps.ACCEPT);
        t.Start();

        // Let's loop until our registration was cancelled.
        while (registration.IsValid())
        {
            handler.Run(context);

            for (;;)
            {
                // Execute all tasks that were dispatched.
                if (!tasks.TryDequeue(out Action r))
                {
                    break;
                }

                r();
            }

            if (acceptedConnection.Get())
            {
                // We accepted the connection, let's cancel the registration.
                Assert.True(registration.Cancel());
            }
        }

        // Wait until the connect thread is done.
        t.Join();
        // Let's now close the ServerSocketChannel and after that destroy the IoHandler.
        channel.Close();
        handler.PrepareToDestroy();
        handler.Destroy();
    }

    // CLR: the anonymous ThreadAwareExecutor, IoHandlerContext and NioSelectableChannelIoHandle of the Java method
    // become nested classes.
    private sealed class MainThreadExecutor(Thread current, ConcurrentQueue<Action> tasks) : IThreadAwareExecutor
    {
        public bool IsExecutorThread(Thread thread)
        {
            return current == thread;
        }

        public void Execute(Action command)
        {
            tasks.Enqueue(command);
        }
    }

    private sealed class BusySpinContext : IIoHandlerContext
    {
        public bool CanBlock()
        {
            // Just busy spin.
            return false;
        }

        public long DelayNanos(long currentTimeNanos)
        {
            return 0;
        }

        public long DeadlineNanos()
        {
            return -1;
        }
    }

    private sealed class AcceptingHandle(Socket channel, AtomicBoolean acceptedConnection)
        : NioSelectableChannelIoHandle<Socket>(channel)
    {
        protected override void Handle(Socket channel, NioIoOps key)
        {
            // CLR: SelectionKey.isAcceptable() is the OP_ACCEPT bit of the ready NioIoOps.
            if (key.Contains(NioIoOps.ACCEPT))
            {
                try
                {
                    // Just accept the connection and close it.
                    Socket accepted = channel.Accept();
                    accepted.Close();
                }
                catch (Exception)
                {
                    // ignore
                }
                finally
                {
                    acceptedConnection.Set(true);
                }
            }
        }
    }
}
