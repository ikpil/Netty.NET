/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpUtil;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

#pragma warning disable xUnit1051 // The source's writeAndFlush(...) observes no test cancellation.

// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setUp() runs from the
// constructor (https://xunit.net/docs/shared-context). @ParameterizedTest @MethodSource is a [Theory] with
// MemberData rows, and the static imports HttpHeaderValues.CLOSE/KEEP_ALIVE/MULTIPART_MIXED are written in full
// because HttpUtil (also imported) has members named like them. writeAndFlush(msg) is WriteAndFlushAsync(msg)
// whose Task is discarded, as the source discards the ChannelFuture; there is no void promise, so
// writeAndFlush(msg, channel.voidPromise()) is the same discarded call. Messages are read with
// ReadInbound<T>/ReadOutbound<T>; assertions with messages use XunitAssertMessages.AssertM. String.toUpperCase of
// the AsciiString is AsciiString.ToUpperCase().
public class HttpServerKeepAliveHandlerTest
{
    private const string REQUEST_KEEP_ALIVE = "REQUEST_KEEP_ALIVE";
    private const int NOT_SELF_DEFINED_MSG_LENGTH = 0;
    private const int SET_RESPONSE_LENGTH = 1;
    private const int SET_MULTIPART = 2;
    private const int SET_CHUNKED = 4;

    private EmbeddedChannel channel;

    public HttpServerKeepAliveHandlerTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        channel = new EmbeddedChannel(new HttpServerKeepAliveHandler());
    }

    public static IEnumerable<object[]> KeepAliveProvider()
    {
        return new object[][] {
                new object[] { true, HttpVersion.HTTP_1_0, OK, REQUEST_KEEP_ALIVE, SET_RESPONSE_LENGTH, HttpHeaderValues.KEEP_ALIVE },          //  0
                new object[] { true, HttpVersion.HTTP_1_0, OK, REQUEST_KEEP_ALIVE, SET_MULTIPART, HttpHeaderValues.KEEP_ALIVE },                //  1
                new object[] { false, HttpVersion.HTTP_1_0, OK, null, SET_RESPONSE_LENGTH, null },                             //  2
                new object[] { true, HttpVersion.HTTP_1_1, OK, REQUEST_KEEP_ALIVE, SET_RESPONSE_LENGTH, null },                //  3
                new object[] { false, HttpVersion.HTTP_1_1, OK, REQUEST_KEEP_ALIVE, SET_RESPONSE_LENGTH, HttpHeaderValues.CLOSE },              //  4
                new object[] { true, HttpVersion.HTTP_1_1, OK, REQUEST_KEEP_ALIVE, SET_MULTIPART, null },                      //  5
                new object[] { true, HttpVersion.HTTP_1_1, OK, REQUEST_KEEP_ALIVE, SET_CHUNKED, null },                        //  6
                new object[] { false, HttpVersion.HTTP_1_1, OK, null, SET_RESPONSE_LENGTH, null },                             //  7
                new object[] { false, HttpVersion.HTTP_1_0, OK, REQUEST_KEEP_ALIVE, NOT_SELF_DEFINED_MSG_LENGTH, null },       //  8
                new object[] { false, HttpVersion.HTTP_1_0, OK, null, NOT_SELF_DEFINED_MSG_LENGTH, null },                     //  9
                new object[] { false, HttpVersion.HTTP_1_1, OK, REQUEST_KEEP_ALIVE, NOT_SELF_DEFINED_MSG_LENGTH, null },       // 10
                new object[] { false, HttpVersion.HTTP_1_1, OK, null, NOT_SELF_DEFINED_MSG_LENGTH, null },                     // 11
                new object[] { false, HttpVersion.HTTP_1_0, OK, REQUEST_KEEP_ALIVE, SET_RESPONSE_LENGTH, null },               // 12
                new object[] { true, HttpVersion.HTTP_1_1, NO_CONTENT, REQUEST_KEEP_ALIVE, NOT_SELF_DEFINED_MSG_LENGTH, null}, // 13
                new object[] { false, HttpVersion.HTTP_1_0, NO_CONTENT, null, NOT_SELF_DEFINED_MSG_LENGTH, null}               // 14
        };
    }

    [Theory]
    [MemberData(nameof(KeepAliveProvider))]
    public void Test_KeepAlive(bool isKeepAliveResponseExpected, HttpVersion httpVersion,
                               HttpResponseStatus responseStatus,
                               string sendKeepAlive, int setSelfDefinedMessageLength,
                               AsciiString setResponseConnection)
    {
        IFullHttpRequest request = new DefaultFullHttpRequest(httpVersion, HttpMethod.GET, "/v1/foo/bar");
        SetKeepAlive(request, REQUEST_KEEP_ALIVE.Equals(sendKeepAlive));
        IHttpResponse response = new DefaultFullHttpResponse(httpVersion, responseStatus);

        if (setResponseConnection != null)
        {
            response.Headers().Set(HttpHeaderNames.CONNECTION, setResponseConnection);
        }

        SetupMessageLength(response, setSelfDefinedMessageLength);

        Assert.True(channel.WriteInbound(request));
        object requestForwarded = channel.ReadInbound();
        Assert.Equal(request, requestForwarded);
        ReferenceCountUtil.Release(requestForwarded);
        _ = channel.WriteAndFlushAsync(response);
        IHttpResponse writtenResponse = channel.ReadOutbound<IHttpResponse>();

        Assert.Equal(isKeepAliveResponseExpected, channel.IsOpen, "channel.isOpen");
        Assert.Equal(isKeepAliveResponseExpected, IsKeepAlive(writtenResponse), "response keep-alive");
        ReferenceCountUtil.Release(writtenResponse);
        Assert.False(channel.FinishAndReleaseAll());
    }

    public static IEnumerable<object[]> ConnectionCloseProvider()
    {
        return new object[][] {
                new object[] { HttpVersion.HTTP_1_0, OK, SET_RESPONSE_LENGTH },
                new object[] { HttpVersion.HTTP_1_0, OK, SET_MULTIPART },
                new object[] { HttpVersion.HTTP_1_0, OK, NOT_SELF_DEFINED_MSG_LENGTH },
                new object[] { HttpVersion.HTTP_1_0, NO_CONTENT, NOT_SELF_DEFINED_MSG_LENGTH },
                new object[] { HttpVersion.HTTP_1_1, OK, SET_RESPONSE_LENGTH },
                new object[] { HttpVersion.HTTP_1_1, OK, SET_MULTIPART },
                new object[] { HttpVersion.HTTP_1_1, OK, NOT_SELF_DEFINED_MSG_LENGTH },
                new object[] { HttpVersion.HTTP_1_1, OK, SET_CHUNKED },
                new object[] { HttpVersion.HTTP_1_1, NO_CONTENT, NOT_SELF_DEFINED_MSG_LENGTH }
        };
    }

    [Theory]
    [MemberData(nameof(ConnectionCloseProvider))]
    public void TestConnectionCloseHeaderHandledCorrectly(
            HttpVersion httpVersion, HttpResponseStatus responseStatus, int setSelfDefinedMessageLength)
    {
        IHttpResponse response = new DefaultFullHttpResponse(httpVersion, responseStatus);
        response.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.CLOSE);
        SetupMessageLength(response, setSelfDefinedMessageLength);

        _ = channel.WriteAndFlushAsync(response);
        IHttpResponse writtenResponse = channel.ReadOutbound<IHttpResponse>();

        Assert.False(channel.IsOpen);
        ReferenceCountUtil.Release(writtenResponse);
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Theory]
    [MemberData(nameof(ConnectionCloseProvider))]
    public void TestConnectionCloseHeaderHandledCorrectlyForVoidPromise(
            HttpVersion httpVersion, HttpResponseStatus responseStatus, int setSelfDefinedMessageLength)
    {
        IHttpResponse response = new DefaultFullHttpResponse(httpVersion, responseStatus);
        response.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.CLOSE);
        SetupMessageLength(response, setSelfDefinedMessageLength);

        // CLR: writeAndFlush(response, channel.voidPromise()): the C# counterpart of a void promise is a write whose
        // Task nobody observes (see the class comment).
        _ = channel.WriteAndFlushAsync(response);
        IHttpResponse writtenResponse = channel.ReadOutbound<IHttpResponse>();

        Assert.False(channel.IsOpen);
        ReferenceCountUtil.Release(writtenResponse);
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Theory]
    [MemberData(nameof(KeepAliveProvider))]
    public void TestPipelineKeepAlive(bool isKeepAliveResponseExpected, HttpVersion httpVersion,
                                       HttpResponseStatus responseStatus,
                                       string sendKeepAlive, int setSelfDefinedMessageLength,
                                       AsciiString setResponseConnection)
    {
        IFullHttpRequest firstRequest = new DefaultFullHttpRequest(httpVersion, HttpMethod.GET, "/v1/foo/bar");
        SetKeepAlive(firstRequest, true);
        IFullHttpRequest secondRequest = new DefaultFullHttpRequest(httpVersion, HttpMethod.GET, "/v1/foo/bar");
        SetKeepAlive(secondRequest, REQUEST_KEEP_ALIVE.Equals(sendKeepAlive));
        IFullHttpRequest finalRequest = new DefaultFullHttpRequest(httpVersion, HttpMethod.GET, "/v1/foo/bar");
        SetKeepAlive(finalRequest, false);
        IFullHttpResponse response = new DefaultFullHttpResponse(httpVersion, responseStatus);
        IFullHttpResponse informationalResp = new DefaultFullHttpResponse(httpVersion, HttpResponseStatus.PROCESSING);
        SetKeepAlive(response, true);
        SetContentLength(response, 0);
        SetKeepAlive(informationalResp, true);

        Assert.True(channel.WriteInbound(firstRequest, secondRequest, finalRequest));

        object requestForwarded = channel.ReadInbound();
        Assert.Equal(firstRequest, requestForwarded);
        ReferenceCountUtil.Release(requestForwarded);

        _ = channel.WriteAndFlushAsync(response.RetainedDuplicate());
        IHttpResponse firstResponse = channel.ReadOutbound<IHttpResponse>();
        Assert.True(channel.IsOpen, "channel.isOpen");
        Assert.True(IsKeepAlive(firstResponse), "response keep-alive");
        ReferenceCountUtil.Release(firstResponse);

        requestForwarded = channel.ReadInbound();
        Assert.Equal(secondRequest, requestForwarded);
        ReferenceCountUtil.Release(requestForwarded);

        _ = channel.WriteAndFlushAsync(informationalResp);
        IHttpResponse writtenInfoResp = channel.ReadOutbound<IHttpResponse>();
        Assert.True(channel.IsOpen, "channel.isOpen");
        Assert.True(IsKeepAlive(writtenInfoResp), "response keep-alive");
        ReferenceCountUtil.Release(writtenInfoResp);

        if (setResponseConnection != null)
        {
            response.Headers().Set(HttpHeaderNames.CONNECTION, setResponseConnection);
        }
        else
        {
            response.Headers().Remove(HttpHeaderNames.CONNECTION);
        }

        SetupMessageLength(response, setSelfDefinedMessageLength);
        _ = channel.WriteAndFlushAsync(response.RetainedDuplicate());
        IHttpResponse secondResponse = channel.ReadOutbound<IHttpResponse>();
        Assert.Equal(isKeepAliveResponseExpected, channel.IsOpen, "channel.isOpen");
        Assert.Equal(isKeepAliveResponseExpected, IsKeepAlive(secondResponse), "response keep-alive");
        ReferenceCountUtil.Release(secondResponse);

        requestForwarded = channel.ReadInbound();
        Assert.Equal(finalRequest, requestForwarded);
        ReferenceCountUtil.Release(requestForwarded);

        if (isKeepAliveResponseExpected)
        {
            _ = channel.WriteAndFlushAsync(response);
            IHttpResponse finalResponse = channel.ReadOutbound<IHttpResponse>();
            Assert.False(channel.IsOpen, "channel.isOpen");
            Assert.False(IsKeepAlive(finalResponse), "response keep-alive");
        }

        ReferenceCountUtil.Release(response);
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestConnectionClosedWhenBothTransferEncodingAndContentLengthRfc7230()
    {
        EmbeddedChannel ch = new EmbeddedChannel(
                new HttpRequestDecoder(new HttpDecoderConfig().SetUseRfc9112TransferEncoding(false)),
                new HttpServerKeepAliveHandler());

        string requestStr = "POST / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Content-Length: 5\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";

        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.False(HttpUtil.IsKeepAlive(request));
        ILastHttpContent content = ch.ReadInbound<ILastHttpContent>();
        ReferenceCountUtil.Release(content);

        IFullHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        SetContentLength(response, 0);

        _ = ch.WriteAndFlushAsync(response);
        IHttpResponse writtenResponse = ch.ReadOutbound<IHttpResponse>();

        Assert.False(IsKeepAlive(writtenResponse));
        Assert.False(ch.IsOpen);
        ReferenceCountUtil.Release(writtenResponse);
        Assert.False(ch.FinishAndReleaseAll());
    }

    private static void SetupMessageLength(IHttpResponse response, int setSelfDefinedMessageLength)
    {
        switch (setSelfDefinedMessageLength)
        {
        case NOT_SELF_DEFINED_MSG_LENGTH:
            if (IsContentLengthSet(response))
            {
                response.Headers().Remove(HttpHeaderNames.CONTENT_LENGTH);
            }

            break;
        case SET_RESPONSE_LENGTH:
            SetContentLength(response, 0);
            break;
        case SET_CHUNKED:
            SetTransferEncodingChunked(response, true);
            break;
        case SET_MULTIPART:
            response.Headers().Set(HttpHeaderNames.CONTENT_TYPE, HttpHeaderValues.MULTIPART_MIXED.ToUpperCase());
            break;
        default:
            throw new ArgumentException("selfDefinedMessageLength: " + setSelfDefinedMessageLength);
        }
    }
}
