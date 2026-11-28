/*
 * Copyright 2012 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: CharsetUtil.UTF_8 is Encoding.UTF8 (docs/codec-base-porting.md). The random traffic of
// ensureInboundTrafficDiscarded comes from PlatformDependent.splittableRandomNextBytes over a SplittableRandom seeded
// by java.util.Random; only its size matters (it is discarded), so it is System.Random.NextBytes of the instance field
// rnd (rule 9: java.util.Random sequences are not reproduced). DecoderResult is the codec-base type.
public class HttpInvalidMessageTest
{

    private readonly Random rnd = new Random();

    [Fact]
    public void TestRequestWithBadInitialLine()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpRequestDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer("GET / HTTP/1.0 with extra\r\n", Encoding.UTF8));
        IHttpRequest req = ch.ReadInbound<IHttpRequest>();
        DecoderResult dr = req.DecoderResult();
        Assert.False(dr.IsSuccess());
        Assert.True(dr.IsFailure());
        EnsureInboundTrafficDiscarded(ch);
    }

    [Fact]
    public void TestRequestWithBadHeader()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpRequestDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer("GET /maybe-something HTTP/1.0\r\n", Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer("Good_Name: Good Value\r\n", Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer("Bad=Name: Bad Value\r\n", Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.UTF8));
        IHttpRequest req = ch.ReadInbound<IHttpRequest>();
        DecoderResult dr = req.DecoderResult();
        Assert.False(dr.IsSuccess());
        Assert.True(dr.IsFailure());
        Assert.Equal("Good Value", req.Headers().Get(Of("Good_Name")));
        Assert.Equal("/maybe-something", req.Uri());
        EnsureInboundTrafficDiscarded(ch);
    }

    [Fact]
    public void TestResponseWithBadInitialLine()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer("HTTP/1.0 BAD_CODE Bad Server\r\n", Encoding.UTF8));
        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        DecoderResult dr = res.DecoderResult();
        Assert.False(dr.IsSuccess());
        Assert.True(dr.IsFailure());
        EnsureInboundTrafficDiscarded(ch);
    }

    [Fact]
    public void TestResponseWithBadHeader()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer("HTTP/1.0 200 Maybe OK\r\n", Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer("Good_Name: Good Value\r\n", Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer("Bad=Name: Bad Value\r\n", Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.UTF8));
        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        DecoderResult dr = res.DecoderResult();
        Assert.False(dr.IsSuccess());
        Assert.True(dr.IsFailure());
        Assert.Equal("Maybe OK", res.Status().ReasonPhrase());
        Assert.Equal("Good Value", res.Headers().Get(Of("Good_Name")));
        EnsureInboundTrafficDiscarded(ch);
    }

    [Fact]
    public void TestBadChunk()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpRequestDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer("GET / HTTP/1.1\r\n", Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer("Transfer-Encoding: chunked\r\n\r\n", Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer("BAD_LENGTH\r\n", Encoding.UTF8));

        IHttpRequest req = ch.ReadInbound<IHttpRequest>();
        Assert.True(req.DecoderResult().IsSuccess());

        ILastHttpContent chunk = ch.ReadInbound<ILastHttpContent>();
        DecoderResult dr = chunk.DecoderResult();
        Assert.False(dr.IsSuccess());
        Assert.True(dr.IsFailure());
        EnsureInboundTrafficDiscarded(ch);
    }

    private void EnsureInboundTrafficDiscarded(EmbeddedChannel ch)
    {
        // Generate a lot of random traffic to ensure that it's discarded silently.
        byte[] data = new byte[1048576];
        rnd.NextBytes(data);

        ByteBuf buf = Unpooled.WrappedBuffer(data);

        for (int i = 0; i < 4096; i ++)
        {
            buf.SetIndex(0, data.Length);
            ch.WriteInbound(buf.Retain());
            ch.CheckException();

            Assert.Null(ch.ReadInbound());
        }

        buf.Release();
    }
}
