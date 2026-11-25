/*
 * Copyright 2023 The Netty Project
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
using Moq;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: @Mock fields and initMocks are Moq mocks created in the constructor (xUnit creates a test class instance per
// test, https://xunit.net/docs/shared-context); MockTicker and Ticker.newMockTicker() are the Common MockTicker and
// Ticker.NewMockTicker(), and ticker.advance(1, SECONDS) is Advance(TimeSpan.FromSeconds(1)). assertThrows returns the
// exception through Assert.ThrowsAny<Http2Exception> (docs/codec-http-porting.md, Core test notes); verify(mock,
// times(n)) is Verify(..., Times.Exactly(n)) with It.IsAny<int>() for anyInt().
public class Http2MaxRstFrameListenerTest
{

    private readonly Mock<IHttp2FrameListener> frameListener;
    private readonly IChannelHandlerContext ctx;

    private readonly MockTicker ticker = Ticker.NewMockTicker();

    private Http2MaxRstFrameListener listener;

    public Http2MaxRstFrameListenerTest()
    {
        frameListener = new Mock<IHttp2FrameListener>();
        ctx = new Mock<IChannelHandlerContext>().Object;
    }

    [Fact]
    public void TestMaxRstFramesReached()
    {
        listener = new Http2MaxRstFrameListener(frameListener.Object, 1, 10, ticker);
        listener.OnRstStreamRead(ctx, 1, Http2Error.STREAM_CLOSED.Code());

        Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                () => listener.OnRstStreamRead(ctx, 2, Http2Error.STREAM_CLOSED.Code()));
        Assert.Equal(Http2Error.ENHANCE_YOUR_CALM, ex.Error());
        frameListener.Verify(l => l.OnRstStreamRead(ctx, It.IsAny<int>(), Http2Error.STREAM_CLOSED.Code()),
                Times.Exactly(1));
    }

    [Fact]
    public void TestRstFramesWindowReset()
    {
        listener = new Http2MaxRstFrameListener(frameListener.Object, 1, 1, ticker);
        listener.OnRstStreamRead(ctx, 1, Http2Error.STREAM_CLOSED.Code());
        // Advance past the window so the counter resets.
        ticker.Advance(TimeSpan.FromSeconds(1));
        listener.OnRstStreamRead(ctx, 1, Http2Error.STREAM_CLOSED.Code());
        frameListener.Verify(l => l.OnRstStreamRead(ctx, It.IsAny<int>(), Http2Error.STREAM_CLOSED.Code()),
                Times.Exactly(2));
    }
}
