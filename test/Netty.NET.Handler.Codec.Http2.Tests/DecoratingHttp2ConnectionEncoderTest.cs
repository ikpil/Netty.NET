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

using System;
using Moq;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: Mockito mock/verify(times(1)) are Moq Mock<T>/Verify(..., Times.Once()) and eq(settings) is the value itself,
// matched with Equals, which Http2Settings overrides with Java's map equality (docs/codec-http-porting.md, Shared CLR
// adaptations). assertThrows(IllegalStateException.class, ...) is Assert.ThrowsAny<InvalidOperationException>
// (docs/codec-http-porting.md, Core test notes). The private nested interface TestHttp2ConnectionEncoder is public
// (ITestHttp2ConnectionEncoder), because Moq's Castle DynamicProxy generates its proxy in another assembly and cannot
// implement a private nested interface.
public class DecoratingHttp2ConnectionEncoderTest
{

    [Fact]
    public void TestConsumeReceivedSettingsThrows()
    {
        IHttp2ConnectionEncoder encoder = new Mock<IHttp2ConnectionEncoder>().Object;
        DecoratingHttp2ConnectionEncoder decoratingHttp2ConnectionEncoder =
                new DecoratingHttp2ConnectionEncoder(encoder);
        Assert.ThrowsAny<InvalidOperationException>(
                () => decoratingHttp2ConnectionEncoder.ConsumeReceivedSettings(Http2Settings.DefaultSettings()));
    }

    [Fact]
    public void TestConsumeReceivedSettingsDelegate()
    {
        Mock<ITestHttp2ConnectionEncoder> encoder = new Mock<ITestHttp2ConnectionEncoder>();
        DecoratingHttp2ConnectionEncoder decoratingHttp2ConnectionEncoder =
                new DecoratingHttp2ConnectionEncoder(encoder.Object);

        Http2Settings settings = Http2Settings.DefaultSettings();
        decoratingHttp2ConnectionEncoder.ConsumeReceivedSettings(Http2Settings.DefaultSettings());
        encoder.Verify(e => e.ConsumeReceivedSettings(settings), Times.Exactly(1));
    }

    public interface ITestHttp2ConnectionEncoder : IHttp2ConnectionEncoder, IHttp2SettingsReceivedConsumer { }
}
