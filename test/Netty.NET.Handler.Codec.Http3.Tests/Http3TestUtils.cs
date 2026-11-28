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
using System.Linq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: Hamcrest's assertThat(e, instanceOf(T.class)) is Assert.IsAssignableFrom<T> and errorCode() is ErrorCode().
// The stream filter count of verifyClose is Enumerable.Count over CloseErrorCodes(), compared as a long, as
// assertEquals(int, long, String) widens the int; expectedCode.code is the internal field. assertEquals of two
// ByteBufs is Assert.Equal; assertEquals of two frames is Assert.Equal with object.Equals as the comparer, because
// Java calls equals and xUnit would compare an enumerable frame (IHttp3SettingsFrame) element by element.
// assertSame is Assert.Same, and ReferenceCounted is IReferenceCounted with the RefCnt property. The lambdas () ->
// 0 of the functional frame interfaces are private nested classes whose Type() returns 0. A Java String passed as
// a CharSequence (Http3Headers.add) is a StringCharSequence, because a C# string is not an ICharSequence.
internal static class Http3TestUtils
{

    internal static void AssertException(Http3ErrorCode code, Exception e)
    {
        Assert.IsAssignableFrom<Http3Exception>(e);
        Http3Exception exception = (Http3Exception) e;
        Assert.Equal(code, exception.ErrorCode());
    }

    internal static void VerifyClose(int times, Http3ErrorCode expectedCode, EmbeddedQuicChannel channel)
    {
        Assert.Equal((long) times, channel.CloseErrorCodes().Count(integer => integer == expectedCode.code),
                "Close not invoked with expected times with error code: " + expectedCode.code);
    }

    internal static void VerifyClose(Http3ErrorCode expectedCode, EmbeddedQuicChannel channel)
    {
        VerifyClose(1, expectedCode, channel);
    }

    internal static void AssertBufferEquals(ByteBuf expected, ByteBuf actual)
    {
        try
        {
            Assert.Equal(expected, actual);
        }
        finally
        {
            ReferenceCountUtil.Release(expected);
            ReferenceCountUtil.Release(actual);
        }
    }

    internal static void AssertFrameEquals(IHttp3Frame expected, IHttp3Frame actual)
    {
        try
        {
            Assert.Equal(expected, actual, (x, y) => object.Equals(x, y));
        }
        finally
        {
            ReferenceCountUtil.Release(expected);
            ReferenceCountUtil.Release(actual);
        }
    }

    internal static void AssertFrameSame(IHttp3Frame expected, IHttp3Frame actual)
    {
        try
        {
            Assert.Same(expected, actual);
        }
        finally
        {
            // as both frames are the same we only want to release once.
            ReferenceCountUtil.Release(actual);
        }
    }

    internal static void AssertFrameReleased(IHttp3Frame frame)
    {
        if (frame is IReferenceCounted)
        {
            Assert.Equal(0, ((IReferenceCounted) frame).RefCnt);
        }
    }

    internal static IHttp3Frame NewHttp3Frame()
    {
        return new Http3Frame();
    }

    internal static IHttp3PushStreamFrame NewHttp3PushStreamFrame()
    {
        return new Http3PushStreamFrame();
    }

    internal static IHttp3RequestStreamFrame NewHttp3RequestStreamFrame()
    {
        return new Http3RequestStreamFrame();
    }

    internal static IHttp3ControlStreamFrame NewHttp3ControlStreamFrame()
    {
        return new Http3ControlStreamFrame();
    }

    internal static DefaultHttp3HeadersFrame NewHeadersFrameWithPseudoHeaders()
    {
        DefaultHttp3HeadersFrame headers = new DefaultHttp3HeadersFrame();
        headers.Headers().Add(Cs(":authority"), Cs("netty.quic")); // name only
        headers.Headers().Add(Cs(":path"), Cs("/")); // name & value
        headers.Headers().Add(Cs(":method"), Cs("GET")); // name & value with few options per name
        headers.Headers().Add(Cs(":scheme"), Cs("https"));
        return headers;
    }

    internal static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }

    // CLR: the lambda () -> 0 of newHttp3Frame().
    private sealed class Http3Frame : IHttp3Frame
    {
        public long Type()
        {
            return 0;
        }
    }

    // CLR: the lambda () -> 0 of newHttp3PushStreamFrame().
    private sealed class Http3PushStreamFrame : IHttp3PushStreamFrame
    {
        public long Type()
        {
            return 0;
        }
    }

    // CLR: the lambda () -> 0 of newHttp3RequestStreamFrame().
    private sealed class Http3RequestStreamFrame : IHttp3RequestStreamFrame
    {
        public long Type()
        {
            return 0;
        }
    }

    // CLR: the lambda () -> 0 of newHttp3ControlStreamFrame().
    private sealed class Http3ControlStreamFrame : IHttp3ControlStreamFrame
    {
        public long Type()
        {
            return 0;
        }
    }
}
