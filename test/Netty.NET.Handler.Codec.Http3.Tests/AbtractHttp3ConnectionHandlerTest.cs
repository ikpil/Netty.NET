/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the abstract JUnit fixture (its name keeps the original spelling) is an abstract xUnit class whose [Fact]s run
// for each derived class, as codec-http2's AbstractDecoratingHttp2ConnectionDecoderTest.
// assertBidirectionalStreamHandled takes the internal EmbeddedQuicChannel, so it is private protected (a protected
// member of a public class may not expose an internal type, CS0051). The handler callbacks are called directly on the
// handler with the first context, as in Java (handlerAdded, channelRegistered, channelActive, channelRead,
// channelInactive, channelUnregistered and handlerRemoved are the IChannelHandler/IChannelInboundHandler members);
// pipeline().firstContext() is the FirstContext property and pipeline().get(Class) is Get<T>(). createStream(...).get()
// is an await of CreateStreamAsync with TestContext.Current.CancellationToken (the test methods that call it are async
// Task); QuicStreamChannel is IQuicStreamChannel. The throws clauses have no counterpart.
public abstract class AbtractHttp3ConnectionHandlerTest
{

    private readonly bool server;

    protected abstract Http3ConnectionHandler NewConnectionHandler();

    private protected abstract void AssertBidirectionalStreamHandled(EmbeddedQuicChannel channel,
                                                                     IQuicStreamChannel streamChannel);

    public AbtractHttp3ConnectionHandlerTest(bool server)
    {
        this.server = server;
    }

    [Fact]
    public void TestOpenLocalControlStream()
    {
        EmbeddedQuicChannel quicChannel = new EmbeddedQuicChannel(server, new ChannelDuplexHandler());
        IChannelHandlerContext ctx = quicChannel.Pipeline.FirstContext;

        Http3ConnectionHandler handler = NewConnectionHandler();
        handler.HandlerAdded(ctx);
        handler.ChannelRegistered(ctx);
        handler.ChannelActive(ctx);

        EmbeddedQuicStreamChannel localControlStream = quicChannel.LocalControlStream();
        Assert.NotNull(localControlStream);

        Assert.NotNull(Http3.GetLocalControlStream(quicChannel));

        handler.ChannelInactive(ctx);
        handler.ChannelUnregistered(ctx);
        handler.HandlerRemoved(ctx);

        Assert.True(localControlStream.FinishAndReleaseAll());
    }

    [Fact]
    public async Task TestBidirectionalStream()
    {
        EmbeddedQuicChannel quicChannel = new EmbeddedQuicChannel(server, new ChannelDuplexHandler());
        EmbeddedQuicStreamChannel bidirectionalStream =
                (EmbeddedQuicStreamChannel) await quicChannel.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL,
                        new ChannelDuplexHandler(), TestContext.Current.CancellationToken);
        IChannelHandlerContext ctx = quicChannel.Pipeline.FirstContext;

        Http3ConnectionHandler handler = NewConnectionHandler();
        handler.HandlerAdded(ctx);
        handler.ChannelRegistered(ctx);
        handler.ChannelActive(ctx);

        EmbeddedQuicStreamChannel localControlStream = quicChannel.LocalControlStream();
        Assert.NotNull(localControlStream);

        handler.ChannelRead(ctx, bidirectionalStream);

        AssertBidirectionalStreamHandled(quicChannel, bidirectionalStream);
        handler.ChannelInactive(ctx);
        handler.ChannelUnregistered(ctx);
        handler.HandlerRemoved(ctx);

        Assert.True(localControlStream.FinishAndReleaseAll());
    }

    [Fact]
    public async Task TestUnidirectionalStream()
    {
        EmbeddedQuicChannel quicChannel = new EmbeddedQuicChannel(server, new ChannelDuplexHandler());
        IQuicStreamChannel unidirectionalStream =
                await quicChannel.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL, new ChannelDuplexHandler(),
                        TestContext.Current.CancellationToken);
        IChannelHandlerContext ctx = quicChannel.Pipeline.FirstContext;

        Http3ConnectionHandler handler = NewConnectionHandler();
        handler.HandlerAdded(ctx);
        handler.ChannelRegistered(ctx);
        handler.ChannelActive(ctx);

        EmbeddedQuicStreamChannel localControlStream = quicChannel.LocalControlStream();
        Assert.NotNull(localControlStream);

        handler.ChannelRead(ctx, unidirectionalStream);

        Assert.NotNull(unidirectionalStream.Pipeline.Get<Http3UnidirectionalStreamInboundHandler>());

        handler.ChannelInactive(ctx);
        handler.ChannelUnregistered(ctx);
        handler.HandlerRemoved(ctx);

        Assert.True(localControlStream.FinishAndReleaseAll());
    }
}
