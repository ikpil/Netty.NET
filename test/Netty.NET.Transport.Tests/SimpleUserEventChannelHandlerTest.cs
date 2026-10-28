/*
 * Copyright 2018 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit2013 // Preserve the original explicit size assertions.
public class SimpleUserEventChannelHandlerTest
{
    private readonly FooEventCatcher fooEventCatcher;
    private readonly AllEventCatcher allEventCatcher;
    private readonly EmbeddedChannel channel;
    public SimpleUserEventChannelHandlerTest()
    {
        fooEventCatcher = new FooEventCatcher();
        allEventCatcher = new AllEventCatcher();
        channel = new EmbeddedChannel(fooEventCatcher, allEventCatcher);
    }
    [Fact]
    public void TestTypeMatch()
    {
        FooEvent fooEvent = new FooEvent();
        channel.Pipeline.FireUserEventTriggered(fooEvent);
        Assert.Equal(1, fooEventCatcher.caughtEvents.Count);
        Assert.Equal(0, allEventCatcher.caughtEvents.Count);
        Assert.Equal(0, fooEvent.ReferenceCount);
        Assert.False(channel.Finish());
    }
    [Fact]
    public void TestTypeMismatch()
    {
        BarEvent barEvent = new BarEvent();
        channel.Pipeline.FireUserEventTriggered(barEvent);
        Assert.Equal(0, fooEventCatcher.caughtEvents.Count);
        Assert.Equal(1, allEventCatcher.caughtEvents.Count);
        Assert.True(barEvent.Release());
        Assert.False(channel.Finish());
    }
    private sealed class FooEvent() : DefaultByteBufHolder(Unpooled.Buffer());
    private sealed class BarEvent() : DefaultByteBufHolder(Unpooled.Buffer());
    private sealed class FooEventCatcher : SimpleUserEventChannelHandler<FooEvent>
    {
        public readonly List<FooEvent> caughtEvents = new();
        protected override void EventReceived(ChannelHandlerContext context, FooEvent evt) => caughtEvents.Add(evt);
    }
    private sealed class AllEventCatcher : ChannelInboundHandlerAdapter
    {
        public readonly List<object> caughtEvents = new();
        public override void UserEventTriggered(ChannelHandlerContext context, object evt) => caughtEvents.Add(evt);
    }
}
