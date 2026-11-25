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
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http.Cors;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;
using static Netty.NET.Handler.Codec.Http.Cors.CorsConfigBuilder;

namespace Netty.NET.Handler.Codec.Http.Tests.Cors;

// CLR: assertThat(collection).contains(values...) is one Assert.Contains per value; assertThrows(IllegalArgumentException
// .class, executable) is Assert.Throws<ArgumentException>, and the anonymous Executable is a lambda. The String header
// names that Java passes to preflightResponseHeader(CharSequence, ...) are StringCharSequence values (a C# string is not
// an ICharSequence). HttpHeaders is an IEnumerable of its entries, so assertEquals(EmptyHttpHeaders.INSTANCE, headers) calls
// Equals directly, as the core header tests do.
public class CorsConfigTest
{

    [Fact]
    public void Disabled()
    {
        CorsConfig cors = ForAnyOrigin().Disable().Build();
        Assert.False(cors.IsCorsSupportEnabled());
    }

    [Fact]
    public void AnyOrigin()
    {
        CorsConfig cors = ForAnyOrigin().Build();
        Assert.True(cors.IsAnyOriginSupported());
        Assert.Equal("*", cors.Origin());
        Assert.True(cors.Origins().Count == 0);
    }

    [Fact]
    public void WildcardOrigin()
    {
        CorsConfig cors = ForOrigin("*").Build();
        Assert.True(cors.IsAnyOriginSupported());
        Assert.Equal("*", cors.Origin());
        Assert.True(cors.Origins().Count == 0);
    }

    [Fact]
    public void Origin()
    {
        CorsConfig cors = ForOrigin("http://localhost:7888").Build();
        Assert.Equal("http://localhost:7888", cors.Origin());
        Assert.False(cors.IsAnyOriginSupported());
    }

    [Fact]
    public void Origins()
    {
        string[] origins = { "http://localhost:7888", "https://localhost:7888" };
        CorsConfig cors = ForOrigins(origins).Build();
        foreach (string origin in origins)
        {
            Assert.Contains(origin, cors.Origins());
        }
        Assert.False(cors.IsAnyOriginSupported());
    }

    [Fact]
    public void ExposeHeaders()
    {
        CorsConfig cors = ForAnyOrigin().ExposeHeaders("custom-header1", "custom-header2").Build();
        Assert.Contains("custom-header1", cors.ExposedHeaders());
        Assert.Contains("custom-header2", cors.ExposedHeaders());
    }

    [Fact]
    public void AllowCredentials()
    {
        CorsConfig cors = ForAnyOrigin().AllowCredentials().Build();
        Assert.True(cors.IsCredentialsAllowed());
    }

    [Fact]
    public void MaxAge()
    {
        CorsConfig cors = ForAnyOrigin().MaxAge(3000).Build();
        Assert.Equal(3000L, cors.MaxAge());
    }

    [Fact]
    public void RequestMethods()
    {
        CorsConfig cors = ForAnyOrigin().AllowedRequestMethods(HttpMethod.POST, HttpMethod.GET).Build();
        Assert.Contains(HttpMethod.POST, cors.AllowedRequestMethods());
        Assert.Contains(HttpMethod.GET, cors.AllowedRequestMethods());
    }

    [Fact]
    public void RequestHeaders()
    {
        CorsConfig cors = ForAnyOrigin().AllowedRequestHeaders("preflight-header1", "preflight-header2").Build();
        Assert.Contains("preflight-header1", cors.AllowedRequestHeaders());
        Assert.Contains("preflight-header2", cors.AllowedRequestHeaders());
    }

    [Fact]
    public void PreflightResponseHeadersSingleValue()
    {
        CorsConfig cors = ForAnyOrigin().PreflightResponseHeader(new StringCharSequence("SingleValue"), "value").Build();
        Assert.Equal("value", cors.PreflightResponseHeaders().Get(Of("SingleValue")));
    }

    [Fact]
    public void PreflightResponseHeadersMultipleValues()
    {
        CorsConfig cors = ForAnyOrigin().PreflightResponseHeader(new StringCharSequence("MultipleValues"), "value1",
                "value2").Build();
        Assert.Contains("value1", cors.PreflightResponseHeaders().GetAll(Of("MultipleValues")));
        Assert.Contains("value2", cors.PreflightResponseHeaders().GetAll(Of("MultipleValues")));
    }

    [Fact]
    public void DefaultPreflightResponseHeaders()
    {
        CorsConfig cors = ForAnyOrigin().Build();
        Assert.NotNull(cors.PreflightResponseHeaders().Get(HttpHeaderNames.DATE));
        Assert.Equal("0", cors.PreflightResponseHeaders().Get(HttpHeaderNames.CONTENT_LENGTH));
    }

    [Fact]
    public void EmptyPreflightResponseHeaders()
    {
        CorsConfig cors = ForAnyOrigin().NoPreflightResponseHeaders().Build();
        Assert.True(EmptyHttpHeaders.INSTANCE.Equals(cors.PreflightResponseHeaders()));
    }

    [Fact]
    public void ShouldThrowIfValueIsNull()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            ForOrigin("*").PreflightResponseHeader(new StringCharSequence("HeaderName"), new object[] { null }).Build();
        });
    }

    [Fact]
    public void ShortCircuit()
    {
        CorsConfig cors = ForOrigin("http://localhost:8080").ShortCircuit().Build();
        Assert.True(cors.IsShortCircuit());
    }
}
