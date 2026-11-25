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

using Netty.NET.Handler.Codec.Http.WebSocketx;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: getMessage() is the Message property, and HttpRequest/HttpResponse are the I-prefixed core interfaces.
// HttpHeaders is an IEnumerable of its entries, so xUnit's Assert.Equal would compare it element by element; the header
// equality assertions call Equals, as Java's assertEquals does (core's DefaultHttpHeadersTest does the same).
public class WebSocketHandshakeExceptionTest
{

    [Fact]
    public void TestClientExceptionWithoutResponse()
    {
        WebSocketClientHandshakeException clientException = new WebSocketClientHandshakeException("client message");

        Assert.Null(clientException.Response());
        Assert.Equal("client message", clientException.Message);
    }

    [Fact]
    public void TestClientExceptionWithResponse()
    {
        IHttpResponse httpResponse = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.BAD_REQUEST);
        httpResponse.Headers().Set("x-header", "x-value");
        WebSocketClientHandshakeException clientException = new WebSocketClientHandshakeException("client message",
                                                                                                  httpResponse);

        Assert.NotNull(clientException.Response());
        Assert.Equal("client message", clientException.Message);
        Assert.Equal(HttpResponseStatus.BAD_REQUEST, clientException.Response().Status());
        Assert.True(httpResponse.Headers().Equals(clientException.Response().Headers()));
    }

    [Fact]
    public void TestServerExceptionWithoutRequest()
    {
        WebSocketServerHandshakeException serverException = new WebSocketServerHandshakeException("server message");

        Assert.Null(serverException.Request());
        Assert.Equal("server message", serverException.Message);
    }

    [Fact]
    public void TestClientExceptionWithRequest()
    {
        IHttpRequest httpRequest = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                                                         "ws://localhost:9999/ws");
        httpRequest.Headers().Set("x-header", "x-value");
        WebSocketServerHandshakeException serverException = new WebSocketServerHandshakeException("server message",
                                                                                                  httpRequest);

        Assert.NotNull(serverException.Request());
        Assert.Equal("server message", serverException.Message);
        Assert.Equal(HttpMethod.GET, serverException.Request().Method());
        Assert.True(httpRequest.Headers().Equals(serverException.Request().Headers()));
        Assert.Equal(httpRequest.Uri(), serverException.Request().Uri());
    }
}
