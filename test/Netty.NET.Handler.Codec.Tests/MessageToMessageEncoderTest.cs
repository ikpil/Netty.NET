/*
 * Copyright 2013 The Netty Project
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Tests;

#pragma warning disable xUnit1051 // The source's writeAndFlush takes no cancellation.

public class MessageToMessageEncoderTest
{
    /**
     * Test-case for https://github.com/netty/netty/issues/1656
     */
    [Fact]
    public void TestException()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new ThrowingEncoder());
        Assert.Throws<EncoderException>(() => channel.WriteOutbound(new object()));
    }

    [Fact]
    public async Task TestIntermediateWriteFailures()
    {
        IChannelHandler encoder = new TwoMessageEncoder();

        Exception firstWriteException = new Exception();

        IChannelHandler writeThrower = new FirstWriteFailer(firstWriteException);

        EmbeddedChannel channel = new EmbeddedChannel(writeThrower, encoder);
        object msg = new object();
        Task write = channel.WriteAndFlushAsync(msg);
        // CLR: write.cause() is the exception the completed Task rethrows when awaited.
        Assert.Same(firstWriteException, await Record.ExceptionAsync(
                () => write.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)));
        Assert.Same(msg, channel.ReadOutbound());
        Assert.False(channel.Finish());
    }

    // CLR: the anonymous handlers are named nested classes.
    private sealed class ThrowingEncoder : MessageToMessageEncoder<object>
    {
        protected override void Encode(IChannelHandlerContext ctx, object msg, IList<object> @out)
        {
            throw new Exception();
        }
    }

    private sealed class TwoMessageEncoder : MessageToMessageEncoder<object>
    {
        protected override void Encode(IChannelHandlerContext ctx, object msg, IList<object> @out)
        {
            @out.Add(new object());
            @out.Add(msg);
        }
    }

    private sealed class FirstWriteFailer(Exception firstWriteException) : ChannelOutboundHandlerAdapter
    {
        private bool firstWritten;

        // CLR: promise.setFailure(cause) is returning a faulted Task.
        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            if (firstWritten)
            {
                return ctx.WriteAsync(msg, cancellationToken);
            }
            else
            {
                firstWritten = true;
                return Task.FromException(firstWriteException);
            }
        }
    }
}
