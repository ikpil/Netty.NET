/*
 * Copyright 2026 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original group shutdown and descriptor cleanup have independent ownership.
public class AbstractNioChannelSourceTest
{
    // https://github.com/netty/netty/issues/17103
    [Fact(Timeout = 5000)]
    public async Task TestRemoveReadOpDoesNotThrowAfterDeregister()
    {
        var group = new DefaultEventLoopGroup(1);
        var channel = new TcpSocketChannel();
        try
        {
            await group.RegisterAsync(channel, TestContext.Current.CancellationToken);
            // Deregistering runs doDeregister() which sets the IoRegistration field to null. This mimics
            // a deregistration that races with clearReadPending(): the isRegistered() check there reads the
            // separate "registered" boolean and may pass, after which the scheduled clearReadPendingRunnable
            // ends up calling removeReadOp() on the EventLoop once the registration has already been cleared.
            await channel.DeregisterAsync(TestContext.Current.CancellationToken);
            Assert.False(channel.IsRegistered);
            // Before the fix this threw a NullPointerException (or AssertionError with -ea) because
            // removeReadOp() dereferenced the now-null registration via registration().
            // CLR: ClearReadPending clears the socket backend demand without a SelectionKey/Unsafe facade.
            Assert.Null(Record.Exception(channel.ClearReadPending));
        }
        finally { await Bootstrap.CleanupAsync(channel); await group.StopAsync(); }
    }
}
#pragma warning restore xUnit1051
