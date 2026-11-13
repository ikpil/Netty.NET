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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

// CLR: verify the original completed-state contract through actual channel writes, without a ChannelFuture facade.
#pragma warning disable xUnit1031 // The manual event loop is owned and pumped by this test thread.
public class FailedChannelFutureSourceTest
{
    [Fact]
    public void TestConstantProperties()
    {
        Exception e = new Exception();
        var channel = new TaskTestChannel(_ => Task.FromException(e));
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        try
        {
            loop.RegisterAsync(channel, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            Task future = channel.WriteAndFlushAsync(new object(), TestContext.Current.CancellationToken);
            Assert.False(future.IsCompletedSuccessfully);
            Assert.True(future.IsFaulted);
            Assert.Same(e, future.Exception!.InnerException);
        }
        finally { ManualIoEventLoopSourceTest.Terminate(loop); }
    }
}
