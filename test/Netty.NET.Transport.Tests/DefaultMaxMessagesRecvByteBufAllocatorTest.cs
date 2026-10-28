/*
 * Copyright 2021 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

public class DefaultMaxMessagesRecvByteBufAllocatorTest
{
    private DefaultMaxMessagesRecvByteBufAllocator NewAllocator(bool ignoreReadBytes) => new Allocator(ignoreReadBytes);
    private sealed class Allocator(bool ignoreReadBytes) : DefaultMaxMessagesRecvByteBufAllocator(2, ignoreReadBytes)
    {
        public override IRecvByteBufAllocator.IHandle NewHandle() => new Handle(this);
        private sealed class Handle(Allocator allocator) : MaxMessageHandle(allocator)
        {
            public override int Guess() => 0;
        }
    }
    [Fact]
    public void TestRespectReadBytes()
    {
        DefaultMaxMessagesRecvByteBufAllocator allocator = NewAllocator(false);
        IRecvByteBufAllocator.IHandle handle = allocator.NewHandle();
        EmbeddedChannel channel = new EmbeddedChannel();
        handle.Reset(channel.Config);
        handle.IncMessagesRead(1);
        Assert.False(handle.ContinueReading());
        handle.Reset(channel.Config);
        handle.IncMessagesRead(1);
        handle.AttemptedBytesRead(1);
        handle.LastBytesRead(1);
        Assert.True(handle.ContinueReading());
        channel.Finish();
    }
    [Fact]
    public void TestIgnoreReadBytes()
    {
        DefaultMaxMessagesRecvByteBufAllocator allocator = NewAllocator(true);
        IRecvByteBufAllocator.IHandle handle = allocator.NewHandle();
        EmbeddedChannel channel = new EmbeddedChannel();
        handle.Reset(channel.Config);
        handle.IncMessagesRead(1);
        Assert.True(handle.ContinueReading());
        handle.IncMessagesRead(1);
        Assert.False(handle.ContinueReading());
        handle.Reset(channel.Config);
        handle.AttemptedBytesRead(0);
        handle.LastBytesRead(0);
        Assert.True(handle.ContinueReading());
        channel.Finish();
    }
}
