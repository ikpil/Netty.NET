/*
 * Copyright 2020 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the "License");
 * you may not use this file except in compliance with the License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License is
 * distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and limitations under the License.
 */

using System;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.WebSocketx;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: the package-private JUnit class is public for xUnit. The anonymous ThrowableAssert.ThrowingCallable instances
// are lambdas (Action), assertThatExceptionOfType(IllegalArgumentException.class).isThrownBy(...) is
// Assert.ThrowsAny<ArgumentException>, which like AssertJ accepts the type or a subtype, and assertThat(x).isEqualTo(y)
// is Assert.Equal(y, x). CharsetUtil.US_ASCII is Encoding.ASCII (docs/codec-smtp-porting.md).
public class CloseWebSocketFrameTest
{

    [Fact]
    public void TestInvalidCode()
    {
        DoTestInvalidCode(() => new CloseWebSocketFrame(WebSocketCloseStatus.ABNORMAL_CLOSURE));

        DoTestInvalidCode(() => new CloseWebSocketFrame(WebSocketCloseStatus.ABNORMAL_CLOSURE, "invalid code"));

        DoTestInvalidCode(() => new CloseWebSocketFrame(1006, "invalid code"));

        DoTestInvalidCode(() => new CloseWebSocketFrame(true, 0, 1006, "invalid code"));
    }

    [Fact]
    public void TestValidCode()
    {
        DoTestValidCode(new CloseWebSocketFrame(WebSocketCloseStatus.NORMAL_CLOSURE),
                WebSocketCloseStatus.NORMAL_CLOSURE.Code(), WebSocketCloseStatus.NORMAL_CLOSURE.ReasonText());

        DoTestValidCode(new CloseWebSocketFrame(WebSocketCloseStatus.NORMAL_CLOSURE, "valid code"),
                WebSocketCloseStatus.NORMAL_CLOSURE.Code(), "valid code");

        DoTestValidCode(new CloseWebSocketFrame(1000, "valid code"), 1000, "valid code");

        DoTestValidCode(new CloseWebSocketFrame(true, 0, 1000, "valid code"), 1000, "valid code");
    }

    [Fact]
    public void TestNonZeroReaderIndex()
    {
        ByteBuf buffer = Unpooled.Buffer().WriteZero(1);
        buffer.WriteShort(WebSocketCloseStatus.NORMAL_CLOSURE.Code())
                .WriteCharSequence(WebSocketCloseStatus.NORMAL_CLOSURE.ReasonText(), Encoding.ASCII);
        DoTestValidCode(new CloseWebSocketFrame(true, 0, buffer.SkipBytes(1)),
                WebSocketCloseStatus.NORMAL_CLOSURE.Code(), WebSocketCloseStatus.NORMAL_CLOSURE.ReasonText());
    }

    [Fact]
    public void TestCustomCloseCode()
    {
        ByteBuf buffer = Unpooled.Buffer().WriteZero(1);
        buffer.WriteShort(60000)
                .WriteCharSequence("Custom close code", Encoding.ASCII);
        DoTestValidCode(new CloseWebSocketFrame(true, 0, buffer.SkipBytes(1)),
                60000, "Custom close code");
    }

    private static void DoTestInvalidCode(Action callable)
    {
        Assert.ThrowsAny<ArgumentException>(callable);
    }

    private static void DoTestValidCode(CloseWebSocketFrame frame, int expectedCode, string expectedReason)
    {
        try
        {
            Assert.Equal(expectedCode, frame.StatusCode());
            Assert.Equal(expectedReason, frame.ReasonText());
        }
        finally
        {
            frame.Release();
        }
    }
}
