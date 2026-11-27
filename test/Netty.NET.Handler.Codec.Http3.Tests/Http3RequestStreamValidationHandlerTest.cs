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
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using Xunit.Sdk;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the four theories of AbstractHttp3FrameTypeValidationHandlerTest run for this class too (super(true, true): both
// inbound and outbound), with NewHandler and NewValidFrames overridden as in Java. The @ParameterizedTest methods with
// @MethodSource("data") are [Theory]s with [MemberData(nameof(Data))] over the base class's public static Data() (the
// rows true, false); the display-name patterns have no xUnit counterpart. The anonymous ChannelInitializer
// <QuicStreamChannel> classes of newHandler(boolean) and newClientStream(BooleanSupplier) are the private nested classes
// ServerValidatorInitializer and ClientValidatorInitializer over ChannelInitializer<IQuicStreamChannel>; like the
// anonymous classes they read the test's qpackAttributes and decoder when the channel is initialized.
// Http3RequestStreamValidationHandler.newServerValidator/newClientValidator, the state validators and QpackDecoder are
// internal (package-private in Java) and reached through InternalsVisibleTo. BooleanSupplier is Func<bool>; the
// AtomicBoolean of testGoawayReceivedAfterWritingHeaders, read by goAway::get, is a local bool captured by the lambda
// () => goAway. assertThrows(Exception.class, ...) is Assert.ThrowsAny<Exception>, fail() is Assert.Fail(), and
// assertEquals(Object, Object) of two frames compares with object.Equals (as Http3TestUtils.assertFrameEquals).
// JUnit's fail() and failed assertions throw an Error that catch (Exception e) does not catch, while xUnit's assertion
// exceptions are Exceptions, so the catch of testInformationalResponse is catch (Exception e) when e is not an
// IAssertionException; catch (Throwable cause) of testHeadersFrame is catch (Exception cause), which catches both, and
// "throw cause" is "throw". The Integer code.code compared with outputShutdownError() is an int? and
// ReferenceCountUtil.release (static import) is ReferenceCountUtil.Release. A Java String passed as a CharSequence is a
// StringCharSequence (Http3TestUtils.Cs). readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>(),
// refCnt() the RefCnt property, isActive() IsActive, and Unpooled.directBuffer() Unpooled.DirectBuffer(). The throws
// clauses and @Nullable (org.jetbrains.annotations) have no counterpart.
public class Http3RequestStreamValidationHandlerTest : Http3FrameTypeValidationHandlerTest
{
    private readonly QpackDecoder decoder;

    public Http3RequestStreamValidationHandlerTest() : base(true, true)
    {
        decoder = new QpackDecoder(0, 0);
    }

    protected override IChannelHandler NewHandler(bool server)
    {
        return new ServerValidatorInitializer(this);
    }

    // CLR: the anonymous ChannelInitializer of newHandler(boolean).
    private sealed class ServerValidatorInitializer : ChannelInitializer<IQuicStreamChannel>
    {
        private readonly Http3RequestStreamValidationHandlerTest test;

        internal ServerValidatorInitializer(Http3RequestStreamValidationHandlerTest test)
        {
            this.test = test;
        }

        protected override void InitChannel(IQuicStreamChannel ch)
        {
            Http3RequestStreamEncodeStateValidator encStateValidator = new Http3RequestStreamEncodeStateValidator();
            Http3RequestStreamDecodeStateValidator decStateValidator = new Http3RequestStreamDecodeStateValidator();
            ch.Pipeline.AddLast(encStateValidator);
            ch.Pipeline.AddLast(decStateValidator);
            ch.Pipeline.AddLast(Http3RequestStreamValidationHandler.NewServerValidator(test.qpackAttributes,
                    test.decoder, encStateValidator, decStateValidator));
        }
    }

    protected override List<IHttp3RequestStreamFrame> NewValidFrames()
    {
        return new List<IHttp3RequestStreamFrame>
        {
            new DefaultHttp3HeadersFrame(), new DefaultHttp3DataFrame(Unpooled.DirectBuffer()),
            new DefaultHttp3UnknownFrame(Http3CodecUtils.MAX_RESERVED_FRAME_TYPE, Unpooled.Buffer())
        };
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInvalidFrameSequenceStartInbound(bool server)
    {
        SetUp(server);
        EmbeddedQuicStreamChannel channel = NewStream(QuicStreamType.BIDIRECTIONAL, NewHandler(server));
        IHttp3DataFrame dataFrame = new DefaultHttp3DataFrame(Unpooled.Buffer());

        Exception e = Assert.ThrowsAny<Exception>(() => channel.WriteInbound(dataFrame));
        AssertException(Http3ErrorCode.H3_FRAME_UNEXPECTED, e);

        VerifyClose(Http3ErrorCode.H3_FRAME_UNEXPECTED, parent);
        Assert.Equal(0, dataFrame.RefCnt);
        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInvalidFrameSequenceEndInbound(bool server)
    {
        SetUp(server);
        EmbeddedQuicStreamChannel channel = NewStream(QuicStreamType.BIDIRECTIONAL, NewHandler(server));

        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        IHttp3DataFrame dataFrame = new DefaultHttp3DataFrame(Unpooled.Buffer());
        IHttp3DataFrame dataFrame2 = new DefaultHttp3DataFrame(Unpooled.Buffer());
        IHttp3DataFrame dataFrame3 = new DefaultHttp3DataFrame(Unpooled.Buffer());
        IHttp3HeadersFrame trailersFrame = new DefaultHttp3HeadersFrame();

        Assert.True(channel.WriteInbound(headersFrame));
        Assert.True(channel.WriteInbound(dataFrame.RetainedDuplicate()));
        Assert.True(channel.WriteInbound(dataFrame2.RetainedDuplicate()));
        Assert.True(channel.WriteInbound(trailersFrame));

        Exception e = Assert.ThrowsAny<Exception>(() => channel.WriteInbound(dataFrame3));
        AssertException(Http3ErrorCode.H3_FRAME_UNEXPECTED, e);

        VerifyClose(Http3ErrorCode.H3_FRAME_UNEXPECTED, parent);
        Assert.True(channel.Finish());
        Assert.Equal(0, dataFrame3.RefCnt);

        AssertFrameEquals(headersFrame, channel.ReadInbound<IHttp3Frame>());
        AssertFrameEquals(dataFrame, channel.ReadInbound<IHttp3Frame>());
        AssertFrameEquals(dataFrame2, channel.ReadInbound<IHttp3Frame>());
        AssertFrameEquals(trailersFrame, channel.ReadInbound<IHttp3Frame>());
        Assert.Null(channel.ReadInbound());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInvalidFrameSequenceStartOutbound(bool server)
    {
        SetUp(server);
        EmbeddedQuicStreamChannel channel = NewStream(QuicStreamType.BIDIRECTIONAL, NewHandler(server));

        IHttp3DataFrame dataFrame = new DefaultHttp3DataFrame(Unpooled.Buffer());

        Exception e = Assert.ThrowsAny<Exception>(() => channel.WriteOutbound(dataFrame));
        AssertException(Http3ErrorCode.H3_FRAME_UNEXPECTED, e);

        Assert.False(channel.Finish());
        Assert.Equal(0, dataFrame.RefCnt);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInvalidFrameSequenceEndOutbound(bool server)
    {
        SetUp(server);
        EmbeddedQuicStreamChannel channel = NewStream(QuicStreamType.BIDIRECTIONAL, NewHandler(server));

        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        IHttp3DataFrame dataFrame = new DefaultHttp3DataFrame(Unpooled.Buffer());
        IHttp3DataFrame dataFrame2 = new DefaultHttp3DataFrame(Unpooled.Buffer());
        IHttp3DataFrame dat3Frame3 = new DefaultHttp3DataFrame(Unpooled.Buffer());
        IHttp3HeadersFrame trailersFrame = new DefaultHttp3HeadersFrame();
        Assert.True(channel.WriteOutbound(headersFrame));
        Assert.True(channel.WriteOutbound(dataFrame.RetainedDuplicate()));
        Assert.True(channel.WriteOutbound(dataFrame2.RetainedDuplicate()));
        Assert.True(channel.WriteOutbound(trailersFrame));

        Exception e = Assert.ThrowsAny<Exception>(() => channel.WriteOutbound(dat3Frame3));
        AssertException(Http3ErrorCode.H3_FRAME_UNEXPECTED, e);

        Assert.True(channel.Finish());
        Assert.Equal(0, dat3Frame3.RefCnt);

        AssertFrameEquals(headersFrame, channel.ReadOutbound<IHttp3Frame>());
        AssertFrameEquals(dataFrame, channel.ReadOutbound<IHttp3Frame>());
        AssertFrameEquals(dataFrame2, channel.ReadOutbound<IHttp3Frame>());
        AssertFrameEquals(trailersFrame, channel.ReadOutbound<IHttp3Frame>());
        Assert.Null(channel.ReadOutbound());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestGoawayReceivedBeforeWritingHeaders(bool server)
    {
        SetUp(server);
        EmbeddedQuicStreamChannel channel = NewClientStream(() => true);

        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        Exception e = Assert.ThrowsAny<Exception>(() => channel.WriteOutbound(headersFrame));
        AssertException(Http3ErrorCode.H3_FRAME_UNEXPECTED, e);

        // We should have closed the channel.
        Assert.False(channel.IsActive);
        Assert.False(channel.Finish());
        Assert.Null(channel.ReadOutbound());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestGoawayReceivedAfterWritingHeaders(bool server)
    {
        SetUp(server);
        bool goAway = false;
        EmbeddedQuicStreamChannel channel = NewClientStream(() => goAway);

        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        IHttp3DataFrame dataFrame = new DefaultHttp3DataFrame(Unpooled.Buffer());
        Assert.True(channel.WriteOutbound(headersFrame));
        goAway = true;
        Assert.True(channel.WriteOutbound(dataFrame.RetainedDuplicate()));
        Assert.True(channel.Finish());
        AssertFrameEquals(headersFrame, channel.ReadOutbound<IHttp3Frame>());
        AssertFrameEquals(dataFrame, channel.ReadOutbound<IHttp3Frame>());

        Assert.Null(channel.ReadOutbound());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestClientHeadRequestWithContentLength(bool server)
    {
        SetUp(server);
        EmbeddedQuicStreamChannel channel = NewClientStream(() => false);

        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        headersFrame.Headers().Method(HttpMethod.HEAD.AsciiName());
        Assert.True(channel.WriteOutbound(headersFrame));

        IHttp3HeadersFrame responseHeadersFrame = new DefaultHttp3HeadersFrame();
        responseHeadersFrame.Headers().SetLong(HttpHeaderNames.CONTENT_LENGTH, 10);

        Assert.True(channel.WriteInbound(responseHeadersFrame));
        channel.Pipeline.FireUserEventTriggered(ChannelInputShutdownReadComplete.INSTANCE);

        Assert.True(channel.FinishAndReleaseAll());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestClientNonHeadRequestWithContentLengthNoData(bool server)
    {
        SetUp(server);
        TestClientNonHeadRequestWithContentLength(true, false);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestClientNonHeadRequestWithContentLengthNoDataAndTrailers(bool server)
    {
        SetUp(server);
        TestClientNonHeadRequestWithContentLength(true, true);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestClientNonHeadRequestWithContentLengthNotEnoughData(bool server)
    {
        SetUp(server);
        TestClientNonHeadRequestWithContentLength(false, false);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestClientNonHeadRequestWithContentLengthNotEnoughDataAndTrailer(bool server)
    {
        SetUp(server);
        TestClientNonHeadRequestWithContentLength(false, true);
    }

    private void TestClientNonHeadRequestWithContentLength(bool noData, bool trailers)
    {
        EmbeddedQuicStreamChannel channel = NewClientStream(() => false);

        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        headersFrame.Headers().Method(HttpMethod.GET.AsciiName());
        Assert.True(channel.WriteOutbound(headersFrame));

        IHttp3HeadersFrame responseHeadersFrame = new DefaultHttp3HeadersFrame();
        responseHeadersFrame.Headers().SetLong(HttpHeaderNames.CONTENT_LENGTH, 10);

        Assert.True(channel.WriteInbound(responseHeadersFrame));
        if (!noData)
        {
            Assert.True(channel.WriteInbound(new DefaultHttp3DataFrame(Unpooled.Buffer().WriteZero(9))));
        }
        try
        {
            if (trailers)
            {
                channel.WriteInbound(new DefaultHttp3HeadersFrame());
            }
            else
            {
                channel.Pipeline.FireUserEventTriggered(ChannelInputShutdownReadComplete.INSTANCE);
                channel.CheckException();
            }
        }
        catch (Exception e)
        {
            AssertException(Http3ErrorCode.H3_MESSAGE_ERROR, e);
        }
        Assert.True(channel.FinishAndReleaseAll());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestServerWithContentLengthNoData(bool server)
    {
        SetUp(server);
        TestServerWithContentLength(true, false);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestServerWithContentLengthNoDataAndTrailers(bool server)
    {
        SetUp(server);
        TestServerWithContentLength(true, true);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestServerWithContentLengthNotEnoughData(bool server)
    {
        SetUp(server);
        TestServerWithContentLength(false, false);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestServerWithContentLengthNotEnoughDataAndTrailer(bool server)
    {
        SetUp(server);
        TestServerWithContentLength(false, true);
    }

    private void TestServerWithContentLength(bool noData, bool trailers)
    {
        EmbeddedQuicStreamChannel channel = NewServerStream();

        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        headersFrame.Headers().SetLong(HttpHeaderNames.CONTENT_LENGTH, 10);
        headersFrame.Headers().Method(HttpMethod.POST.AsciiName());
        Assert.True(channel.WriteInbound(headersFrame));

        if (!noData)
        {
            Assert.True(channel.WriteInbound(new DefaultHttp3DataFrame(Unpooled.Buffer().WriteZero(9))));
        }
        try
        {
            if (trailers)
            {
                channel.WriteInbound(new DefaultHttp3HeadersFrame());
            }
            else
            {
                channel.Pipeline.FireUserEventTriggered(ChannelInputShutdownReadComplete.INSTANCE);
                channel.CheckException();
            }
        }
        catch (Exception e)
        {
            AssertException(Http3ErrorCode.H3_MESSAGE_ERROR, e);
        }
        Assert.True(channel.FinishAndReleaseAll());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3HeadersFrameWithConnectionHeader(bool server)
    {
        SetUp(server);
        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        headersFrame.Headers().Add(HttpHeaderNames.CONNECTION, Cs("something"));
        TestHeadersFrame(headersFrame, Http3ErrorCode.H3_MESSAGE_ERROR);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3HeadersFrameWithTeHeaderAndInvalidValue(bool server)
    {
        SetUp(server);
        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        headersFrame.Headers().Add(HttpHeaderNames.TE, Cs("something"));
        TestHeadersFrame(headersFrame, Http3ErrorCode.H3_MESSAGE_ERROR);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3HeadersFrameWithTeHeaderAndValidValue(bool server)
    {
        SetUp(server);
        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        headersFrame.Headers().Add(HttpHeaderNames.TE, HttpHeaderValues.TRAILERS);
        TestHeadersFrame(headersFrame, null);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInformationalResponseAfterActualResponseServer(bool server)
    {
        SetUp(server);
        TestInformationalResponse(true, true, NewResponse(HttpResponseStatus.OK),
                NewResponse(HttpResponseStatus.CONTINUE));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInformationalResponseAfterActualResponseClient(bool server)
    {
        SetUp(server);
        TestInformationalResponse(false, true, NewResponse(HttpResponseStatus.OK),
                NewResponse(HttpResponseStatus.CONTINUE));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestMultiInformationalResponseServer(bool server)
    {
        SetUp(server);
        TestInformationalResponse(true, false, NewResponse(HttpResponseStatus.CONTINUE),
                NewResponse(HttpResponseStatus.CONTINUE), NewResponse(HttpResponseStatus.OK));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestMultiInformationalResponseClient(bool server)
    {
        SetUp(server);
        TestInformationalResponse(false, false, NewResponse(HttpResponseStatus.CONTINUE),
                NewResponse(HttpResponseStatus.CONTINUE), NewResponse(HttpResponseStatus.OK));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestMultiInformationalResponseAfterActualResponseServer(bool server)
    {
        SetUp(server);
        TestInformationalResponse(true, false, NewResponse(HttpResponseStatus.CONTINUE),
                NewResponse(HttpResponseStatus.CONTINUE), NewResponse(HttpResponseStatus.OK));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestMultiInformationalResponseAfterActualResponseClient(bool server)
    {
        SetUp(server);
        TestInformationalResponse(false, false, NewResponse(HttpResponseStatus.CONTINUE),
                NewResponse(HttpResponseStatus.CONTINUE), NewResponse(HttpResponseStatus.OK));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInformationalResponseWithDataAndTrailersServer(bool server)
    {
        SetUp(server);
        TestInformationalResponse(true, false, NewResponse(HttpResponseStatus.CONTINUE),
                NewResponse(HttpResponseStatus.OK),
                new DefaultHttp3DataFrame(Unpooled.Buffer()),
                new DefaultHttp3HeadersFrame());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInformationalResponseWithDataAndTrailersClient(bool server)
    {
        SetUp(server);
        TestInformationalResponse(false, false, NewResponse(HttpResponseStatus.CONTINUE),
                NewResponse(HttpResponseStatus.OK),
                new DefaultHttp3DataFrame(Unpooled.Buffer()),
                new DefaultHttp3HeadersFrame());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInformationalResponseWithDataServer(bool server)
    {
        SetUp(server);
        TestInformationalResponse(true, false, NewResponse(HttpResponseStatus.CONTINUE),
                NewResponse(HttpResponseStatus.OK),
                new DefaultHttp3DataFrame(Unpooled.Buffer()));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInformationalResponseWithDataClient(bool server)
    {
        SetUp(server);
        TestInformationalResponse(false, false, NewResponse(HttpResponseStatus.CONTINUE),
                NewResponse(HttpResponseStatus.OK),
                new DefaultHttp3DataFrame(Unpooled.Buffer()));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInformationalResponsePostDataServer(bool server)
    {
        SetUp(server);
        TestInformationalResponse(true, true, NewResponse(HttpResponseStatus.OK),
                new DefaultHttp3DataFrame(Unpooled.Buffer()), NewResponse(HttpResponseStatus.CONTINUE));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInformationalResponsePostDataClient(bool server)
    {
        SetUp(server);
        TestInformationalResponse(false, true, NewResponse(HttpResponseStatus.OK),
                new DefaultHttp3DataFrame(Unpooled.Buffer()), NewResponse(HttpResponseStatus.CONTINUE));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInformationalResponsePostTrailersServer(bool server)
    {
        SetUp(server);
        TestInformationalResponse(true, true, NewResponse(HttpResponseStatus.OK),
                new DefaultHttp3DataFrame(Unpooled.Buffer()), new DefaultHttp3HeadersFrame(),
                NewResponse(HttpResponseStatus.CONTINUE));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestInformationalResponsePostTrailersClient(bool server)
    {
        SetUp(server);
        TestInformationalResponse(false, true, NewResponse(HttpResponseStatus.OK),
                new DefaultHttp3DataFrame(Unpooled.Buffer()), new DefaultHttp3HeadersFrame(),
                NewResponse(HttpResponseStatus.CONTINUE));
    }

    private void TestInformationalResponse(bool server, bool expectFail, params IHttp3Frame[] frames)
    {
        EmbeddedQuicStreamChannel channel = server ? NewServerStream() :
                NewClientStream(() => false);

        for (int i = 0; i < frames.Length; i++)
        {
            IHttp3Frame frame = frames[i];
            IHttp3Frame read = null;
            try
            {
                if (server)
                {
                    Assert.True(channel.WriteOutbound(frame));
                    if (expectFail && i == frames.Length - 1)
                    {
                        Assert.Fail();
                    }
                    else
                    {
                        read = channel.ReadOutbound<IHttp3Frame>();
                    }
                }
                else
                {
                    Assert.True(channel.WriteInbound(frame));
                    if (expectFail && i == frames.Length - 1)
                    {
                        Assert.Fail();
                    }
                    else
                    {
                        read = channel.ReadInbound<IHttp3Frame>();
                    }
                }
                Assert.Equal(frame, read, (x, y) => object.Equals(x, y));
            }
            catch (Exception e) when (!(e is IAssertionException))
            {
                AssertException(Http3ErrorCode.H3_FRAME_UNEXPECTED, e);
                if (!server)
                {
                    VerifyClose(Http3ErrorCode.H3_FRAME_UNEXPECTED, parent);
                }
            }
            finally
            {
                ReferenceCountUtil.Release(read);
            }
        }
        Assert.False(parent.Finish());
        Assert.False(channel.Finish());
    }

    private void TestHeadersFrame(IHttp3HeadersFrame headersFrame, Http3ErrorCode code)
    {
        EmbeddedQuicStreamChannel channel = NewServerStream();
        try
        {
            Assert.True(channel.WriteInbound(headersFrame));
            if (code != null)
            {
                Assert.Fail();
            }
        }
        catch (Exception cause)
        {
            if (code == null)
            {
                throw;
            }
            AssertException(code, cause);
            Assert.Equal((int?) code.code, channel.OutputShutdownError());
        }
        // Only expect produced messages when there was no error.
        Assert.Equal(code == null, channel.FinishAndReleaseAll());
    }

    private EmbeddedQuicStreamChannel NewClientStream(Func<bool> goAwayReceivedSupplier)
    {
        return NewStream(QuicStreamType.BIDIRECTIONAL, new ClientValidatorInitializer(this, goAwayReceivedSupplier));
    }

    // CLR: the anonymous ChannelInitializer of newClientStream(BooleanSupplier).
    private sealed class ClientValidatorInitializer : ChannelInitializer<IQuicStreamChannel>
    {
        private readonly Http3RequestStreamValidationHandlerTest test;
        private readonly Func<bool> goAwayReceivedSupplier;

        internal ClientValidatorInitializer(Http3RequestStreamValidationHandlerTest test,
                                            Func<bool> goAwayReceivedSupplier)
        {
            this.test = test;
            this.goAwayReceivedSupplier = goAwayReceivedSupplier;
        }

        protected override void InitChannel(IQuicStreamChannel ch)
        {
            Http3RequestStreamEncodeStateValidator encStateValidator = new Http3RequestStreamEncodeStateValidator();
            Http3RequestStreamDecodeStateValidator decStateValidator = new Http3RequestStreamDecodeStateValidator();
            ch.Pipeline.AddLast(encStateValidator);
            ch.Pipeline.AddLast(decStateValidator);
            ch.Pipeline.AddLast(Http3RequestStreamValidationHandler.NewClientValidator(goAwayReceivedSupplier,
                    test.qpackAttributes, test.decoder, encStateValidator, decStateValidator));
        }
    }

    private EmbeddedQuicStreamChannel NewServerStream()
    {
        return NewStream(QuicStreamType.BIDIRECTIONAL, NewHandler(true));
    }

    private static IHttp3Frame NewResponse(HttpResponseStatus status)
    {
        IHttp3HeadersFrame frame = new DefaultHttp3HeadersFrame();
        frame.Headers().Status(status.CodeAsText());
        return frame;
    }
}
