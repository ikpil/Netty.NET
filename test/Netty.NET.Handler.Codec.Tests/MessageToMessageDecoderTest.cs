/*
 * Copyright 2024 The Netty Project
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
using System.Threading;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Tests;

public class MessageToMessageDecoderTest
{
    [Fact]
    public void TestReadIfNotAutoReadWhenNotSharable()
    {
        ReadCountHandler readCountHandler = new ReadCountHandler();
        EmbeddedChannel channel = new EmbeddedChannel(new SkipFirstDecoder());
        channel.Config.SetAutoRead(false);
        channel.Pipeline.AddFirst(readCountHandler);
        Assert.False(channel.WriteInbound("something"));
        Assert.Equal(1, Volatile.Read(ref readCountHandler.readCount));
        Assert.True(channel.WriteInbound("something"));
        // As we produced a message in the MessageToMessageDecoder we don't expect that there will be any extra
        // read happen.
        Assert.Equal(1, Volatile.Read(ref readCountHandler.readCount));
        Assert.True(channel.FinishAndReleaseAll());
    }

    // CLR: the anonymous decoder is a named nested class.
    private sealed class SkipFirstDecoder : MessageToMessageDecoder<string>
    {
        private int count;

        protected override void Decode(IChannelHandlerContext ctx, string msg, IList<object> @out)
        {
            if (count++ == 0)
            {
                return;
            }
            @out.Add(msg);
        }
    }

    private sealed class ReadCountHandler : ChannelOutboundHandlerAdapter
    {
        // CLR: AtomicInteger is an int updated with Interlocked.
        internal int readCount;

        public override void Read(IChannelHandlerContext ctx)
        {
            Interlocked.Increment(ref readCount);
            base.Read(ctx);
        }
    }
}
