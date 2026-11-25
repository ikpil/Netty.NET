/*
 * Copyright 2019 The Netty Project
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

using Moq;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: @Mock fields and initMocks are Moq mocks created in the constructor (xUnit creates a test class instance per
// test, https://xunit.net/docs/shared-context). The mocked ByteBuf whose isReadable() returns true is a one-byte
// Unpooled buffer, because ByteBuf.IsReadable() is not virtual in the Buffer port (src/Netty.NET.Buffer/ByteBuf.cs) and
// Moq can only stub overridable members (https://github.com/devlooped/moq/wiki/Quickstart). verify(mock, times(n)) is
// Verify(..., Times.Exactly(n)), any(ByteBuf.class) is It.IsAny<ByteBuf>() and eq(x) the value itself.
// assertThrows(Http2Exception.class, ...) is Assert.ThrowsAny<Http2Exception> (docs/codec-http-porting.md, Core test
// notes); the anonymous Executables are lambdas.
public class Http2EmptyDataFrameListenerTest
{

    private readonly Mock<IHttp2FrameListener> frameListener;
    private readonly IChannelHandlerContext ctx;

    private readonly ByteBuf nonEmpty;

    private readonly Http2EmptyDataFrameListener listener;

    public Http2EmptyDataFrameListenerTest()
    {
        frameListener = new Mock<IHttp2FrameListener>();
        ctx = new Mock<IChannelHandlerContext>().Object;
        nonEmpty = Unpooled.Buffer(1).WriteByte(1);
        listener = new Http2EmptyDataFrameListener(frameListener.Object, 2);
    }

    [Fact]
    public void TestEmptyDataFrames()
    {
        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);
        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);

        Assert.ThrowsAny<Http2Exception>(() => listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false));
        frameListener.Verify(l => l.OnDataRead(ctx, 1, It.IsAny<ByteBuf>(), 0, false), Times.Exactly(2));
    }

    [Fact]
    public void TestEmptyDataFramesWithNonEmptyInBetween()
    {
        Http2EmptyDataFrameListener listener = new Http2EmptyDataFrameListener(frameListener.Object, 2);
        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);
        listener.OnDataRead(ctx, 1, nonEmpty, 0, false);

        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);
        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);

        Assert.ThrowsAny<Http2Exception>(() => listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false));
        frameListener.Verify(l => l.OnDataRead(ctx, 1, It.IsAny<ByteBuf>(), 0, false), Times.Exactly(4));
    }

    [Fact]
    public void TestEmptyDataFramesWithEndOfStreamInBetween()
    {
        Http2EmptyDataFrameListener listener = new Http2EmptyDataFrameListener(frameListener.Object, 2);
        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);
        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, true);

        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);
        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);

        Assert.ThrowsAny<Http2Exception>(() => listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false));

        frameListener.Verify(l => l.OnDataRead(ctx, 1, It.IsAny<ByteBuf>(), 0, true), Times.Exactly(1));
        frameListener.Verify(l => l.OnDataRead(ctx, 1, It.IsAny<ByteBuf>(), 0, false), Times.Exactly(3));
    }

    [Fact]
    public void TestEmptyDataFramesWithHeaderFrameInBetween()
    {
        Http2EmptyDataFrameListener listener = new Http2EmptyDataFrameListener(frameListener.Object, 2);
        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);
        listener.OnHeadersRead(ctx, 1, EmptyHttp2Headers.INSTANCE, 0, true);

        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);
        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);

        Assert.ThrowsAny<Http2Exception>(() => listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false));

        frameListener.Verify(l => l.OnHeadersRead(ctx, 1, EmptyHttp2Headers.INSTANCE, 0, true), Times.Exactly(1));
        frameListener.Verify(l => l.OnDataRead(ctx, 1, It.IsAny<ByteBuf>(), 0, false), Times.Exactly(3));
    }

    [Fact]
    public void TestEmptyDataFramesWithHeaderFrameInBetween2()
    {
        Http2EmptyDataFrameListener listener = new Http2EmptyDataFrameListener(frameListener.Object, 2);
        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);
        listener.OnHeadersRead(ctx, 1, EmptyHttp2Headers.INSTANCE, 0, (short) 0, false, 0, true);

        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);
        listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false);

        Assert.ThrowsAny<Http2Exception>(() => listener.OnDataRead(ctx, 1, Unpooled.EmptyBuffer, 0, false));

        frameListener.Verify(l => l.OnHeadersRead(ctx, 1,
                EmptyHttp2Headers.INSTANCE, 0, (short) 0, false, 0, true), Times.Exactly(1));
        frameListener.Verify(l => l.OnDataRead(ctx, 1, It.IsAny<ByteBuf>(), 0, false), Times.Exactly(3));
    }
}
