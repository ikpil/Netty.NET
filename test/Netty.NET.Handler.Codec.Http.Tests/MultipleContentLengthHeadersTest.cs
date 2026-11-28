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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: IllegalArgumentException is ArgumentException, so assertInstanceOf is
// Assert.IsAssignableFrom<ArgumentException>; AssertJ's assertThat(list).contains(x) and
// assertThat(message).contains(s) are Assert.Contains.
public class MultipleContentLengthHeadersTest
{

    public static IEnumerable<object[]> Parameters()
    {
        return new List<object[]> {
                new object[] { false, false, false },
                new object[] { false, false, true },
                new object[] { false, true, false },
                new object[] { false, true, true },
                new object[] { true, false, false },
                new object[] { true, false, true },
                new object[] { true, true, false },
                new object[] { true, true, true }
        };
    }

    private static EmbeddedChannel NewChannel(bool allowDuplicateContentLengths)
    {
        HttpDecoderConfig config = new HttpDecoderConfig()
                .SetAllowDuplicateContentLengths(allowDuplicateContentLengths);
        HttpRequestDecoder decoder = new HttpRequestDecoder(config);
        return new EmbeddedChannel(decoder);
    }

    [Theory]
    [MemberData(nameof(Parameters))]
    public void TestMultipleContentLengthHeadersBehavior(bool allowDuplicateContentLengths,
                                                         bool sameValue, bool singleField)
    {
        EmbeddedChannel channel = NewChannel(allowDuplicateContentLengths);
        string requestStr = SetupRequestString(sameValue, singleField);
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();

        if (allowDuplicateContentLengths)
        {
            if (sameValue)
            {
                AssertValid(request);
                IList<string> contentLengths = request.Headers().GetAll(HttpHeaderNames.CONTENT_LENGTH);
                Assert.Contains("1", contentLengths);
                ILastHttpContent body = channel.ReadInbound<ILastHttpContent>();
                Assert.Equal(1, body.Content.ReadableBytes);
                Assert.Equal("a", body.Content.ReadCharSequence(1, Encoding.ASCII).ToString());
            }
            else
            {
                AssertInvalid(request);
            }
        }
        else
        {
            AssertInvalid(request);
        }

        Assert.False(channel.Finish());
    }

    private static string SetupRequestString(bool sameValue, bool singleField)
    {
        return SetupRequestString(sameValue, singleField, "HTTP/1.1");
    }

    private static string SetupRequestString(bool sameValue, bool singleField, string version)
    {
        string firstValue = "1";
        string secondValue = sameValue ? firstValue : "2";
        string contentLength;

        if (singleField)
        {
            contentLength = "Content-Length: " + firstValue + ", " + secondValue + "\r\n\r\n";
        }
        else
        {
            contentLength = "Content-Length: " + firstValue + "\r\n" +
                            "Content-Length: " + secondValue + "\r\n\r\n";
        }

        return "PUT /some/path " + version + "\r\n" +
               contentLength +
               "ab";
    }

    [Fact]
    public void TestDanglingComma()
    {
        EmbeddedChannel channel = NewChannel(false);
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                            "Content-Length: 1,\r\n" +
                            "Connection: close\r\n\r\n" +
                            "ab";
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        AssertInvalid(request);
        Assert.False(channel.Finish());
    }

    [Theory]
    [InlineData("HTTP/1.0")]
    [InlineData("HTTP/0.9")]
    public void TestMultipleContentLengthHeadersHttp10(string version)
    {
        EmbeddedChannel channel = NewChannel(false);
        string requestStr = SetupRequestString(false, false, version);
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        AssertInvalid(request);
        Assert.Null(channel.ReadInbound());
        Assert.False(channel.Finish());
    }

    private static void AssertValid(IHttpRequest request)
    {
        Assert.False(request.DecoderResult().IsFailure());
    }

    private static void AssertInvalid(IHttpRequest request)
    {
        Assert.True(request.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<ArgumentException>(request.DecoderResult().Cause());
        Assert.Contains("Multiple Content-Length values found", request.DecoderResult().Cause().Message);
    }
}
