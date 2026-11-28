/*
 * Copyright 2026 The Netty Project
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

using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Spdy;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;

namespace Netty.NET.Handler.Codec.Http.Tests.Spdy;

// CLR: the protected SpdyHttpDecoder constructor is protected internal (Java protected also admits the package) and is
// reached through InternalsVisibleTo. HashMap<Integer, FullHttpMessage> is Dictionary<int, IFullHttpMessage> and
// containsKey is ContainsKey. assertThrows, which also accepts a subclass, is Assert.ThrowsAny; the decoder throws
// TooLongHttpHeaderException, a TooLongFrameException. The Strings passed as CharSequence header names and values are
// wrapped by the core test helper HttpHeadersTestUtils.Of (StringCharSequence), and the char concatenation "x-pad-" +
// frame + '-' + i uses the invariant culture for the numbers (non-negative ints, so the text is the Java text).
// instanceof is is.
public class SpdyHttpDecoderTest
{

    /**
     * A non-final SYN_STREAM buffers a request while its body/trailers arrive later. Prior to the
     * fix, an attacker could keep the stream open and send an unbounded number of non-final HEADERS
     * frames, each appended verbatim to the buffered message with no cumulative cap, exhausting the
     * heap. The decoder must bound the total accumulated header size, mirroring the cap HTTP/2 places
     * on its decoded header list (see HpackDecoder/Http2CodecUtil#headerListSizeExceeded).
     */
    [Fact]
    public void TestUnboundedHeaderAccumulationIsRejected()
    {
        int maxHeadersSize = 8192;
        int maxContentLength = 8192;
        Dictionary<int, IFullHttpMessage> messageMap = new Dictionary<int, IFullHttpMessage>();
        EmbeddedChannel ch = new EmbeddedChannel(
            new SpdyHttpDecoder(SpdyVersion.SPDY_3_1, maxHeadersSize, maxContentLength, messageMap,
                DefaultHttpHeadersFactory.HeadersFactory(), DefaultHttpHeadersFactory.TrailersFactory()));

        ISpdySynStreamFrame synStream = new DefaultSpdySynStreamFrame(1, 0, (byte) 0);
        synStream.SetLast(false);
        synStream.Headers().Set(Of(":method"), Of("GET"));
        synStream.Headers().Set(Of(":path"), Of("/"));
        synStream.Headers().Set(Of(":version"), Of("HTTP/1.1"));
        synStream.Headers().Set(Of(":scheme"), Of("https"));
        synStream.Headers().Set(Of(":host"), Of("netty.io"));
        Assert.False(ch.WriteInbound(synStream));

        Assert.True(messageMap.ContainsKey(1));

        try
        {
            Assert.ThrowsAny<TooLongFrameException>(() =>
            {
                for (int frame = 0; frame < 1000; frame++)
                {
                    ISpdyHeadersFrame headers = new DefaultSpdyHeadersFrame(1);
                    headers.SetLast(false);

                    for (int i = 0; i < 100; i++)
                    {
                        headers.Headers().Add(Of("x-pad-" + frame.ToString(CultureInfo.InvariantCulture)
                                + '-' + i.ToString(CultureInfo.InvariantCulture)), Of("v"));
                    }

                    ch.WriteInbound(headers);
                }
            });

            // The buffered message must be removed and released once the cumulative header
            // size guard trips, instead of being retained for the lifetime of the connection.
            Assert.False(messageMap.ContainsKey(1));
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestHeadersWithinLimitAreAccepted()
    {
        int maxHeadersSize = 8192;
        int maxContentLength = 8192;
        EmbeddedChannel ch = new EmbeddedChannel(
                new SpdyHttpDecoder(SpdyVersion.SPDY_3_1, maxHeadersSize, maxContentLength,
                    new Dictionary<int, IFullHttpMessage>(),
                    DefaultHttpHeadersFactory.HeadersFactory(), DefaultHttpHeadersFactory.TrailersFactory()));

        ISpdySynStreamFrame synStream = new DefaultSpdySynStreamFrame(1, 0, (byte) 0);
        synStream.SetLast(false);
        synStream.Headers().Set(Of(":method"), Of("GET"));
        synStream.Headers().Set(Of(":path"), Of("/"));
        synStream.Headers().Set(Of(":version"), Of("HTTP/1.1"));
        synStream.Headers().Set(Of(":scheme"), Of("https"));
        synStream.Headers().Set(Of(":host"), Of("netty.io"));
        Assert.False(ch.WriteInbound(synStream));

        ISpdyHeadersFrame headers = new DefaultSpdyHeadersFrame(1);
        headers.SetLast(true);
        headers.Headers().Add(Of("x-small"), Of("value"));
        Assert.True(ch.WriteInbound(headers));

        object decoded = ch.ReadInbound<object>();
        Assert.True(decoded is IFullHttpMessage);
        ((IFullHttpMessage) decoded).Release();

        ch.FinishAndReleaseAll();
    }
}
