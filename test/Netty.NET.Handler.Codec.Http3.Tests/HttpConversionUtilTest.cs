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

using System;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: as codec-http2's HttpConversionUtilTest port (docs/codec-http2-porting.md, HTTP/1.x conversion): the
// @Deprecated DefaultHttpRequest(HttpVersion, HttpMethod, String, boolean validateHeaders) is not ported in codec-http
// (its @deprecated text names the (HttpVersion, HttpMethod, String) constructor, "to always have header validation
// enabled"; docs/codec-http-porting.md, Core not ported); every call here passes true, so it is that constructor. new
// DefaultHttpHeaders(validate) is DefaultHttpHeadersFactory.HeadersFactory().WithValidation(validate).NewHeaders(), and
// the deprecated HttpHeaderNames.KEEP_ALIVE/PROXY_CONNECTION are local AsciiString constants with the same names
// (docs/codec-http-porting.md, Core test notes). A Java String passed as a CharSequence is a StringCharSequence (Cs),
// because a C# string is not an ICharSequence (docs/codec-http-porting.md, Shared CLR adaptations). assertEquals of two
// CharSequences calls equals, which AssertCsEquals keeps (xUnit would compare two ICharSequence values,
// IReadOnlyList<char>, element by element), and a CharSequence result that Java compares with a String is compared as a
// string. assertSame("world", out.get("hello")) holds in Java because the conversion copies the String instance the
// HttpHeaders store; here the headers store an ICharSequence for the C# string, so the assertion compares the text (the
// AsciiString assertSame(TRAILERS, ...) and assertSame(EMPTY_STRING, ...) checks stay reference checks).
// assertThrows(T.class, ...) is Assert.ThrowsAny<T> (docs/codec-http-porting.md, Core test notes) and the lambdas are
// lambdas. The @MethodSource tests are [Theory] methods with [MemberData] over TheoryData (List<String> is string[],
// asList/singletonList are array literals), and the @ValueSource(strings) test is a [Theory] with InlineData rows. The
// static imports of HttpHeaderValues.GZIP and TRAILERS are written HttpHeaderValues.GZIP/TRAILERS: C# imports all
// static members of a class at once, and HttpHeaderValues.UPGRADE would clash with HttpHeaderNames.UPGRADE (CS0229).
// The package-private HttpConversionUtil members are reached through InternalsVisibleTo; the throws clauses have no
// counterpart.
public class HttpConversionUtilTest
{

    private static readonly AsciiString KEEP_ALIVE = AsciiString.Cached("keep-alive");
    private static readonly AsciiString PROXY_CONNECTION = AsciiString.Cached("proxy-connection");

