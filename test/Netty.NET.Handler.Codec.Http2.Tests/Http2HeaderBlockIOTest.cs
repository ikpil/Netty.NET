/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using Netty.NET.Buffer;
using Netty.NET.Common;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for encoding/decoding HTTP2 header blocks.
 */
// CLR: Header equality calls Equals directly, as Java's assertEquals calls equals (xUnit would compare the headers
// as an IEnumerable of entries; docs/codec-http-porting.md, Shared CLR adaptations).
public class Http2HeaderBlockIOTest : IDisposable
{

    private DefaultHttp2HeadersDecoder decoder;
    private DefaultHttp2HeadersEncoder encoder;
    private ByteBuf buffer;

    public Http2HeaderBlockIOTest()
    {
        Setup();
    }

    private void Setup()
    {
        encoder = new DefaultHttp2HeadersEncoder();
        decoder = new DefaultHttp2HeadersDecoder(false);
        buffer = Unpooled.Buffer();
    }

    public void Dispose()
    {
        Teardown();
    }

    private void Teardown()
    {
        buffer.Release();
    }

    [Fact]
    public void RoundtripShouldBeSuccessful()
    {
        IHttp2Headers @in = Headers();
        AssertRoundtripSuccessful(@in);
    }

    [Fact]
    public void SuccessiveCallsShouldSucceed()
    {
        IHttp2Headers @in = new DefaultHttp2Headers().Method(new AsciiString("GET")).Scheme(new AsciiString("https"))
                        .Authority(new AsciiString("example.org")).Path(new AsciiString("/some/path"))
                        .Add(new AsciiString("accept"), new AsciiString("*/*"));
        AssertRoundtripSuccessful(@in);

        @in = new DefaultHttp2Headers().Method(new AsciiString("GET")).Scheme(new AsciiString("https"))
                        .Authority(new AsciiString("example.org")).Path(new AsciiString("/some/path/resource1"))
                        .Add(new AsciiString("accept"), new AsciiString("image/jpeg"))
                        .Add(new AsciiString("cache-control"), new AsciiString("no-cache"));
        AssertRoundtripSuccessful(@in);

        @in = new DefaultHttp2Headers().Method(new AsciiString("GET")).Scheme(new AsciiString("https"))
                        .Authority(new AsciiString("example.org")).Path(new AsciiString("/some/path/resource2"))
                        .Add(new AsciiString("accept"), new AsciiString("image/png"))
                        .Add(new AsciiString("cache-control"), new AsciiString("no-cache"));
        AssertRoundtripSuccessful(@in);
    }

    [Fact]
    public void SetMaxHeaderSizeShouldBeSuccessful()
    {
        encoder.MaxHeaderTableSize(10);
        IHttp2Headers @in = Headers();
        AssertRoundtripSuccessful(@in);
        Assert.Equal(10, decoder.MaxHeaderTableSize());
    }

    private void AssertRoundtripSuccessful(IHttp2Headers @in)
    {
        encoder.EncodeHeaders(3 /* randomly chosen */, @in, buffer);

        IHttp2Headers @out = decoder.DecodeHeaders(0, buffer);
        Assert.True(@in.Equals(@out));
    }

    private static IHttp2Headers Headers()
    {
        return new DefaultHttp2Headers(false).Method(new AsciiString("GET")).Scheme(new AsciiString("https"))
        .Authority(new AsciiString("example.org")).Path(new AsciiString("/some/path/resource2"))
                .Add(new AsciiString("accept"), new AsciiString("image/png"))
                .Add(new AsciiString("cache-control"), new AsciiString("no-cache"))
                .Add(new AsciiString("custom"), new AsciiString("value1"))
                .Add(new AsciiString("custom"), new AsciiString("value2"))
                .Add(new AsciiString("custom"), new AsciiString("value3"))
                .Add(new AsciiString("custom"), new AsciiString("custom4"))
                .Add(RandomString(), RandomString());
    }
}
