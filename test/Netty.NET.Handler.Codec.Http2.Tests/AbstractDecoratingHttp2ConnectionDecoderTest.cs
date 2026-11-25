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

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the abstract JUnit fixture is an abstract xUnit class whose [Fact]s run for each derived class, as JUnit runs
// the inherited tests per subclass. Class<? extends Http2FrameListener> is Type. Mockito's ArgumentCaptor and
// then(answer) are a Moq Callback that stores the listener passed to frameListener(listener) and a Returns that reads
// it. AssertJ assertThat(x).isNotInstanceOf(type) is Assert.False(type.IsInstanceOfType(x)).
public abstract class AbstractDecoratingHttp2ConnectionDecoderTest
{

    protected abstract DecoratingHttp2ConnectionDecoder NewDecoder(IHttp2ConnectionDecoder decoder);

    protected abstract Type DelegatingFrameListenerType();

    [Fact]
    public void TestDecoration()
    {
        Mock<IHttp2ConnectionDecoder> @delegate = new Mock<IHttp2ConnectionDecoder>();
        IHttp2FrameListener captured = null;
        @delegate.Setup(d => d.FrameListener(It.IsAny<IHttp2FrameListener>()))
                .Callback<IHttp2FrameListener>(l => captured = l);
        @delegate.Setup(d => d.FrameListener()).Returns(() => captured);
        IHttp2FrameListener listener = new Mock<IHttp2FrameListener>().Object;
        DecoratingHttp2ConnectionDecoder decoder = NewDecoder(@delegate.Object);
        decoder.FrameListener(listener);
        @delegate.Verify(d => d.FrameListener(It.IsAny<IHttp2FrameListener>()), Times.Once());

        Assert.False(DelegatingFrameListenerType().IsInstanceOfType(decoder.FrameListener()));
    }

    [Fact]
    public void TestDecorationWithNull()
    {
        IHttp2ConnectionDecoder @delegate = new Mock<IHttp2ConnectionDecoder>().Object;

        DecoratingHttp2ConnectionDecoder decoder = NewDecoder(@delegate);
        decoder.FrameListener(null);
        Assert.Null(decoder.FrameListener());
    }
}
