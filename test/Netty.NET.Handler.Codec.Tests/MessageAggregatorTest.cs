/*
 * Copyright 2017 The Netty Project
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

using System.Text;
using Moq;
using Moq.Protected;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: CharsetUtil.US_ASCII is Encoding.ASCII and ByteBufHolder is IByteBufHolder. Mockito's spy(MockMessageAggregator.class)
// is a Moq mock with CallBase = true, which invokes the base implementation when no setup overrides a member
// (https://github.com/devlooped/moq/wiki/Quickstart); the abstract members without a setup return the default value,
// as the spy's do. The stubbed methods are protected, so they are set up through Protected(), whose argument matching
// must use ItExpr; ItExpr.Is with Equals keeps the equals() matching of when(agg.isStartMessage(first)).
public class MessageAggregatorTest
{
    private sealed class ReadCounter : ChannelOutboundHandlerAdapter
    {
        internal int value;

        public override void Read(IChannelHandlerContext ctx)
        {
            value++;
            ctx.Read();
        }
    }

    // CLR: public rather than package-private: Moq proxies the type with Castle DynamicProxy in its own dynamic assembly
    // (DynamicProxyGenAssembly2, https://github.com/devlooped/moq/wiki/Quickstart), which cannot derive from a
    // non-public nested type.
    public abstract class MockMessageAggregator
        : MessageAggregator<IByteBufHolder, IByteBufHolder, IByteBufHolder, IByteBufHolder>
    {
        protected MockMessageAggregator() : base(1024)
        {
        }

        protected override IByteBufHolder BeginAggregation(IByteBufHolder start, ByteBuf content)
        {
            return start.Replace(content);
        }
    }

    private static IByteBufHolder Message(string @string)
    {
        return new DefaultByteBufHolder(
            Unpooled.CopiedBuffer(@string, Encoding.ASCII));
    }

    [Fact]
    public void TestReadFlowManagement()
    {
        ReadCounter counter = new ReadCounter();
        IByteBufHolder first = Message("first");
        IByteBufHolder chunk = Message("chunk");
        IByteBufHolder last = Message("last");

        Mock<MockMessageAggregator> spy = new Mock<MockMessageAggregator> { CallBase = true };
        spy.Protected().Setup<bool>("IsStartMessage", ItExpr.Is<IByteBufHolder>(m => first.Equals(m))).Returns(true);
        spy.Protected().Setup<bool>("IsContentMessage", ItExpr.Is<IByteBufHolder>(m => chunk.Equals(m))).Returns(true);
        spy.Protected().Setup<bool>("IsContentMessage", ItExpr.Is<IByteBufHolder>(m => last.Equals(m))).Returns(true);
        spy.Protected().Setup<bool>("IsLastContentMessage", ItExpr.Is<IByteBufHolder>(m => last.Equals(m))).Returns(true);
        MockMessageAggregator agg = spy.Object;

        EmbeddedChannel embedded = new EmbeddedChannel(counter, agg);
        embedded.Config.SetAutoRead(false);

        Assert.False(embedded.WriteInbound(first));
        Assert.False(embedded.WriteInbound(chunk));
        Assert.True(embedded.WriteInbound(last));

        Assert.Equal(3, counter.value); // 2 reads issued from MockMessageAggregator
                                        // 1 read issued from EmbeddedChannel constructor

        IByteBufHolder all = new DefaultByteBufHolder(Unpooled.WrappedBuffer(
            first.Content.Retain(), chunk.Content.Retain(), last.Content.Retain()));
        IByteBufHolder @out = embedded.ReadInbound<IByteBufHolder>();

        Assert.Equal(all, @out);
        Assert.True(all.Release() && @out.Release());
        Assert.False(embedded.Finish());
    }

    [Fact]
    public void TestCloseWhileAggregating()
    {
        ReadCounter counter = new ReadCounter();
        IByteBufHolder first = new TestMessage(Unpooled.CopiedBuffer("first", Encoding.ASCII));

        Mock<MockMessageAggregator> spy = new Mock<MockMessageAggregator> { CallBase = true };
        spy.Protected().Setup<bool>("IsStartMessage", ItExpr.Is<IByteBufHolder>(m => first.Equals(m))).Returns(true);
        spy.Protected().Setup<bool>("IsLastContentMessage", ItExpr.Is<IByteBufHolder>(m => first.Equals(m))).Returns(false);
        MockMessageAggregator agg = spy.Object;

        EmbeddedChannel embedded = new EmbeddedChannel(counter, agg);
        embedded.Config.SetAutoRead(false);

        Assert.False(embedded.WriteInbound(first));

        Assert.Equal(2, counter.value);
        // CLR: the anonymous Executable is a lambda.
        Assert.Throws<PrematureChannelClosureException>(() => embedded.Finish());
        Assert.Equal(0, first.RefCnt);
    }

    private sealed class TestMessage : DefaultByteBufHolder, IDecoderResultProvider
    {
        internal TestMessage(ByteBuf data) : base(data)
        {
        }

        // CLR: inside this class the simple name DecoderResult is this method, so the DecoderResult type is qualified
        // where it is used as an expression.
        public DecoderResult DecoderResult()
        {
            return global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS;
        }

        public void SetDecoderResult(DecoderResult result)
        {
            // NOOP
        }
    }
}
