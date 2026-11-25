/*
 * Copyright 2021 The Netty Project
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

using Netty.NET.Handler.Codec.Compression;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

#pragma warning disable xUnit1051 // ch.close() of the source observes no test cancellation.

// CLR: @EnabledIf("isBrotiAvailable") has no xUnit counterpart and needs none: the codec-compression Brotli.IsAvailable()
// is always true (the BCL BrotliEncoder/BrotliDecoder, docs/codec-compression-porting.md), so every test runs, and the
// helper keeps its Java name. StandardCompressionOptions.zstd() returns ZstdOptions, which needs zstd-jni and is
// library-not-ported (owner decision 2026-11-23; docs/codec-compression-porting.md), so the brotli and snappy tests
// configure the other four options; their expectations do not involve zstd and are unchanged.
// testGetZstdTargetContentEncoding only exercises the zstd option and is CLR-inapplicable (recorded in the manifest).
// determineEncoding(String) is the protected DetermineEncoding, reached through the nested TestCompressor (Java tests
// call it through package access, as handler's IpSubnetFilter tests reach accept(...); docs/handler-porting.md).
// assertInstanceOf is Assert.IsAssignableFrom, AssertJ's isNotInstanceOf is Assert.False(o is IX), and
// ch.close().isSuccess() is CloseAsync().IsCompletedSuccessfully (the EmbeddedChannel close completes inline).
public class HttpContentCompressorOptionsTest
{

    internal static bool IsBrotiAvailable()
    {
        return Brotli.IsAvailable();
    }

    // CLR: exposes the protected determineEncoding(String) to the tests.
    private sealed class TestCompressor : HttpContentCompressor
    {
        internal TestCompressor(params ICompressionOptions[] compressionOptions) : base(compressionOptions)
        {
        }

        internal string InvokeDetermineEncoding(string acceptEncoding)
        {
            return DetermineEncoding(acceptEncoding);
        }
    }

    [Fact]
    public void TestGetBrTargetContentEncoding()
    {
        TestCompressor compressor = new TestCompressor(
            StandardCompressionOptions.Gzip(),
            StandardCompressionOptions.Deflate(),
            StandardCompressionOptions.Brotli(),
            StandardCompressionOptions.Snappy()
        );

        string[] tests = {
                // Accept-Encoding -> Content-Encoding
                "", null,
                "*", "br",
                "*;q=0.0", null,
                "br", "br",
                "compress, br;q=0.5", "br",
                "br; q=0.5, identity", "br",
                "br; q=0, deflate", "br",
        };
        for (int i = 0; i < tests.Length; i += 2)
        {
            string acceptEncoding = tests[i];
            string contentEncoding = tests[i + 1];
            string targetEncoding = compressor.InvokeDetermineEncoding(acceptEncoding);
            Assert.Equal(contentEncoding, targetEncoding);
        }
    }

    [Fact]
    public void TestGetSnappyTargetContentEncoding()
    {
        TestCompressor compressor = new TestCompressor(
                StandardCompressionOptions.Gzip(),
                StandardCompressionOptions.Deflate(),
                StandardCompressionOptions.Brotli(),
                StandardCompressionOptions.Snappy()
        );

        string[] tests = {
                // Accept-Encoding -> Content-Encoding
                "", null,
                "*;q=0.0", null,
                "snappy", "snappy",
                "compress, snappy;q=0.5", "snappy",
                "snappy; q=0.5, identity", "snappy",
                "snappy; q=0, deflate", "snappy",
        };
        for (int i = 0; i < tests.Length; i += 2)
        {
            string acceptEncoding = tests[i];
            string contentEncoding = tests[i + 1];
            string targetEncoding = compressor.InvokeDetermineEncoding(acceptEncoding);
            Assert.Equal(contentEncoding, targetEncoding);
        }
    }

    [Fact]
    public void TestAcceptEncodingHttpRequest()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor((ICompressionOptions[]) null));
        ch.WriteInbound(NewRequest());
        IFullHttpRequest fullHttpRequest = ch.ReadInbound<IFullHttpRequest>();
        fullHttpRequest.Release();

        IHttpResponse res = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        ch.WriteOutbound(res);

        AssertEncodedResponse(ch);

        Assert.True(ch.CloseAsync().IsCompletedSuccessfully);
    }

    private static void AssertEncodedResponse(EmbeddedChannel ch)
    {
        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IHttpResponse>(o);

        AssertEncodedResponse((IHttpResponse) o);
    }

    private static void AssertEncodedResponse(IHttpResponse res)
    {
        Assert.False(res is IHttpContent);
        Assert.Equal("chunked", res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.Equal("br", res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
    }

    private static IFullHttpRequest NewRequest()
    {
        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");
        req.Headers().Set(HttpHeaderNames.ACCEPT_ENCODING, "br, zstd, snappy, gzip, deflate");
        return req;
    }
}
