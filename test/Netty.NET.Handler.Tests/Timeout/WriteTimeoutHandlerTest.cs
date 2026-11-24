/*
 * Copyright 2021 The Netty Project
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

using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Timeout;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Tests.Timeout;

// CLR: CountDownLatch is CountdownEvent (https://learn.microsoft.com/dotnet/api/system.threading.countdownevent) and
// the anonymous ChannelInboundHandlerAdapter and ChannelFutureListener are the named nested class WriteOnActive and a
// Task continuation. pipeline().addLast(group, handler) is the deprecated addLast(EventExecutorGroup, ChannelHandler...)
// that the Transport port does not port (src/Netty.NET.Transport/Channels/IChannelPipeline.cs); its body,
// addLast(group, null, handler), is called instead. shutdownGracefully() is ShutdownGracefullyAsync(), not awaited as in
// Java.
public class WriteTimeoutHandlerTest
{

    [Fact]
    public void TestPromiseUseDifferentExecutor()
    {
        IEventExecutorGroup group1 = new DefaultEventExecutorGroup(1);
        IEventExecutorGroup group2 = new DefaultEventExecutorGroup(1);
        EmbeddedChannel channel = new EmbeddedChannel(false, false);
        try
        {
            channel.Pipeline.AddLast(group1, null, new WriteTimeoutHandler(10000));
            CountdownEvent latch = new CountdownEvent(1);
            channel.Pipeline.AddLast(group2, null, new WriteOnActive(latch));

            channel.Register();
            latch.Wait(TestContext.Current.CancellationToken);
            Assert.True(channel.FinishAndReleaseAll());
        }
        finally
        {
            _ = group1.ShutdownGracefullyAsync();
            _ = group2.ShutdownGracefullyAsync();
        }
    }

    private sealed class WriteOnActive : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent latch;

        internal WriteOnActive(CountdownEvent latch)
        {
            this.latch = latch;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.WriteAndFlushAsync("something").ContinueWith(future => latch.Signal(),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
