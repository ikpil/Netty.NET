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

using System;
using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the abstract JUnit fixture is an abstract xUnit class whose [Theory]s run for each derived class, as
// codec-http2's AbstractDecoratingHttp2ConnectionDecoderTest; T extends Http3Frame is T : class, IHttp3Frame. The
// @ParameterizedTest methods with @MethodSource("data") are [Theory]s with [MemberData(nameof(Data))]: data() is public
// static (xUnit reads public data members; MemberData finds it on the base class of the derived test class) and returns
// the same rows (true, false) as TheoryData<bool>; the display-name patterns have no xUnit counterpart. @AfterEach
// tearDown() is Dispose() (xUnit creates a test class instance per test case and disposes it after the case).
// assumeTrue(condition) is Assert.SkipUnless(condition, ...), which reports the case as skipped as JUnit reports an
// aborted test ("will skip the test if condition is false", https://xunit.net/docs/getting-started/v3/whats-new).
// assertThrows(Exception.class, ...) is Assert.ThrowsAny<Exception>. The members that name the internal
// EmbeddedQuicChannel, EmbeddedQuicStreamChannel and QpackAttributes (parent, qpackAttributes, qPACKEncoderStream(),
// qPACKDecoderStream(), readAndReleaseStreamHeader, newStream) are private protected: a protected member of a public
// class may not expose an internal type (CS0052, CS0050, CS0051), and private protected limits them to derived classes
// of this assembly. newStream's createStream(...).get() is the result of the completed Task of CreateStreamAsync
// (GetAwaiter().GetResult()); readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>() and readableBytes()
// the ReadableBytes property. List<T> is List<T> (Arrays.asList is a List initializer in the derived classes).
public abstract class AbstractHttp3FrameTypeValidationHandlerTest<T> : IDisposable where T : class, IHttp3Frame
{

    private readonly QuicStreamType defaultStreamType;
    private readonly bool isOutbound;
    private readonly bool isInbound;
    private protected EmbeddedQuicChannel parent;
    private protected QpackAttributes qpackAttributes;

    protected abstract IChannelHandler NewHandler(bool server);

    protected abstract List<T> NewValidFrames();

    protected abstract List<IHttp3Frame> NewInvalidFrames();

    protected AbstractHttp3FrameTypeValidationHandlerTest(QuicStreamType defaultStreamType,
                                                          bool isInbound, bool isOutbound)
    {
        this.defaultStreamType = defaultStreamType;
        this.isInbound = isInbound;
        this.isOutbound = isOutbound;
    }

    public static TheoryData<bool> Data()
    {
        return new TheoryData<bool> { true, false };
    }

    protected virtual void SetUp(bool server)
    {
        parent = new EmbeddedQuicChannel(server);
        qpackAttributes = new QpackAttributes(parent, false);
        Http3.SetQpackAttributes(parent, qpackAttributes);
    }

    public void Dispose()
    {
        if (parent != null)
        {
            QpackAttributes qpackAttributes = Http3.GetQpackAttributes(parent);
            Assert.NotNull(qpackAttributes);
            if (qpackAttributes.DecoderStreamAvailable())
            {
                Assert.False(((EmbeddedQuicStreamChannel) qpackAttributes.DecoderStream()).Finish());
            }
            if (qpackAttributes.EncoderStreamAvailable())
            {
                Assert.False(((EmbeddedQuicStreamChannel) qpackAttributes.EncoderStream()).Finish());
            }
            Assert.False(parent.Finish());
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestValidTypeInbound(bool server)
    {
        Assert.SkipUnless(isInbound, "isInbound");
        SetUp(server);
        EmbeddedQuicStreamChannel channel = NewStream(defaultStreamType, NewHandler(server));
        List<T> validFrames = NewValidFrames();
        foreach (T valid in validFrames)
        {
            Assert.True(channel.WriteInbound(valid));
            T read = channel.ReadInbound<T>();
            Http3TestUtils.AssertFrameSame(valid, read);
            if (valid is IHttp3SettingsFrame)
            {
                AfterSettingsFrameRead((IHttp3SettingsFrame) valid);
            }
        }
        Assert.False(channel.Finish());
    }

    protected virtual void AfterSettingsFrameRead(IHttp3SettingsFrame settingsFrame)
    {
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestValidTypeOutbound(bool server)
    {
        Assert.SkipUnless(isOutbound, "isOutbound");
        SetUp(server);
        EmbeddedQuicStreamChannel channel = NewStream(defaultStreamType, NewHandler(server));
        List<T> validFrames = NewValidFrames();
        foreach (T valid in validFrames)
        {
            Assert.True(channel.WriteOutbound(valid));
            T read = channel.ReadOutbound<T>();
            Http3TestUtils.AssertFrameSame(valid, read);
        }
        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInvalidTypeInbound(bool server)
    {
        Assert.SkipUnless(isInbound, "isInbound");
        SetUp(server);
        EmbeddedQuicStreamChannel channel = NewStream(defaultStreamType, NewHandler(server));

        Http3ErrorCode errorCode = InboundErrorCodeInvalid();
        List<IHttp3Frame> invalidFrames = NewInvalidFrames();
        foreach (IHttp3Frame invalid in invalidFrames)
        {
            Exception e = Assert.ThrowsAny<Exception>(() => channel.WriteInbound(invalid));
            Http3TestUtils.AssertException(errorCode, e);

            Http3TestUtils.AssertFrameReleased(invalid);
        }
        Http3TestUtils.VerifyClose(invalidFrames.Count, errorCode, parent);
        Assert.False(channel.Finish());
    }

    protected virtual Http3ErrorCode InboundErrorCodeInvalid()
    {
        return Http3ErrorCode.H3_FRAME_UNEXPECTED;
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInvalidTypeOutbound(bool server)
    {
        Assert.SkipUnless(isOutbound, "isOutbound");
        SetUp(server);
        EmbeddedQuicStreamChannel channel = NewStream(defaultStreamType, NewHandler(server));

        List<IHttp3Frame> invalidFrames = NewInvalidFrames();
        foreach (IHttp3Frame invalid in invalidFrames)
        {
            Exception e = Assert.ThrowsAny<Exception>(() => channel.WriteOutbound(invalid));
            Http3TestUtils.AssertException(Http3ErrorCode.H3_FRAME_UNEXPECTED, e);

            Http3TestUtils.AssertFrameReleased(invalid);
        }
        Assert.False(channel.Finish());
    }

    private protected EmbeddedQuicStreamChannel QPACKEncoderStream()
    {
        Assert.True(qpackAttributes.EncoderStreamAvailable());
        return (EmbeddedQuicStreamChannel) qpackAttributes.EncoderStream();
    }

    private protected EmbeddedQuicStreamChannel QPACKDecoderStream()
    {
        Assert.True(qpackAttributes.DecoderStreamAvailable());
        return (EmbeddedQuicStreamChannel) qpackAttributes.DecoderStream();
    }

    private protected void ReadAndReleaseStreamHeader(EmbeddedQuicStreamChannel stream)
    {
        ByteBuf streamType = stream.ReadOutbound<ByteBuf>();
        Assert.Equal(streamType.ReadableBytes, 1);
        ReferenceCountUtil.Release(streamType);
    }

    private protected virtual EmbeddedQuicStreamChannel NewStream(QuicStreamType streamType, IChannelHandler handler)
    {
        return (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(streamType, handler).GetAwaiter().GetResult();
    }
}