    [Fact]
    public void ConnectNoPath()
    {
        string authority = "netty.io:80";
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Authority(Cs(authority));
        headers.Method(HttpMethod.CONNECT.AsciiName());
        IHttpRequest request = HttpConversionUtil.ToHttpRequest(0, headers, true);
        Assert.NotNull(request);
        Assert.Equal(authority, request.Uri());
        Assert.Equal(authority, request.Headers().Get(HOST));

        // Regular CONNECT (RFC 9114) must not carry Extended CONNECT (RFC 9220) state.
        Assert.False(request.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text()));
        Assert.False(request.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PATH.Text()));
    }

    [Fact]
    public void ExtendedConnectPreservesProtocolAndPathAsExtensionHeaders()
    {
        string authority = "ws.example:443";
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Method(HttpMethod.CONNECT.AsciiName());
        headers.Authority(Cs(authority));
        headers.Scheme(Cs("https"));
        headers.Path(Cs("/admin/ws"));
        headers.Protocol(Cs("websocket"));

        IHttpRequest request = HttpConversionUtil.ToHttpRequest(0, headers, true);
        Assert.NotNull(request);

        // The request-target/URI for a CONNECT request stays the authority, same as a regular CONNECT
        // request: this fix does not change wire-level CONNECT behavior.
        Assert.Equal(authority, request.Uri());
        Assert.Equal(HttpMethod.CONNECT, request.Method());
        Assert.Equal(authority, request.Headers().Get(HOST));

        // But the Extended CONNECT (RFC 9220) state, which changes CONNECT semantics such that ':authority'
        // must not be treated as an ordinary tunnel target, is preserved so it is not confused with a
        // regular CONNECT request once converted to an HTTP/1.x object.
        Assert.Equal("websocket",
                request.Headers().Get(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text()));
        Assert.Equal("/admin/ws",
                request.Headers().Get(HttpConversionUtil.ExtensionHeaderNames.PATH.Text()));
    }

    [Fact]
    public void ExtendedConnectAndRegularConnectProduceDifferentHttpObjectShape()
    {
        string authority = "ws.example:443";

        IHttp3Headers regularConnect = new DefaultHttp3Headers();
        regularConnect.Method(HttpMethod.CONNECT.AsciiName());
        regularConnect.Authority(Cs(authority));
        IHttpRequest regularConnectRequest = HttpConversionUtil.ToHttpRequest(0, regularConnect, true);

        IHttp3Headers extendedConnect = new DefaultHttp3Headers();
        extendedConnect.Method(HttpMethod.CONNECT.AsciiName());
        extendedConnect.Authority(Cs(authority));
        extendedConnect.Scheme(Cs("https"));
        extendedConnect.Path(Cs("/admin/ws"));
        extendedConnect.Protocol(Cs("websocket"));
        IHttpRequest extendedConnectRequest = HttpConversionUtil.ToHttpRequest(0, extendedConnect, true);

        // Both requests still have the same CONNECT method and request-target/Host, matching a regular
        // CONNECT allowlist that keys off of those alone.
        Assert.Equal(regularConnectRequest.Method(), extendedConnectRequest.Method());
        Assert.Equal(regularConnectRequest.Uri(), extendedConnectRequest.Uri());
        Assert.Equal(regularConnectRequest.Headers().Get(HOST), extendedConnectRequest.Headers().Get(HOST));

        // A protocol-aware policy can still tell them apart via the extension headers.
        Assert.False(regularConnectRequest.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text()));
        Assert.True(extendedConnectRequest.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text()));
    }

    [Fact]
    public void RegularConnectDropsExtensionHeaderReceivedFromPeer()
    {
        IHttp3Headers regularConnect = new DefaultHttp3Headers();
        regularConnect.Method(HttpMethod.CONNECT.AsciiName());
        regularConnect.Authority(Cs("ws.example:443"));
        regularConnect.Add(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text(), Cs("websocket"));
        IHttpRequest regularConnectRequest = HttpConversionUtil.ToHttpRequest(0, regularConnect, true);
        Assert.False(regularConnectRequest.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text()));
    }

    [Fact]
    public void SetHttp3AuthorityWithoutUserInfo()
    {
        IHttp3Headers headers = new DefaultHttp3Headers();

        HttpConversionUtil.SetHttp3Authority("foo", headers);
        AssertCsEquals(new AsciiString("foo"), headers.Authority());
    }

    [Fact]
    public void SetHttp3AuthorityWithUserInfo()
    {
        IHttp3Headers headers = new DefaultHttp3Headers();

        HttpConversionUtil.SetHttp3Authority("info@foo", headers);
        AssertCsEquals(new AsciiString("foo"), headers.Authority());

        HttpConversionUtil.SetHttp3Authority("@foo.bar", headers);
        AssertCsEquals(new AsciiString("foo.bar"), headers.Authority());
    }

    [Fact]
    public void SetHttp3AuthorityNullOrEmpty()
    {
        IHttp3Headers headers = new DefaultHttp3Headers();

        HttpConversionUtil.SetHttp3Authority(null, headers);
        Assert.Null(headers.Authority());

        HttpConversionUtil.SetHttp3Authority("", headers);
        Assert.Same(AsciiString.EMPTY_STRING, headers.Authority());
    }

    [Fact]
    public void SetHttp2AuthorityWithEmptyAuthority()
    {
        Assert.ThrowsAny<ArgumentException>(
                () => HttpConversionUtil.SetHttp3Authority("info@", new DefaultHttp3Headers()));
    }

    [Fact]
    public void StripTEHeaders()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(TE, HttpHeaderValues.GZIP);
        IHttp3Headers @out = new DefaultHttp3Headers();
        HttpConversionUtil.ToHttp3Headers(inHeaders, @out);
        Assert.True(@out.IsEmpty());
    }

    [Fact]
    public void StripTEHeadersExcludingTrailers()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(TE, HttpHeaderValues.GZIP);
        inHeaders.Add(TE, HttpHeaderValues.TRAILERS);
        IHttp3Headers @out = new DefaultHttp3Headers();
        HttpConversionUtil.ToHttp3Headers(inHeaders, @out);
        Assert.Same(HttpHeaderValues.TRAILERS, @out.Get(TE));
    }

    [Fact]
    public void StripTEHeadersCsvSeparatedExcludingTrailers()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(TE, HttpHeaderValues.GZIP + "," + HttpHeaderValues.TRAILERS);
        IHttp3Headers @out = new DefaultHttp3Headers();
        HttpConversionUtil.ToHttp3Headers(inHeaders, @out);
        Assert.Same(HttpHeaderValues.TRAILERS, @out.Get(TE));
    }

    [Fact]
    public void StripTEHeadersCsvSeparatedAccountsForValueSimilarToTrailers()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(TE, HttpHeaderValues.GZIP + "," + HttpHeaderValues.TRAILERS + "foo");
        IHttp3Headers @out = new DefaultHttp3Headers();
        HttpConversionUtil.ToHttp3Headers(inHeaders, @out);
        Assert.False(@out.Contains(TE));
    }

    [Fact]
    public void StripTEHeadersAccountsForValueSimilarToTrailers()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(TE, HttpHeaderValues.TRAILERS + "foo");
        IHttp3Headers @out = new DefaultHttp3Headers();
        HttpConversionUtil.ToHttp3Headers(inHeaders, @out);
        Assert.False(@out.Contains(TE));
    }

    [Fact]
    public void StripTEHeadersAccountsForOWS()
    {
        // Disable header validation, since it will otherwise reject the header.
        bool validate = false;
        HttpHeaders inHeaders = DefaultHttpHeadersFactory.HeadersFactory().WithValidation(validate).NewHeaders();
        inHeaders.Add(TE, " " + HttpHeaderValues.TRAILERS + ' ');
        IHttp3Headers @out = new DefaultHttp3Headers();
        HttpConversionUtil.ToHttp3Headers(inHeaders, @out);
        Assert.Same(HttpHeaderValues.TRAILERS, @out.Get(TE));
    }

    [Fact]
    public void StripConnectionHeadersAndNominees()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(CONNECTION, "foo");
        inHeaders.Add("foo", "bar");
        IHttp3Headers @out = new DefaultHttp3Headers();
        HttpConversionUtil.ToHttp3Headers(inHeaders, @out);
        Assert.True(@out.IsEmpty());
    }

    [Fact]
    public void StripConnectionNomineesWithCsv()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(CONNECTION, "foo,  bar");
        inHeaders.Add("foo", "baz");
        inHeaders.Add("bar", "qux");
        inHeaders.Add("hello", "world");
        IHttp3Headers @out = new DefaultHttp3Headers();
        HttpConversionUtil.ToHttp3Headers(inHeaders, @out);
        Assert.Equal(1, @out.Size());
        Assert.Equal("world", ToJavaString(@out.Get(Cs("hello"))));
    }

    [Fact]
    public void AddHttp3ToHttpHeadersCombinesCookies()
    {
        IHttp3Headers inHeaders = new DefaultHttp3Headers();
        inHeaders.Add(Cs("yes"), Cs("no"));
        inHeaders.Add(COOKIE, Cs("foo=bar"));
        inHeaders.Add(COOKIE, Cs("bax=baz"));

        HttpHeaders outHeaders = new DefaultHttpHeaders();

        HttpConversionUtil.AddHttp3ToHttpHeaders(5, inHeaders, outHeaders, HttpVersion.HTTP_1_1, false, false);
        Assert.Equal("no", outHeaders.Get("yes"));
        Assert.Equal("foo=bar; bax=baz", outHeaders.Get(COOKIE.ToString()));
    }

    [Theory]
    [MemberData(nameof(MatchingAuthorityAndHost))]
    public void AddHttp3ToHttpHeadersDeduplicatesMatchingAuthorityAndHost(
            string authority, string[] hosts, bool hostAddedFirst, string expectedHost)
    {
        IHttp3Headers inHeaders = NewAuthorityAndHostHeaders(authority, hosts, hostAddedFirst);
        HttpHeaders outHeaders = new DefaultHttpHeaders();

        HttpConversionUtil.AddHttp3ToHttpHeaders(5, inHeaders, outHeaders, HttpVersion.HTTP_1_1, false, true);
        Assert.Equal(1, outHeaders.GetAll(HOST).Count);
        Assert.Equal(expectedHost, outHeaders.Get(HOST));
    }

    [Theory]
    [MemberData(nameof(ConflictingAuthorityAndHost))]
    public void AddHttp3ToHttpHeadersRejectsConflictingAuthorityAndHost(
            string authority, string[] hosts, bool hostAddedFirst)
    {
        IHttp3Headers inHeaders = NewAuthorityAndHostHeaders(authority, hosts, hostAddedFirst);
        HttpHeaders outHeaders = new DefaultHttpHeaders();

        Http3Exception exception = Assert.ThrowsAny<Http3Exception>(() =>
            HttpConversionUtil.AddHttp3ToHttpHeaders(5, inHeaders, outHeaders, HttpVersion.HTTP_1_1, false, true));
        Assert.Equal(Http3ErrorCode.H3_MESSAGE_ERROR, exception.ErrorCode());
    }

    // :authority, host, host header added first, expected value
    public static TheoryData<string, string[], bool, string> MatchingAuthorityAndHost()
    {
        return new TheoryData<string, string[], bool, string>
        {
            { "example.com", new[] { "example.com" }, false, "example.com" },
            { "Example.COM", new[] { "example.com" }, false, "Example.COM" },
            { "example.com", new[] { "Example.COM" }, false, "example.com" },
            { "example.com", new[] { "example.com" }, true, "example.com" },
            { "example.com", new[] { "example.com", "example.com" }, false, "example.com" },
            { null, new[] { "example.com", "example.com" }, false, "example.com" }
        };
    }

    // :authority, host, host header added first
    public static TheoryData<string, string[], bool> ConflictingAuthorityAndHost()
    {
        return new TheoryData<string, string[], bool>
        {
            { "public.example.com", new[] { "internal-admin.local" }, false },
            { "public.example.com", new[] { "internal-admin.local" }, true },
            { "public.example.com", new[] { "public.example.com", "internal-admin.local" }, false },
            { "example.com", new[] { "example.com:4433" }, false },
            { null, new[] { "public.example.com", "internal-admin.local" }, false }
        };
    }

    private static IHttp3Headers NewAuthorityAndHostHeaders(
            string authority, string[] hosts, bool hostAddedFirst)
    {
        IHttp3Headers headers = new DefaultHttp3Headers();

        if (!hostAddedFirst && authority != null)
        {
            headers.Authority(Cs(authority));
        }

        foreach (string host in hosts)
        {
            headers.Add(HOST, Cs(host));
        }

        if (hostAddedFirst && authority != null)
        {
            headers.Authority(Cs(authority));
        }

        return headers;
    }

    [Fact]
    public void ConnectionSpecificHeadersShouldBeRemoved()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(CONNECTION, "keep-alive");
        inHeaders.Add(HOST, "example.com");
        AsciiString keepAlive = KEEP_ALIVE;
        inHeaders.Add(keepAlive, "timeout=5, max=1000");
        AsciiString proxyConnection = PROXY_CONNECTION;
        inHeaders.Add(proxyConnection, "timeout=5, max=1000");
        inHeaders.Add(TRANSFER_ENCODING, "chunked");
        inHeaders.Add(UPGRADE, "h2c");

        IHttp3Headers outHeaders = new DefaultHttp3Headers();
        HttpConversionUtil.ToHttp3Headers(inHeaders, outHeaders);

        Assert.False(outHeaders.Contains(CONNECTION));
        Assert.False(outHeaders.Contains(HOST));
        Assert.False(outHeaders.Contains(keepAlive));
        Assert.False(outHeaders.Contains(proxyConnection));
        Assert.False(outHeaders.Contains(TRANSFER_ENCODING));
        Assert.False(outHeaders.Contains(UPGRADE));
    }

    [Fact]
    public void ToHttp3HeadersConnectUsesRequestTargetAsAuthorityIgnoringHost()
    {
        // The Host header intentionally conflicts with the CONNECT authority-form request-target. HTTP/3
        // must tunnel to the request-target, not to whatever a client claims via Host.
        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.CONNECT,
                "trusted.example:443");
        request.Headers().Add(HOST, "attacker.example:443");

        IHttp3Headers headers = HttpConversionUtil.ToHttp3Headers(request, true);

        AssertCsEquals(HttpMethod.CONNECT.AsciiName(), headers.Method());
        AssertCsEquals(new AsciiString("trusted.example:443"), headers.Authority());
        Assert.Null(headers.Scheme());
        Assert.Null(headers.Path());
        Assert.False(headers.Contains(HOST));
    }

    [Fact]
    public void ToHttp3HeadersConnectUsesRequestTargetAsAuthorityWithoutHostHeader()
    {
        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.CONNECT,
                "trusted.example:443");

        IHttp3Headers headers = HttpConversionUtil.ToHttp3Headers(request, true);

        AssertCsEquals(new AsciiString("trusted.example:443"), headers.Authority());
        Assert.Null(headers.Scheme());
        Assert.Null(headers.Path());
    }

    [Theory]
    [InlineData("trusted.example:443@attacker.example:443")]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("http://www.example.com:80")]
    [InlineData("trusted.example:443/../attacker.example")]
    public void ConnectAuthorityFormInvalid(string uri)
    {
        IHttpRequest msg = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.CONNECT, uri);
        Assert.ThrowsAny<ArgumentException>(() => HttpConversionUtil.ToHttp3Headers(msg, true));
    }

    [Fact]
    public void Http3ToHttpHeaderTest()
    {
        IHttp3Headers http3Headers = new DefaultHttp3Headers();
        http3Headers.Status(Cs("200"));
        http3Headers.Path(Cs("/meow")); // HTTP/2 Header response should not contain 'path' in response.
        http3Headers.Set(Cs("cat"), Cs("meow"));

        HttpHeaders httpHeaders = new DefaultHttpHeaders();
        HttpConversionUtil.AddHttp3ToHttpHeaders(3, http3Headers, httpHeaders, HttpVersion.HTTP_1_1, false, true);
        Assert.False(httpHeaders.Contains(HttpConversionUtil.ExtensionHeaderNames.PATH.Text()));
        Assert.Equal("meow", httpHeaders.Get("cat"));

        httpHeaders.Clear();
        HttpConversionUtil.AddHttp3ToHttpHeaders(3, http3Headers, httpHeaders, HttpVersion.HTTP_1_1, false, false);
        Assert.True(httpHeaders.Contains(HttpConversionUtil.ExtensionHeaderNames.PATH.Text()));
        Assert.Equal("meow", httpHeaders.Get("cat"));
    }

    [Fact]
    public void AbsoluteFormRequestTargetAuthorityTakesPrecedenceOverConflictingHost()
    {
        IHttpRequest msg = new DefaultHttpRequest(
            HttpVersion.HTTP_1_1, HttpMethod.GET, "http://request-target.example/admin");
        msg.Headers().Add(HOST, "host-header.example");

        IHttp3Headers @out = HttpConversionUtil.ToHttp3Headers(msg, true);

        AssertCsEquals(new AsciiString("/admin"), @out.Path());
        AssertCsEquals(new AsciiString("http"), @out.Scheme());
        AssertCsEquals(new AsciiString("request-target.example"), @out.Authority());
    }

    [Fact]
    public void AbsoluteFormRequestTargetAuthorityMatchingHostIsUnaffected()
    {
        IHttpRequest msg = new DefaultHttpRequest(
            HttpVersion.HTTP_1_1, HttpMethod.GET, "http://example.com/admin");
        msg.Headers().Add(HOST, "example.com");

        IHttp3Headers @out = HttpConversionUtil.ToHttp3Headers(msg, true);

        AssertCsEquals(new AsciiString("/admin"), @out.Path());
        AssertCsEquals(new AsciiString("http"), @out.Scheme());
        AssertCsEquals(new AsciiString("example.com"), @out.Authority());
    }

    [Fact]
    public void OriginFormRequestStillUsesHostAsAuthority()
    {
        IHttpRequest msg = new DefaultHttpRequest(
            HttpVersion.HTTP_1_1, HttpMethod.GET, "/admin");
        msg.Headers().Add(HOST, "host-header.example");
        msg.Headers().Add(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");

        IHttp3Headers @out = HttpConversionUtil.ToHttp3Headers(msg, true);

        AssertCsEquals(new AsciiString("/admin"), @out.Path());
        AssertCsEquals(new AsciiString("http"), @out.Scheme());
        AssertCsEquals(new AsciiString("host-header.example"), @out.Authority());
    }

    [Fact]
    public void AbsoluteFormRequestTargetWithUserInfoStripsUserInfoFromAuthority()
    {
        IHttpRequest msg = new DefaultHttpRequest(
            HttpVersion.HTTP_1_1, HttpMethod.GET, "http://user:pass@request-target.example/admin");
        msg.Headers().Add(HOST, "host-header.example");

        IHttp3Headers @out = HttpConversionUtil.ToHttp3Headers(msg, true);

        AssertCsEquals(new AsciiString("request-target.example"), @out.Authority());
    }

    [Fact]
    public void AbsoluteFormRequestTargetWithIPv6LiteralTakesPrecedenceOverHost()
    {
        IHttpRequest msg = new DefaultHttpRequest(
            HttpVersion.HTTP_1_1, HttpMethod.GET, "http://[::1]:8080/admin");
        msg.Headers().Add(HOST, "host-header.example");

        IHttp3Headers @out = HttpConversionUtil.ToHttp3Headers(msg, true);

        AssertCsEquals(new AsciiString("[::1]:8080"), @out.Authority());
    }

    [Fact]
    public void AbsoluteFormRequestTargetWithExplicitPortTakesPrecedenceOverHost()
    {
        IHttpRequest msg = new DefaultHttpRequest(
            HttpVersion.HTTP_1_1, HttpMethod.GET, "http://request-target.example:8080/admin");
        msg.Headers().Add(HOST, "host-header.example");

        IHttp3Headers @out = HttpConversionUtil.ToHttp3Headers(msg, true);

        AssertCsEquals(new AsciiString("request-target.example:8080"), @out.Authority());
    }

    // CLR: a Java String passed as a CharSequence (see the class comment).
    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }

    // CLR: the text of a CharSequence that Java compares with a String (null stays null).
    private static string ToJavaString(ICharSequence s)
    {
        return s?.ToString();
    }

    // CLR: assertEquals(Object, Object) of two CharSequences, which calls equals (see the class comment).
    private static void AssertCsEquals(ICharSequence expected, ICharSequence actual)
    {
        Assert.True(Equals(expected, actual), "expected: <" + expected + "> but was: <" + actual + ">");
    }
}
