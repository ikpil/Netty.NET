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

using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions;

public class WebSocketExtensionFilterTest
{

    [Fact]
    public void TestNeverSkip()
    {
        IWebSocketExtensionFilter neverSkip = IWebSocketExtensionFilter.NEVER_SKIP;

        BinaryWebSocketFrame binaryFrame = new BinaryWebSocketFrame();
        Assert.False(neverSkip.MustSkip(binaryFrame));
        Assert.True(binaryFrame.Release());

        TextWebSocketFrame textFrame = new TextWebSocketFrame();
        Assert.False(neverSkip.MustSkip(textFrame));
        Assert.True(textFrame.Release());

        PingWebSocketFrame pingFrame = new PingWebSocketFrame();
        Assert.False(neverSkip.MustSkip(pingFrame));
        Assert.True(pingFrame.Release());

        PongWebSocketFrame pongFrame = new PongWebSocketFrame();
        Assert.False(neverSkip.MustSkip(pongFrame));
        Assert.True(pongFrame.Release());

        CloseWebSocketFrame closeFrame = new CloseWebSocketFrame();
        Assert.False(neverSkip.MustSkip(closeFrame));
        Assert.True(closeFrame.Release());

        ContinuationWebSocketFrame continuationFrame = new ContinuationWebSocketFrame();
        Assert.False(neverSkip.MustSkip(continuationFrame));
        Assert.True(continuationFrame.Release());
    }

    [Fact]
    public void TestAlwaysSkip()
    {
        IWebSocketExtensionFilter neverSkip = IWebSocketExtensionFilter.ALWAYS_SKIP;

        BinaryWebSocketFrame binaryFrame = new BinaryWebSocketFrame();
        Assert.True(neverSkip.MustSkip(binaryFrame));
        Assert.True(binaryFrame.Release());

        TextWebSocketFrame textFrame = new TextWebSocketFrame();
        Assert.True(neverSkip.MustSkip(textFrame));
        Assert.True(textFrame.Release());

        PingWebSocketFrame pingFrame = new PingWebSocketFrame();
        Assert.True(neverSkip.MustSkip(pingFrame));
        Assert.True(pingFrame.Release());

        PongWebSocketFrame pongFrame = new PongWebSocketFrame();
        Assert.True(neverSkip.MustSkip(pongFrame));
        Assert.True(pongFrame.Release());

        CloseWebSocketFrame closeFrame = new CloseWebSocketFrame();
        Assert.True(neverSkip.MustSkip(closeFrame));
        Assert.True(closeFrame.Release());

        ContinuationWebSocketFrame continuationFrame = new ContinuationWebSocketFrame();
        Assert.True(neverSkip.MustSkip(continuationFrame));
        Assert.True(continuationFrame.Release());
    }
}
