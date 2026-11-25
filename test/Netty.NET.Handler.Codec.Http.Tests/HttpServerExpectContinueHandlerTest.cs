/*
 * Copyright 2017 The Netty Project
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

using Netty.NET.Common;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: the anonymous HttpServerExpectContinueHandler subclasses are the named nested classes CustomAcceptHandler and
// RejectingHandler. Messages are read with ReadInbound<T>/ReadOutbound<T>, and inboundMessages() is the
// InboundMessages() queue of the Transport EmbeddedChannel (its Count is 0 when the request was swallowed).
public class HttpServerExpectContinueHandlerTest
{

    [Fact]
    public void ShouldRespondToExpectedHeader()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new CustomAcceptHandler());
        IHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");
        HttpUtil.Set100ContinueExpected(request, true);

        channel.WriteInbound(request);
        IHttpResponse response = channel.ReadOutbound<IHttpResponse>();

        Assert.Equal(HttpResponseStatus.CONTINUE, response.Status());
        Assert.Equal("bar", response.Headers().Get("foo"));
        ReferenceCountUtil.Release(response);

        IHttpRequest processedRequest = channel.ReadInbound<IHttpRequest>();
        Assert.False(processedRequest.Headers().Contains(HttpHeaderNames.EXPECT));
        ReferenceCountUtil.Release(processedRequest);
        Assert.False(channel.FinishAndReleaseAll());
    }

    private sealed class CustomAcceptHandler : HttpServerExpectContinueHandler
    {
        protected override IHttpResponse AcceptMessage(IHttpRequest request)
        {
            IHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.CONTINUE);
            response.Headers().Set("foo", "bar");
            return response;
        }
    }

    [Fact]
    public void ShouldAllowCustomResponses()
    {
        EmbeddedChannel channel = new EmbeddedChannel(
            new RejectingHandler()
        );

        IHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");
        HttpUtil.Set100ContinueExpected(request, true);

        channel.WriteInbound(request);
        IHttpResponse response = channel.ReadOutbound<IHttpResponse>();

        Assert.Equal(HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, response.Status());
        ReferenceCountUtil.Release(response);

        // request was swallowed
        Assert.True(channel.InboundMessages().Count == 0);
        Assert.False(channel.FinishAndReleaseAll());
    }

    private sealed class RejectingHandler : HttpServerExpectContinueHandler
    {
        protected override IHttpResponse AcceptMessage(IHttpRequest request)
        {
            return null;
        }

        protected override IHttpResponse RejectResponse(IHttpRequest request)
        {
            return new DefaultFullHttpResponse(HttpVersion.HTTP_1_1,
                    HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE);
        }
    }
}
