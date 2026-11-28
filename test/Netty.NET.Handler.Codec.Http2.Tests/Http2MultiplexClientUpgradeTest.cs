/*
 * Copyright 2018 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the abstract JUnit fixture is an abstract generic xUnit class whose [Fact] methods run once per derived
// class, as JUnit runs the inherited tests (owner rule 9); C extends Http2FrameCodec is the constraint where C :
// Http2FrameCodec. Http2StreamChannel is IHttp2StreamChannel, stream().state()/id() are Stream().State()/Id(),
// Http2Stream.State is the sealed class IHttp2Stream.State, ctx.channel() is the Channel property and @Sharable is
// [Sharable]. getProperty(codec.streamKey) is the generic GetProperty<object>, and codec.streamKey is the
// protected Java field that the package's tests read, which needs protected internal access in C#.
// assertThrows(Http2Exception.class, Executable) is Assert.ThrowsAny<Http2Exception> with a lambda.
public abstract class Http2MultiplexClientUpgradeTest<C> where C : Http2FrameCodec
{

    [Sharable]
    internal sealed class NoopHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.Channel.CloseAsync();
        }
    }

    private sealed class UpgradeHandler : ChannelInboundHandlerAdapter
    {
        internal IHttp2Stream.State stateOnActive;
        internal int streamId;
        internal bool channelInactiveCalled;

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            IHttp2StreamChannel ch = (IHttp2StreamChannel) ctx.Channel;
            stateOnActive = ch.Stream().State();
            streamId = ch.Stream().Id();
            base.ChannelActive(ctx);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            channelInactiveCalled = true;
            base.ChannelInactive(ctx);
        }
    }

    protected abstract C NewCodec(IChannelHandler upgradeHandler);

    protected abstract IChannelHandler NewMultiplexer(IChannelHandler upgradeHandler);

    [Fact]
    public void UpgradeHandlerGetsActivated()
    {
        UpgradeHandler upgradeHandler = new UpgradeHandler();
        C codec = NewCodec(upgradeHandler);
        EmbeddedChannel ch = new EmbeddedChannel(codec, NewMultiplexer(upgradeHandler));

        codec.OnHttpClientUpgrade();

        Assert.False(upgradeHandler.stateOnActive.LocalSideOpen());
        Assert.True(upgradeHandler.stateOnActive.RemoteSideOpen());
        Assert.NotNull(
                codec.Connection().Stream(Http2CodecUtil.HTTP_UPGRADE_STREAM_ID).GetProperty<object>(codec.streamKey));
        Assert.Equal(Http2CodecUtil.HTTP_UPGRADE_STREAM_ID, upgradeHandler.streamId);
        Assert.True(ch.FinishAndReleaseAll());
        Assert.True(upgradeHandler.channelInactiveCalled);
    }

    [Fact]
    public void ClientUpgradeWithoutUpgradeHandlerThrowsHttp2Exception()
    {
        C codec = NewCodec(null);
        EmbeddedChannel ch = new EmbeddedChannel(codec, NewMultiplexer(null));

        Assert.ThrowsAny<Http2Exception>(() =>
        {
            try
            {
                codec.OnHttpClientUpgrade();
            }
            finally
            {
                ch.FinishAndReleaseAll();
            }
        });
    }
}
