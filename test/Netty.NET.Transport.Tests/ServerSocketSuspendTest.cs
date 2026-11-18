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
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Peer socket connects run independently of the test cancellation.
public class ServerSocketSuspendTest : AbstractServerSocketTest
{
    private const int NUM_CHANNELS = 10;
    private const long TIMEOUT = 3000000000L;

    // CLR: JUnit's @Disabled marks a test that "is currently disabled and should not be executed"
    // (https://docs.junit.org/current/api/org.junit.jupiter.api/org/junit/jupiter/api/Disabled.html); xUnit's
    // FactAttribute.Skip is the "skip reason for the test", so it carries the source annotation's reason.
    [Fact(Skip = "Need to investigate why it fails on osx")]
    public async Task TestSuspendAndResumeAccept()
    {
        await Run(TestContext.Current, TestSuspendAndResumeAccept0);
    }

    // CLR: xUnit1024 forbids overloading the TestSuspendAndResumeAccept test name.
    private async Task TestSuspendAndResumeAccept0(ServerBootstrap sb)
    {
        AcceptedChannelCounter counter = new AcceptedChannelCounter(NUM_CHANNELS);

        sb.Option(ChannelOption.SO_BACKLOG, 1);
        sb.Option(ChannelOption.AUTO_READ, false);
        sb.ChildHandler(counter);

        IChannel sc = await sb.BindAsync();

        // CLR: java.net.Socket is a System.Net.Sockets.Socket stream socket.
        List<Socket> sockets = new List<Socket>();

        try
        {
            // CLR: Stopwatch timestamps replace System.nanoTime(); elapsed TimeSpan ticks are 100 ns each, so the
            // comparisons below stay in TIMEOUT's nanoseconds.
            long startTime = Stopwatch.GetTimestamp();
            for (int i = 0; i < NUM_CHANNELS; i ++)
            {
                Socket s = new Socket(SocketType.Stream, ProtocolType.Tcp);
                // CLR: SocketUtils.connect(s, address, 10000) connects with a 10000 ms timeout; the CLR connect
                // observes a CancellationToken that a CancellationTokenSource cancels after 10000 ms instead.
                using (CancellationTokenSource timeout = new CancellationTokenSource(10000))
                {
                    await s.ConnectAsync(sc.LocalAddress, timeout.Token);
                }
                sockets.Add(s);
            }

            sc.Config.SetAutoRead(true);

            counter.latch.Wait(TestContext.Current.CancellationToken);

            long endTime = Stopwatch.GetTimestamp();
            Assert.True(Stopwatch.GetElapsedTime(startTime, endTime).Ticks * 100 > TIMEOUT);
        }
        finally
        {
            foreach (Socket s in sockets)
            {
                s.Close();
            }
        }

        await Task.Delay((int) (TIMEOUT / 1000000));

        try
        {
            long startTime = Stopwatch.GetTimestamp();
            for (int i = 0; i < NUM_CHANNELS; i ++)
            {
                Socket s = new Socket(SocketType.Stream, ProtocolType.Tcp);
                // CLR: Socket.connect(address, 10000) is the same bounded connect as above.
                using (CancellationTokenSource timeout = new CancellationTokenSource(10000))
                {
                    await s.ConnectAsync(sc.LocalAddress, timeout.Token);
                }
                sockets.Add(s);
            }
            long endTime = Stopwatch.GetTimestamp();

            Assert.True(Stopwatch.GetElapsedTime(startTime, endTime).Ticks * 100 < TIMEOUT);
        }
        finally
        {
            foreach (Socket s in sockets)
            {
                s.Close();
            }
        }
    }

    [Sharable]
    private sealed class AcceptedChannelCounter : ChannelInboundHandlerAdapter
    {

        // CLR: CountdownEvent replaces CountDownLatch.
        internal readonly CountdownEvent latch;

        internal AcceptedChannelCounter(int nChannels)
        {
            latch = new CountdownEvent(nChannels);
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            latch.Signal();
        }
    }
}
