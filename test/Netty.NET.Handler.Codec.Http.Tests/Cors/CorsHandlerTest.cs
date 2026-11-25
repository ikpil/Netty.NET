/*
 * Copyright 2013 The Netty Project
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
using System.Collections.Immutable;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http.Cors;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpVersion;
using static Netty.NET.Handler.Codec.Http.Cors.CorsConfigBuilder;

namespace Netty.NET.Handler.Codec.Http.Tests.Cors;

// CLR: HttpResponse/FullHttpRequest/FullHttpResponse/HttpContent/LastHttpContent are the I-prefixed interfaces;
// readOutbound()/readInbound() are ReadOutbound<T>()/ReadInbound<T>(), isOpen() is IsOpen, refCnt() is RefCnt, and
// releaseOutbound() is ReleaseOutbound() (its bool result is boxed, as Java autoboxes it for assertNotNull). The static
// imports of HttpHeaderValues.CLOSE/KEEP_ALIVE are qualified HttpHeaderValues.CLOSE/KEEP_ALIVE, because a using static of
// HttpHeaderValues would also import names of HttpHeaderNames. Arrays.asList and Collections.singletonList are a
// List<T> and a one-element ImmutableList; the String header names that Java passes to preflightResponseHeader(CharSequence,
// ...) are StringCharSequence values, and the anonymous Callable<String> is a lambda (Callable is Func<T>, see
// CorsConfigBuilder). The anonymous SimpleChannelInboundHandler of varyHeaderIsAppended is the named nested class
// VaryHandler. assertThat(s).contains(x) is Assert.Contains(x, s); CharsetUtil.UTF_8 is Encoding.UTF8. ctx.writeAndFlush
// in the handlers discards its Task, as Java discards the future.
public class CorsHandlerTest
{

    [Fact]
    public void NonCorsRequest()
    {
        IHttpResponse response = SimpleRequest(ForAnyOrigin().Build(), null);
        Assert.False(response.Headers().Contains(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void SimpleRequestWithAnyOrigin()
    {
        IHttpResponse response = SimpleRequest(ForAnyOrigin().Build(), "http://localhost:7777");
        Assert.Equal("*", response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Null(response.Headers().Get(ACCESS_CONTROL_ALLOW_HEADERS));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void SimpleRequestWithNullOrigin()
    {
        IHttpResponse response = SimpleRequest(ForOrigin("http://test.com").AllowNullOrigin()
                .AllowCredentials()
                .Build(), "null");
        Assert.Equal("null", response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Equal("true", response.Headers().Get(ACCESS_CONTROL_ALLOW_CREDENTIALS));
        Assert.Null(response.Headers().Get(ACCESS_CONTROL_ALLOW_HEADERS));
        Assert.Equal(ORIGIN.ToString(), response.Headers().Get(VARY));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void SimpleRequestWithOrigin()
    {
        string origin = "http://localhost:8888";
        IHttpResponse response = SimpleRequest(ForOrigin(origin).Build(), origin);
        Assert.Equal(origin, response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Null(response.Headers().Get(ACCESS_CONTROL_ALLOW_HEADERS));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void SimpleRequestWithOrigins()
    {
        string origin1 = "http://localhost:8888";
        string origin2 = "https://localhost:8888";
        string[] origins = { origin1, origin2 };
        IHttpResponse response1 = SimpleRequest(ForOrigins(origins).Build(), origin1);
        Assert.Equal(origin1, response1.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Null(response1.Headers().Get(ACCESS_CONTROL_ALLOW_HEADERS));
        Assert.True(ReferenceCountUtil.Release(response1));

        IHttpResponse response2 = SimpleRequest(ForOrigins(origins).Build(), origin2);
        Assert.Equal(origin2, response2.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Null(response2.Headers().Get(ACCESS_CONTROL_ALLOW_HEADERS));
        Assert.True(ReferenceCountUtil.Release(response2));
    }

    [Fact]
    public void SimpleRequestWithNoMatchingOrigin()
    {
        string origin = "http://localhost:8888";
        IHttpResponse response = SimpleRequest(
                ForOrigins("https://localhost:8888").Build(), origin);
        Assert.Null(response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Null(response.Headers().Get(ACCESS_CONTROL_ALLOW_HEADERS));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightDeleteRequestWithCustomHeaders()
    {
        CorsConfig config = ForOrigin("http://localhost:8888")
                .AllowedRequestMethods(GET, DELETE)
                .Build();
        IHttpResponse response = PreflightRequest(config, "http://localhost:8888", "content-type, xheader1");
        Assert.Equal("http://localhost:8888", response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Contains("GET", response.Headers().Get(ACCESS_CONTROL_ALLOW_METHODS));
        Assert.Contains("DELETE", response.Headers().Get(ACCESS_CONTROL_ALLOW_METHODS));
        Assert.Equal(ORIGIN.ToString(), response.Headers().Get(VARY));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightGetRequestWithCustomHeaders()
    {
        CorsConfig config = ForOrigin("http://localhost:8888")
                .AllowedRequestMethods(OPTIONS, GET, DELETE)
                .AllowedRequestHeaders("content-type", "xheader1")
                .Build();
        IHttpResponse response = PreflightRequest(config, "http://localhost:8888", "content-type, xheader1");
        Assert.Equal("http://localhost:8888", response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Contains("OPTIONS", response.Headers().Get(ACCESS_CONTROL_ALLOW_METHODS));
        Assert.Contains("GET", response.Headers().Get(ACCESS_CONTROL_ALLOW_METHODS));
        Assert.Contains("content-type", response.Headers().Get(ACCESS_CONTROL_ALLOW_HEADERS));
        Assert.Contains("xheader1", response.Headers().Get(ACCESS_CONTROL_ALLOW_HEADERS));
        Assert.Equal(ORIGIN.ToString(), response.Headers().Get(VARY));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightRequestWithDefaultHeaders()
    {
        CorsConfig config = ForOrigin("http://localhost:8888").Build();
        IHttpResponse response = PreflightRequest(config, "http://localhost:8888", "content-type, xheader1");
        Assert.Equal("0", response.Headers().Get(CONTENT_LENGTH));
        Assert.NotNull(response.Headers().Get(DATE));
        Assert.Equal(ORIGIN.ToString(), response.Headers().Get(VARY));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightRequestWithCustomHeader()
    {
        CorsConfig config = ForOrigin("http://localhost:8888")
                .PreflightResponseHeader(new StringCharSequence("CustomHeader"), "somevalue")
                .Build();
        IHttpResponse response = PreflightRequest(config, "http://localhost:8888", "content-type, xheader1");
        Assert.Equal("somevalue", response.Headers().Get(Of("CustomHeader")));
        Assert.Equal(ORIGIN.ToString(), response.Headers().Get(VARY));
        Assert.Equal("0", response.Headers().Get(CONTENT_LENGTH));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightRequestWithUnauthorizedOrigin()
    {
        string origin = "http://host";
        CorsConfig config = ForOrigin("http://localhost").Build();
        IHttpResponse response = PreflightRequest(config, origin, "xheader1");
        Assert.False(response.Headers().Contains(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightRequestWithCustomHeaders()
    {
        string headerName = "CustomHeader";
        string value1 = "value1";
        string value2 = "value2";
        CorsConfig config = ForOrigin("http://localhost:8888")
                .PreflightResponseHeader(new StringCharSequence(headerName), value1, value2)
                .Build();
        IHttpResponse response = PreflightRequest(config, "http://localhost:8888", "content-type, xheader1");
        AssertValues(response, headerName, value1, value2);
        Assert.Equal(ORIGIN.ToString(), response.Headers().Get(VARY));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightRequestWithCustomHeadersIterable()
    {
        string headerName = "CustomHeader";
        string value1 = "value1";
        string value2 = "value2";
        CorsConfig config = ForOrigin("http://localhost:8888")
                .PreflightResponseHeader(new StringCharSequence(headerName), new List<string> { value1, value2 })
                .Build();
        IHttpResponse response = PreflightRequest(config, "http://localhost:8888", "content-type, xheader1");
        AssertValues(response, headerName, value1, value2);
        Assert.Equal(ORIGIN.ToString(), response.Headers().Get(VARY));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightRequestWithValueGenerator()
    {
        CorsConfig config = ForOrigin("http://localhost:8888")
                .PreflightResponseHeader(new StringCharSequence("GenHeader"), () => "generatedValue").Build();
        IHttpResponse response = PreflightRequest(config, "http://localhost:8888", "content-type, xheader1");
        Assert.Equal("generatedValue", response.Headers().Get(Of("GenHeader")));
        Assert.Equal(ORIGIN.ToString(), response.Headers().Get(VARY));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightRequestWithNullOrigin()
    {
        string origin = "null";
        CorsConfig config = ForOrigin(origin)
                .AllowNullOrigin()
                .AllowCredentials()
                .Build();
        IHttpResponse response = PreflightRequest(config, origin, "content-type, xheader1");
        Assert.Equal("null", response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Equal("true", response.Headers().Get(ACCESS_CONTROL_ALLOW_CREDENTIALS));
        Assert.Equal(ORIGIN.ToString(), response.Headers().Get(VARY));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightRequestAllowCredentials()
    {
        string origin = "null";
        CorsConfig config = ForOrigin(origin).AllowCredentials().Build();
        IHttpResponse response = PreflightRequest(config, origin, "content-type, xheader1");
        Assert.Equal("true", response.Headers().Get(ACCESS_CONTROL_ALLOW_CREDENTIALS));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightRequestDoNotAllowCredentials()
    {
        CorsConfig config = ForOrigin("http://localhost:8888").Build();
        IHttpResponse response = PreflightRequest(config, "http://localhost:8888", "");
        // the only valid value for Access-Control-Allow-Credentials is true.
        Assert.False(response.Headers().Contains(ACCESS_CONTROL_ALLOW_CREDENTIALS));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void SimpleRequestCustomHeaders()
    {
        CorsConfig config = ForAnyOrigin().ExposeHeaders("custom1", "custom2").Build();
        IHttpResponse response = SimpleRequest(config, "http://localhost:7777");
        Assert.Equal("*", response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Contains("custom1", response.Headers().Get(ACCESS_CONTROL_EXPOSE_HEADERS));
        Assert.Contains("custom2", response.Headers().Get(ACCESS_CONTROL_EXPOSE_HEADERS));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void SimpleRequestAllowCredentials()
    {
        CorsConfig config = ForAnyOrigin().AllowCredentials().Build();
        IHttpResponse response = SimpleRequest(config, "http://localhost:7777");
        Assert.Equal("true", response.Headers().Get(ACCESS_CONTROL_ALLOW_CREDENTIALS));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void SimpleRequestDoNotAllowCredentials()
    {
        CorsConfig config = ForAnyOrigin().Build();
        IHttpResponse response = SimpleRequest(config, "http://localhost:7777");
        Assert.False(response.Headers().Contains(ACCESS_CONTROL_ALLOW_CREDENTIALS));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void AnyOriginAndAllowCredentialsShouldEchoRequestOrigin()
    {
        CorsConfig config = ForAnyOrigin().AllowCredentials().Build();
        IHttpResponse response = SimpleRequest(config, "http://localhost:7777");
        Assert.Equal("true", response.Headers().Get(ACCESS_CONTROL_ALLOW_CREDENTIALS));
        Assert.Equal("http://localhost:7777", response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Equal(ORIGIN.ToString(), response.Headers().Get(VARY));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void SimpleRequestExposeHeaders()
    {
        CorsConfig config = ForAnyOrigin().ExposeHeaders("one", "two").Build();
        IHttpResponse response = SimpleRequest(config, "http://localhost:7777");
        Assert.Contains("one", response.Headers().Get(ACCESS_CONTROL_EXPOSE_HEADERS));
        Assert.Contains("two", response.Headers().Get(ACCESS_CONTROL_EXPOSE_HEADERS));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void SimpleRequestShortCircuit()
    {
        CorsConfig config = ForOrigin("http://localhost:8080").ShortCircuit().Build();
        IHttpResponse response = SimpleRequest(config, "http://localhost:7777");
        Assert.Equal(FORBIDDEN, response.Status());
        Assert.Equal("0", response.Headers().Get(CONTENT_LENGTH));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void SimpleRequestNoShortCircuit()
    {
        CorsConfig config = ForOrigin("http://localhost:8080").Build();
        IHttpResponse response = SimpleRequest(config, "http://localhost:7777");
        Assert.Equal(OK, response.Status());
        Assert.Null(response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void ShortCircuitNonCorsRequest()
    {
        CorsConfig config = ForOrigin("https://localhost").ShortCircuit().Build();
        IHttpResponse response = SimpleRequest(config, null);
        Assert.Equal(OK, response.Status());
        Assert.Null(response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void ShortCircuitWithConnectionKeepAliveShouldStayOpen()
    {
        CorsConfig config = ForOrigin("http://localhost:8080").ShortCircuit().Build();
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config));
        IFullHttpRequest request = CreateHttpRequest(GET);
        request.Headers().Set(ORIGIN, "http://localhost:8888");
        request.Headers().Set(CONNECTION, HttpHeaderValues.KEEP_ALIVE);

        Assert.False(channel.WriteInbound(request));
        IHttpResponse response = channel.ReadOutbound<IHttpResponse>();
        Assert.True(HttpUtil.IsKeepAlive(response));

        Assert.True(channel.IsOpen);
        Assert.Equal(FORBIDDEN, response.Status());
        Assert.True(ReferenceCountUtil.Release(response));
        Assert.False(channel.Finish());
    }

    [Fact]
    public void ShortCircuitWithoutConnectionShouldStayOpen()
    {
        CorsConfig config = ForOrigin("http://localhost:8080").ShortCircuit().Build();
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config));
        IFullHttpRequest request = CreateHttpRequest(GET);
        request.Headers().Set(ORIGIN, "http://localhost:8888");

        Assert.False(channel.WriteInbound(request));
        IHttpResponse response = channel.ReadOutbound<IHttpResponse>();
        Assert.True(HttpUtil.IsKeepAlive(response));

        Assert.True(channel.IsOpen);
        Assert.Equal(FORBIDDEN, response.Status());
        Assert.True(ReferenceCountUtil.Release(response));
        Assert.False(channel.Finish());
    }

    [Fact]
    public void ShortCircuitWithConnectionCloseShouldClose()
    {
        CorsConfig config = ForOrigin("http://localhost:8080").ShortCircuit().Build();
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config));
        IFullHttpRequest request = CreateHttpRequest(GET);
        request.Headers().Set(ORIGIN, "http://localhost:8888");
        request.Headers().Set(CONNECTION, HttpHeaderValues.CLOSE);

        Assert.False(channel.WriteInbound(request));
        IHttpResponse response = channel.ReadOutbound<IHttpResponse>();
        Assert.False(HttpUtil.IsKeepAlive(response));

        Assert.False(channel.IsOpen);
        Assert.Equal(FORBIDDEN, response.Status());
        Assert.True(ReferenceCountUtil.Release(response));
        Assert.False(channel.Finish());
    }

    [Fact]
    public void PreflightRequestShouldReleaseRequest()
    {
        CorsConfig config = ForOrigin("http://localhost:8888")
                .PreflightResponseHeader(new StringCharSequence("CustomHeader"), new List<string> { "value1", "value2" })
                .Build();
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config));
        IFullHttpRequest request = OptionsRequest("http://localhost:8888", "content-type, xheader1", null);
        Assert.False(channel.WriteInbound(request));
        Assert.Equal(0, request.RefCnt);
        Assert.True(ReferenceCountUtil.Release(channel.ReadOutbound<object>()));
        Assert.False(channel.Finish());
    }

    [Fact]
    public void PreflightRequestWithConnectionKeepAliveShouldStayOpen()
    {

        CorsConfig config = ForOrigin("http://localhost:8888").Build();
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config));
        IFullHttpRequest request = OptionsRequest("http://localhost:8888", "", HttpHeaderValues.KEEP_ALIVE);
        Assert.False(channel.WriteInbound(request));
        IHttpResponse response = channel.ReadOutbound<IHttpResponse>();
        Assert.True(HttpUtil.IsKeepAlive(response));

        Assert.True(channel.IsOpen);
        Assert.Equal(OK, response.Status());
        Assert.True(ReferenceCountUtil.Release(response));
        Assert.False(channel.Finish());
    }

    [Fact]
    public void PreflightRequestWithoutConnectionShouldStayOpen()
    {

        CorsConfig config = ForOrigin("http://localhost:8888").Build();
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config));
        IFullHttpRequest request = OptionsRequest("http://localhost:8888", "", null);
        Assert.False(channel.WriteInbound(request));
        IHttpResponse response = channel.ReadOutbound<IHttpResponse>();
        Assert.True(HttpUtil.IsKeepAlive(response));

        Assert.True(channel.IsOpen);
        Assert.Equal(OK, response.Status());
        Assert.True(ReferenceCountUtil.Release(response));
        Assert.False(channel.Finish());
    }

    [Fact]
    public void PreflightRequestWithConnectionCloseShouldClose()
    {

        CorsConfig config = ForOrigin("http://localhost:8888").Build();
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config));
        IFullHttpRequest request = OptionsRequest("http://localhost:8888", "", HttpHeaderValues.CLOSE);
        Assert.False(channel.WriteInbound(request));
        IHttpResponse response = channel.ReadOutbound<IHttpResponse>();
        Assert.False(HttpUtil.IsKeepAlive(response));

        Assert.False(channel.IsOpen);
        Assert.Equal(OK, response.Status());
        Assert.True(ReferenceCountUtil.Release(response));
        Assert.False(channel.Finish());
    }

    [Fact]
    public void ForbiddenShouldReleaseRequest()
    {
        CorsConfig config = ForOrigin("https://localhost").ShortCircuit().Build();
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config), new EchoHandler());
        IFullHttpRequest request = CreateHttpRequest(GET);
        request.Headers().Set(ORIGIN, "http://localhost:8888");
        Assert.False(channel.WriteInbound(request));
        Assert.Equal(0, request.RefCnt);
        Assert.True(ReferenceCountUtil.Release(channel.ReadOutbound<object>()));
        Assert.False(channel.Finish());
    }

    [Fact]
    public void DifferentConfigsPerOrigin()
    {
        string host1 = "http://host1:80";
        string host2 = "http://host2";
        CorsConfig rule1 = ForOrigin(host1).AllowedRequestMethods(HttpMethod.GET).Build();
        CorsConfig rule2 = ForOrigin(host2).AllowedRequestMethods(HttpMethod.GET, HttpMethod.POST)
                .AllowCredentials().Build();

        IList<CorsConfig> corsConfigs = new List<CorsConfig> { rule1, rule2 };

        IHttpResponse preFlightHost1 = PreflightRequest(corsConfigs, host1, "", false);
        Assert.Equal("GET", preFlightHost1.Headers().Get(ACCESS_CONTROL_ALLOW_METHODS));
        Assert.Null(preFlightHost1.Headers().GetAsString(ACCESS_CONTROL_ALLOW_CREDENTIALS));

        IHttpResponse preFlightHost2 = PreflightRequest(corsConfigs, host2, "", false);
        AssertValues(preFlightHost2, ACCESS_CONTROL_ALLOW_METHODS.ToString(), "GET", "POST");
        Assert.Equal("true", preFlightHost2.Headers().GetAsString(ACCESS_CONTROL_ALLOW_CREDENTIALS));
    }

    [Fact]
    public void SpecificConfigPrecedenceOverGeneric()
    {
        string host1 = "http://host1";
        string host2 = "http://host2";

        CorsConfig forHost1 = ForOrigin(host1).AllowedRequestMethods(HttpMethod.GET).MaxAge(3600L).Build();
        CorsConfig allowAll = ForAnyOrigin().AllowedRequestMethods(HttpMethod.POST, HttpMethod.GET, HttpMethod.OPTIONS)
                .MaxAge(1800).Build();

        IList<CorsConfig> rules = new List<CorsConfig> { forHost1, allowAll };

        IHttpResponse host1Response = PreflightRequest(rules, host1, "", false);
        Assert.Equal("GET", host1Response.Headers().Get(ACCESS_CONTROL_ALLOW_METHODS));
        Assert.Equal("3600", host1Response.Headers().GetAsString(ACCESS_CONTROL_MAX_AGE));

        IHttpResponse host2Response = PreflightRequest(rules, host2, "", false);
        AssertValues(host2Response, ACCESS_CONTROL_ALLOW_METHODS.ToString(), "POST", "GET", "OPTIONS");
        Assert.Equal("*", host2Response.Headers().GetAsString(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.Equal("1800", host2Response.Headers().GetAsString(ACCESS_CONTROL_MAX_AGE));
    }

    [Fact]
    public void SimpleRequestAllowPrivateNetwork()
    {
        CorsConfig config = ForOrigin("http://localhost:8888").AllowPrivateNetwork().Build();
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config));
        IFullHttpRequest request = OptionsRequest("http://localhost:8888", "", null);
        request.Headers().Set(ACCESS_CONTROL_REQUEST_PRIVATE_NETWORK, "true");
        Assert.False(channel.WriteInbound(request));
        IHttpResponse response = channel.ReadOutbound<IHttpResponse>();

        Assert.Equal("true", response.Headers().Get(ACCESS_CONTROL_ALLOW_PRIVATE_NETWORK));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void SimpleRequestDoNotAllowPrivateNetwork()
    {
        CorsConfig config = ForOrigin("http://localhost:8888").Build();
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config));
        IFullHttpRequest request = OptionsRequest("http://localhost:8888", "", null);
        request.Headers().Set(ACCESS_CONTROL_REQUEST_PRIVATE_NETWORK, "true");
        Assert.False(channel.WriteInbound(request));
        IHttpResponse response = channel.ReadOutbound<IHttpResponse>();

        Assert.Equal("false", response.Headers().Get(ACCESS_CONTROL_ALLOW_PRIVATE_NETWORK));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void PreflightEmptyLastDiscarded()
    {
        CorsConfig config = ForOrigin("http://allowed").Build();
        EmbeddedChannel ch = new EmbeddedChannel(new CorsHandler(config));

        IFullHttpRequest preflight = new DefaultFullHttpRequest(HTTP_1_1, OPTIONS, "/test");
        preflight.Headers().Set(ORIGIN, "http://allowed");
        preflight.Headers().Set(ACCESS_CONTROL_REQUEST_METHOD, "GET");

        Assert.False(ch.WriteInbound(preflight));

        object outbound = ch.ReadOutbound<object>();
        Assert.NotNull(outbound); // preflight response

        ILastHttpContent lastHttpContent = ILastHttpContent.EMPTY_LAST_CONTENT;
        Assert.False(ch.WriteInbound(lastHttpContent));

        // Nothing should have been forwarded
        Assert.Null(ch.ReadInbound<object>());

        Assert.False(ch.Finish());
    }

    [Fact]
    public void PreflightSecondEmptyLastForwardedAfterFirstDiscard()
    {
        CorsConfig config = ForOrigin("http://allowed").Build();
        EmbeddedChannel ch = new EmbeddedChannel(new CorsHandler(config));

        IFullHttpRequest preflight = new DefaultFullHttpRequest(HTTP_1_1, OPTIONS, "/test");
        preflight.Headers().Set(ORIGIN, "http://allowed");
        preflight.Headers().Set(ACCESS_CONTROL_REQUEST_METHOD, "GET");

        Assert.False(ch.WriteInbound(preflight));
        ReferenceCountUtil.Release(ch.ReadOutbound<object>());

        ILastHttpContent first = ILastHttpContent.EMPTY_LAST_CONTENT;
        ILastHttpContent second = ILastHttpContent.EMPTY_LAST_CONTENT;

        Assert.False(ch.WriteInbound(first));

        Assert.False(ch.WriteInbound(second));

        Assert.Null(ch.ReadInbound<object>());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void PreflightSecondNonEmptyLastDiscarded()
    {
        CorsConfig config = ForOrigin("http://allowed").Build();
        EmbeddedChannel ch = new EmbeddedChannel(new CorsHandler(config));

        IFullHttpRequest preflight = new DefaultFullHttpRequest(HTTP_1_1, OPTIONS, "/test");
        preflight.Headers().Set(ORIGIN, "http://allowed");
        preflight.Headers().Set(ACCESS_CONTROL_REQUEST_METHOD, "GET");

        Assert.False(ch.WriteInbound(preflight));
        ReferenceCountUtil.Release(ch.ReadOutbound<object>());

        ILastHttpContent first = ILastHttpContent.EMPTY_LAST_CONTENT;
        ILastHttpContent second = new DefaultLastHttpContent(
                Unpooled.CopiedBuffer("test message", Encoding.UTF8));

        Assert.False(ch.WriteInbound(first));
        Assert.False(ch.WriteInbound(second));
        Assert.Null(ch.ReadInbound<object>());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void PreflightNonEmptyLastForwarded()
    {
        CorsConfig config = ForOrigin("http://allowed").Build();
        EmbeddedChannel ch = new EmbeddedChannel(new CorsHandler(config));

        IFullHttpRequest preflight = new DefaultFullHttpRequest(HTTP_1_1, OPTIONS, "/x");
        preflight.Headers().Set(ORIGIN, "http://allowed");
        preflight.Headers().Set(ACCESS_CONTROL_REQUEST_METHOD, "GET");

        Assert.False(ch.WriteInbound(preflight));
        object outbound = ch.ReleaseOutbound();
        Assert.NotNull(outbound);

        ILastHttpContent nonEmpty = new DefaultLastHttpContent(Unpooled.CopiedBuffer("x", Encoding.UTF8));
        Assert.False(ch.WriteInbound(nonEmpty));

        object inbound = ch.ReadInbound<object>();
        Assert.Null(inbound);

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestNormalRequestForwarded()
    {
        CorsConfig config = ForOrigin("http://allowed").Build();
        EmbeddedChannel ch = new EmbeddedChannel(new CorsHandler(config));

        IFullHttpRequest req = new DefaultFullHttpRequest(HTTP_1_1, GET, "/test");
        req.Headers().Set(ORIGIN, "http://allowed");

        Assert.True(ch.WriteInbound(req));

        ILastHttpContent last = ILastHttpContent.EMPTY_LAST_CONTENT;
        Assert.True(ch.WriteInbound(last));

        object firstInbound = ch.ReadInbound<object>();
        object secondInbound = ch.ReadInbound<object>();

        Assert.NotNull(firstInbound);
        Assert.NotNull(secondInbound);

        Assert.Null(ch.ReadInbound<object>());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void PreflightEmptyLastDiscardedThenNewRequestForwarded()
    {
        CorsConfig config = ForOrigin("http://allowed").Build();
        EmbeddedChannel ch = new EmbeddedChannel(new CorsHandler(config));

        // Preflight request
        IFullHttpRequest preflight = new DefaultFullHttpRequest(HTTP_1_1, OPTIONS, "/pre");
        preflight.Headers().Set(ORIGIN, "http://allowed");
        preflight.Headers().Set(ACCESS_CONTROL_REQUEST_METHOD, "GET");
        Assert.False(ch.WriteInbound(preflight));
        object preflightResp = ch.ReadOutbound<object>();
        Assert.NotNull(preflightResp);
        ReferenceCountUtil.Release(preflightResp);

        // Empty last content should be discarded
        Assert.False(ch.WriteInbound(ILastHttpContent.EMPTY_LAST_CONTENT));
        Assert.Null(ch.ReadInbound<object>());

        // New request should be forwarded
        IFullHttpRequest req = new DefaultFullHttpRequest(HTTP_1_1, GET, "/next");
        req.Headers().Set(ORIGIN, "http://allowed");
        Assert.True(ch.WriteInbound(req));

        object firstInbound = ch.ReadInbound<object>();
        Assert.NotNull(firstInbound);

        IHttpContent content = new DefaultHttpContent(Unpooled.CopiedBuffer("test message", Encoding.UTF8));
        Assert.True(ch.WriteInbound(content));
        object secondInbound = ch.ReadInbound<object>();
        Assert.NotNull(secondInbound);

        Assert.Null(ch.ReadInbound<object>());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void ShortCircuitWithNullOriginNotAllowedShouldBeForbidden()
    {
        CorsConfig config = ForOrigin("http://localhost:8080").ShortCircuit().Build();
        IHttpResponse response = SimpleRequest(config, "null");
        Assert.Equal(FORBIDDEN, response.Status());
        Assert.Equal("0", response.Headers().Get(CONTENT_LENGTH));
        Assert.True(ReferenceCountUtil.Release(response));
    }

    [Fact]
    public void ShortCircuitWithNullOriginAllowedShouldSucceed()
    {
        CorsConfig config = ForOrigin("http://localhost:8080").AllowNullOrigin().ShortCircuit().Build();
        IHttpResponse response = SimpleRequest(config, "null");
        Assert.Equal(OK, response.Status());
        Assert.Equal("null", response.Headers().Get(ACCESS_CONTROL_ALLOW_ORIGIN));
        Assert.True(ReferenceCountUtil.Release(response));
    }

   [Fact]
   public void VaryHeaderIsAppended()
   {
       CorsConfig config = ForAnyOrigin().AllowCredentials().Build();
       EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config),
               new VaryHandler());
       IFullHttpRequest request = CreateHttpRequest(GET);
       request.Headers().Set(ORIGIN, "http://localhost:7777");
       Assert.False(channel.WriteInbound(request));

       IFullHttpResponse response = channel.ReadOutbound<IFullHttpResponse>();

       IList<string> varyHeaders = response.Headers().GetAll(VARY);
       Assert.Contains(ACCEPT_ENCODING.ToString(), varyHeaders);
       Assert.Contains(ORIGIN.ToString(), varyHeaders);

       response.Release();
       Assert.False(channel.Finish());
   }

    private sealed class VaryHandler : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            IHttpResponse response = new DefaultFullHttpResponse(HTTP_1_1, OK, Unpooled.Buffer(0));
            response.Headers().Set(VARY, ACCEPT_ENCODING);
            _ = ctx.WriteAndFlushAsync(response);
        }
    }

    private static IHttpResponse SimpleRequest(CorsConfig config, string origin)
    {
        return SimpleRequest(config, origin, null);
    }

    private static IHttpResponse SimpleRequest(CorsConfig config,
                                              string origin,
                                              string requestHeaders)
    {
        return SimpleRequest(config, origin, requestHeaders, GET);
    }

    private static IHttpResponse SimpleRequest(CorsConfig config,
                                              string origin,
                                              string requestHeaders,
                                              HttpMethod method)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(config), new EchoHandler());
        IFullHttpRequest httpRequest = CreateHttpRequest(method);
        if (origin != null)
        {
            httpRequest.Headers().Set(ORIGIN, origin);
        }
        if (requestHeaders != null)
        {
            httpRequest.Headers().Set(ACCESS_CONTROL_REQUEST_HEADERS, requestHeaders);
        }
        Assert.False(channel.WriteInbound(httpRequest));
        IHttpResponse response =  channel.ReadOutbound<IHttpResponse>();
        Assert.False(channel.Finish());
        return response;
    }

    private static IHttpResponse PreflightRequest(CorsConfig config,
                                                 string origin,
                                                 string requestHeaders)
    {
        return PreflightRequest(ImmutableList.Create(config), origin, requestHeaders, config.IsShortCircuit());
    }

    private static IHttpResponse PreflightRequest(IList<CorsConfig> configs,
                                                 string origin,
                                                 string requestHeaders,
                                                 bool isSHortCircuit)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new CorsHandler(configs, isSHortCircuit));
        Assert.False(channel.WriteInbound(OptionsRequest(origin, requestHeaders, null)));
        IHttpResponse response = channel.ReadOutbound<IHttpResponse>();
        Assert.False(channel.Finish());
        return response;
    }

    private static IFullHttpRequest OptionsRequest(string origin,
                                                  string requestHeaders,
                                                  AsciiString connection)
    {
        IFullHttpRequest httpRequest = CreateHttpRequest(OPTIONS);
        httpRequest.Headers().Set(ORIGIN, origin);
        httpRequest.Headers().Set(ACCESS_CONTROL_REQUEST_METHOD, httpRequest.Method().ToString());
        httpRequest.Headers().Set(ACCESS_CONTROL_REQUEST_HEADERS, requestHeaders);
        if (connection != null)
        {
            httpRequest.Headers().Set(CONNECTION, connection);
        }

        return httpRequest;
    }

    private static IFullHttpRequest CreateHttpRequest(HttpMethod method)
    {
        return new DefaultFullHttpRequest(HTTP_1_1, method, "/info");
    }

    private class EchoHandler : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            _ = ctx.WriteAndFlushAsync(new DefaultFullHttpResponse(HTTP_1_1, OK, Unpooled.Buffer(0),
                    DefaultHttpHeadersFactory.HeadersFactory().WithCombiningHeaders(true),
                    DefaultHttpHeadersFactory.TrailersFactory().WithCombiningHeaders(true)));
        }
    }

    private static void AssertValues(IHttpResponse response, string headerName, params string[] values)
    {
        string header = response.Headers().Get(Of(headerName));
        foreach (string value in values)
        {
            Assert.Contains(value, header);
        }
    }
}
