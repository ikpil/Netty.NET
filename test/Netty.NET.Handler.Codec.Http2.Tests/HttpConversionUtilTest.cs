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

using System;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the @Deprecated DefaultHttpRequest(HttpVersion, HttpMethod, String, boolean validateHeaders) is not ported
// in codec-http (its @deprecated text names the (HttpVersion, HttpMethod, String) constructor, "to always have
// header validation enabled"; docs/codec-http-porting.md, Core not ported); every call here passes true, so it is
// that constructor. new DefaultHttpHeaders(false) is
// DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders(), and the deprecated
// HttpHeaderNames.KEEP_ALIVE/PROXY_CONNECTION are local AsciiString constants with the same names. A Java String
// passed as a CharSequence is a StringCharSequence (Cs), because a C# string is not an ICharSequence. assertEquals
// of two CharSequences calls equals, which AssertCsEquals keeps (xUnit would compare two ICharSequence values,
// IReadOnlyList<char>, element by element), and a CharSequence result that Java compares with a String is compared
// as a string. assertSame("world", out.get("hello")) and assertSame of the cookie literal hold in Java because the
// conversion copies the String instance the HttpHeaders store; here the headers store an ICharSequence for the C#
// string, so the assertion compares the text (the AsciiString assertSame(TRAILERS,...) checks stay reference
// checks). assertThrows(T.class,...) is Assert.ThrowsAny<T> and the anonymous Executable classes are lambdas. The
// @ValueSource(strings) test is a [Theory] with InlineData rows. The static imports of HttpHeaderValues.GZIP and
// TRAILERS are written HttpHeaderValues.GZIP/TRAILERS: C# imports all static members of a class at once, and
// HttpHeaderValues.UPGRADE would clash with HttpHeaderNames.UPGRADE (CS0229).
public class HttpConversionUtilTest
{

    private static readonly AsciiString KEEP_ALIVE = AsciiString.Cached("keep-alive");
    private static readonly AsciiString PROXY_CONNECTION = AsciiString.Cached("proxy-connection");

    [Fact]
    public void ConnectNoPath()
    {
        string authority = "netty.io:80";
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Authority(Cs(authority));
        headers.Method(HttpMethod.CONNECT.AsciiName());
        IHttpRequest request = HttpConversionUtil.ToHttpRequest(0, headers, true);
        Assert.NotNull(request);
        Assert.Equal(authority, request.Uri());
        Assert.Equal(authority, request.Headers().Get(HOST));

        // Regular CONNECT (RFC 9113) must not carry Extended CONNECT (RFC 8441) state.
        Assert.False(request.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text()));
        Assert.False(request.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PATH.Text()));
    }

    [Fact]
    public void ExtendedConnectPreservesProtocolAndPathAsExtensionHeaders()
    {
        string authority = "ws.example:443";
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Method(HttpMethod.CONNECT.AsciiName());
        headers.Authority(Cs(authority));
        headers.Scheme(Cs("https"));
        headers.Path(Cs("/admin/ws"));
        headers.Add(IHttp2Headers.PseudoHeaderName.PROTOCOL.Value(), Cs("websocket"));

        IHttpRequest request = HttpConversionUtil.ToHttpRequest(0, headers, true);
        Assert.NotNull(request);

        // The request-target/URI for a CONNECT request stays the authority, same as a regular CONNECT
        // request: this fix does not change wire-level CONNECT behavior.
        Assert.Equal(authority, request.Uri());
        Assert.Equal(HttpMethod.CONNECT, request.Method());
        Assert.Equal(authority, request.Headers().Get(HOST));

        // But the Extended CONNECT (RFC 8441) state, which changes CONNECT semantics such that ':authority'
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

        IHttp2Headers regularConnect = new DefaultHttp2Headers();
        regularConnect.Method(HttpMethod.CONNECT.AsciiName());
        regularConnect.Authority(Cs(authority));
        IHttpRequest regularConnectRequest = HttpConversionUtil.ToHttpRequest(0, regularConnect, true);

        IHttp2Headers extendedConnect = new DefaultHttp2Headers();
        extendedConnect.Method(HttpMethod.CONNECT.AsciiName());
        extendedConnect.Authority(Cs(authority));
        extendedConnect.Scheme(Cs("https"));
        extendedConnect.Path(Cs("/admin/ws"));
        extendedConnect.Add(IHttp2Headers.PseudoHeaderName.PROTOCOL.Value(), Cs("websocket"));
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
        IHttp2Headers regularConnect = new DefaultHttp2Headers();
        regularConnect.Method(HttpMethod.CONNECT.AsciiName());
        regularConnect.Authority(Cs("ws.example:443"));
        regularConnect.Add(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text(), Cs("websocket"));
        IHttpRequest regularConnectRequest = HttpConversionUtil.ToHttpRequest(0, regularConnect, true);
        Assert.False(regularConnectRequest.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text()));
    }

    [Fact]
    public void ConnectAuthorityFormUsesRequestTargetNotHost()
    {
        bool validateHeaders = true;
        IHttpRequest msg = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.CONNECT, "trusted.example:443");
        msg.Headers().Add(HOST, "attacker.example:443");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, validateHeaders);

        AssertCsEquals(HttpMethod.CONNECT.AsciiName(), @out.Method());
        AssertCsEquals(new AsciiString("trusted.example:443"), @out.Authority());
        Assert.Null(@out.Scheme());
        Assert.Null(@out.Path());
    }

    [Fact]
    public void ConnectAuthorityFormWithMatchingHostOmitsSchemeAndPath()
    {
        bool validateHeaders = true;
        IHttpRequest msg = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.CONNECT, "trusted.example:443");
        msg.Headers().Add(HOST, "trusted.example:443");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, validateHeaders);

        AssertCsEquals(HttpMethod.CONNECT.AsciiName(), @out.Method());
        AssertCsEquals(new AsciiString("trusted.example:443"), @out.Authority());
        Assert.Null(@out.Scheme());
        Assert.Null(@out.Path());
    }

    [Fact]
    public void ConnectAuthorityFormWithoutHostStillUsesRequestTarget()
    {
        bool validateHeaders = true;
        IHttpRequest msg = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.CONNECT, "trusted.example:443");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, validateHeaders);

        AssertCsEquals(HttpMethod.CONNECT.AsciiName(), @out.Method());
        AssertCsEquals(new AsciiString("trusted.example:443"), @out.Authority());
        Assert.Null(@out.Scheme());
        Assert.Null(@out.Path());
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
        Assert.ThrowsAny<ArgumentException>(() => HttpConversionUtil.ToHttp2Headers(msg, true));
    }

    [Fact]
    public void SetHttp2AuthorityWithoutUserInfo()
    {
        IHttp2Headers headers = new DefaultHttp2Headers();

        HttpConversionUtil.SetHttp2Authority("foo", headers);
        AssertCsEquals(new AsciiString("foo"), headers.Authority());
    }

    [Fact]
    public void SetHttp2AuthorityWithUserInfo()
    {
        IHttp2Headers headers = new DefaultHttp2Headers();

        HttpConversionUtil.SetHttp2Authority("info@foo", headers);
        AssertCsEquals(new AsciiString("foo"), headers.Authority());

        HttpConversionUtil.SetHttp2Authority("@foo.bar", headers);
        AssertCsEquals(new AsciiString("foo.bar"), headers.Authority());
    }

    [Fact]
    public void SetHttp2AuthorityNullOrEmpty()
    {
        IHttp2Headers headers = new DefaultHttp2Headers();

        HttpConversionUtil.SetHttp2Authority(null, headers);
        Assert.Null(headers.Authority());

        // https://datatracker.ietf.org/doc/html/rfc9113#section-8.3.1
        // Clients that generate HTTP/2 requests directly MUST use the ":authority" pseudo-header
        // field to convey authority information, unless there is no authority information to convey
        // (in which case it MUST NOT generate ":authority").
        // An intermediary that forwards a request over HTTP/2 MUST construct an ":authority" pseudo-header
        // field using the authority information from the control data of the original request, unless the
        // original request's target URI does not contain authority information
        // (in which case it MUST NOT generate ":authority").
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            HttpConversionUtil.SetHttp2Authority("", new DefaultHttp2Headers());
        });
    }

    [Fact]
    public void SetHttp2AuthorityWithEmptyAuthority()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            HttpConversionUtil.SetHttp2Authority("info@", new DefaultHttp2Headers());
        });
    }

    [Fact]
    public void StripTEHeaders()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(TE, HttpHeaderValues.GZIP);
        IHttp2Headers @out = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, @out);
        Assert.True(@out.IsEmpty());
    }

    [Fact]
    public void StripTEHeadersExcludingTrailers()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(TE, HttpHeaderValues.GZIP);
        inHeaders.Add(TE, HttpHeaderValues.TRAILERS);
        IHttp2Headers @out = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, @out);
        Assert.Same(HttpHeaderValues.TRAILERS, @out.Get(TE));
    }

    [Fact]
    public void StripTEHeadersCsvSeparatedExcludingTrailers()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(TE, HttpHeaderValues.GZIP + "," + HttpHeaderValues.TRAILERS);
        IHttp2Headers @out = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, @out);
        Assert.Same(HttpHeaderValues.TRAILERS, @out.Get(TE));
    }

    [Fact]
    public void StripTEHeadersCsvSeparatedAccountsForValueSimilarToTrailers()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(TE, HttpHeaderValues.GZIP + "," + HttpHeaderValues.TRAILERS + "foo");
        IHttp2Headers @out = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, @out);
        Assert.False(@out.Contains(TE));
    }

    [Fact]
    public void StripTEHeadersAccountsForValueSimilarToTrailers()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(TE, HttpHeaderValues.TRAILERS + "foo");
        IHttp2Headers @out = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, @out);
        Assert.False(@out.Contains(TE));
    }

    [Fact]
    public void StripTEHeadersAccountsForOWS()
    {
        HttpHeaders inHeaders = DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders();
        inHeaders.Add(TE, " " + HttpHeaderValues.TRAILERS + ' ');
        IHttp2Headers @out = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, @out);
        Assert.Same(HttpHeaderValues.TRAILERS, @out.Get(TE));
    }

    [Fact]
    public void StripConnectionHeadersAndNominees()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(CONNECTION, "foo");
        inHeaders.Add("foo", "bar");
        IHttp2Headers @out = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, @out);
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
        IHttp2Headers @out = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, @out);
        Assert.Equal(1, @out.Size());
        Assert.Equal("world", ToJavaString(@out.Get(Cs("hello"))));
    }

    [Fact]
    public void CookieNoSpace()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(COOKIE, "one=foo;two=bar");
        IHttp2Headers @out = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, @out);
        Assert.Equal("one=foo;two=bar", ToJavaString(@out.Get(COOKIE))); // not split
    }

    [Fact]
    public void CookieTailSemicolon()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(COOKIE, "one=foo;");
        IHttp2Headers @out = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, @out);
        Assert.Equal("one=foo;", ToJavaString(@out.Get(COOKIE))); // not split
    }

    [Fact]
    public void CookieNonAscii()
    {
        HttpHeaders inHeaders = new DefaultHttpHeaders();
        inHeaders.Add(COOKIE, "one=🙃; two=ü");
        IHttp2Headers @out = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, @out);
        Assert.Equal("one=🙃; two=ü", ToJavaString(@out.Get(COOKIE))); // not split
    }

    [Fact]
    public void HandlesRequest()
    {
        bool validateHeaders = true;
        IHttpRequest msg = new DefaultHttpRequest(
            HttpVersion.HTTP_1_1, HttpMethod.GET, "http://example.com/path/to/something");
        HttpHeaders inHeaders = msg.Headers();
        inHeaders.Add(CONNECTION, "foo,  bar");
        inHeaders.Add("hello", "world");
        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, validateHeaders);
        AssertCsEquals(new AsciiString("/path/to/something"), @out.Path());
        AssertCsEquals(new AsciiString("http"), @out.Scheme());
        AssertCsEquals(new AsciiString("example.com"), @out.Authority());
        AssertCsEquals(HttpMethod.GET.AsciiName(), @out.Method());
        Assert.Equal("world", ToJavaString(@out.Get(Cs("hello"))));
    }

    [Fact]
    public void HandlesRequestWithDoubleSlashPath()
    {
        bool validateHeaders = true;
        IHttpRequest msg = new DefaultHttpRequest(
            HttpVersion.HTTP_1_1, HttpMethod.GET, "//path/to/something");
        HttpHeaders inHeaders = msg.Headers();
        inHeaders.Add(CONNECTION, "foo,  bar");
        inHeaders.Add(HOST, "example.com");
        inHeaders.Add(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");
        inHeaders.Add("hello", "world");
        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, validateHeaders);
        AssertCsEquals(new AsciiString("//path/to/something"), @out.Path());
        AssertCsEquals(new AsciiString("http"), @out.Scheme());
        AssertCsEquals(new AsciiString("example.com"), @out.Authority());
        AssertCsEquals(HttpMethod.GET.AsciiName(), @out.Method());
    }

    [Fact]
    public void HandlesAbsoluteRequestWhoseQueryHasUrlCharactersRejectedByJavaNetUri()
    {
        IHttpRequest msg = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "https://bh.contextweb.com/bh/rtset?pid=558355&ev=1&us_privacy=${us_privacy}");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, false);

        AssertCsEquals(new AsciiString("/bh/rtset?pid=558355&ev=1&us_privacy=${us_privacy}"), @out.Path());
        AssertCsEquals(new AsciiString("https"), @out.Scheme());
        AssertCsEquals(new AsciiString("bh.contextweb.com"), @out.Authority());
        AssertCsEquals(HttpMethod.GET.AsciiName(), @out.Method());
    }

    [Fact]
    public void HandlesAbsoluteRequestWhosePathHasUrlCharactersRejectedByJavaNetUri()
    {
        IHttpRequest msg = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "http://example.com/orders/{id}/items|details?expand={details}#section");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, false);

        AssertCsEquals(new AsciiString("/orders/{id}/items|details?expand={details}#section"), @out.Path());
        AssertCsEquals(new AsciiString("http"), @out.Scheme());
        AssertCsEquals(new AsciiString("example.com"), @out.Authority());
    }

    [Fact]
    public void HandlesAbsoluteRequestWithoutPathUsingVertxCompatiblePath()
    {
        IHttpRequest msg = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://example.com?x=1#frag");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, true);

        AssertCsEquals(new AsciiString("/?x=1#frag"), @out.Path());
        AssertCsEquals(new AsciiString("http"), @out.Scheme());
        AssertCsEquals(new AsciiString("example.com"), @out.Authority());
    }

    [Fact]
    public void HandlesAbsoluteRequestWithAuthorityOnlyUsingVertxCompatiblePath()
    {
        IHttpRequest msg = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://example.com");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, true);

        AssertCsEquals(new AsciiString("/"), @out.Path());
        AssertCsEquals(new AsciiString("http"), @out.Scheme());
        AssertCsEquals(new AsciiString("example.com"), @out.Authority());
    }

    [Fact]
    public void HandlesAbsoluteRequestWithoutPathWhoseQueryOrFragmentContainsSlash()
    {
        IHttpRequest querySlash = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://example.com?next=/home");
        IHttpRequest fragmentSlash = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://example.com#/home");

        AssertCsEquals(new AsciiString("/?next=/home"),
                HttpConversionUtil.ToHttp2Headers(querySlash, true).Path());
        AssertCsEquals(new AsciiString("/"), HttpConversionUtil.ToHttp2Headers(fragmentSlash, true).Path());
    }

    [Fact]
    public void HandlesEmptyRequestTargetUsingLegacyEmptyPathFallback()
    {
        IHttpRequest msg = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "");
        msg.Headers().Add(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, true);

        AssertCsEquals(new AsciiString("/"), @out.Path());
        AssertCsEquals(new AsciiString("http"), @out.Scheme());
        Assert.Null(@out.Authority());
    }

    [Fact]
    public void HandlesAbsoluteRequestWithMissingAuthorityUsingUriAuthority()
    {
        IHttpRequest msg = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://?x=1#frag");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, true);

        AssertCsEquals(new AsciiString("/?x=1#frag"), @out.Path());
        AssertCsEquals(new AsciiString("http"), @out.Scheme());
        Assert.Null(@out.Authority());
    }

    [Fact]
    public void HandlesAbsoluteRequestWithEmptyQueryOrFragmentUsingVertxCompatiblePath()
    {
        IHttpRequest emptyQuery = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://example.com/path?");
        IHttpRequest emptyFragment = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://example.com/path#");
        IHttpRequest emptyQueryWithFragment = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://example.com/path?#frag");
        IHttpRequest queryWithEmptyFragment = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://example.com/path?x#");

        AssertCsEquals(new AsciiString("/path"), HttpConversionUtil.ToHttp2Headers(emptyQuery, true).Path());
        AssertCsEquals(new AsciiString("/path"), HttpConversionUtil.ToHttp2Headers(emptyFragment, true).Path());
        AssertCsEquals(new AsciiString("/path#frag"),
                HttpConversionUtil.ToHttp2Headers(emptyQueryWithFragment, true).Path());
        AssertCsEquals(new AsciiString("/path?x"),
                HttpConversionUtil.ToHttp2Headers(queryWithEmptyFragment, true).Path());
    }

    [Fact]
    public void AbsoluteFormRequestTargetAuthorityTakesPrecedenceOverConflictingHost()
    {
        IHttpRequest msg = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://request-target.example/admin");
        msg.Headers().Add(HOST, "host-header.example");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, true);

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

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, true);

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

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, true);

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

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, true);

        AssertCsEquals(new AsciiString("request-target.example"), @out.Authority());
    }

    [Fact]
    public void AbsoluteFormRequestTargetWithIPv6LiteralTakesPrecedenceOverHost()
    {
        IHttpRequest msg = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://[::1]:8080/admin");
        msg.Headers().Add(HOST, "host-header.example");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, true);

        AssertCsEquals(new AsciiString("[::1]:8080"), @out.Authority());
    }

    [Fact]
    public void AbsoluteFormRequestTargetWithExplicitPortTakesPrecedenceOverHost()
    {
        IHttpRequest msg = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "http://request-target.example:8080/admin");
        msg.Headers().Add(HOST, "host-header.example");

        IHttp2Headers @out = HttpConversionUtil.ToHttp2Headers(msg, true);

        AssertCsEquals(new AsciiString("request-target.example:8080"), @out.Authority());
    }

    [Fact]
    public void RejectsAbsoluteRequestWithMalformedAuthority()
    {
        IHttpRequest msg = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET,
                "http://[bad host]/p?q={x}",
                new DefaultHttpHeaders(),
                false);

        Assert.ThrowsAny<ArgumentException>(() =>
        {
            HttpConversionUtil.ToHttp2Headers(msg, false);
        });
    }

    [Fact]
    public void AddHttp2ToHttpHeadersCombinesCookies()
    {
        IHttp2Headers inHeaders = new DefaultHttp2Headers();
        inHeaders.Add(Cs("yes"), Cs("no"));
        inHeaders.Add(COOKIE, Cs("foo=bar"));
        inHeaders.Add(COOKIE, Cs("bax=baz"));

        HttpHeaders outHeaders = new DefaultHttpHeaders();

        HttpConversionUtil.AddHttp2ToHttpHeaders(5, inHeaders, outHeaders, HttpVersion.HTTP_1_1, false, false);
        Assert.Equal("no", outHeaders.Get("yes"));
        Assert.Equal("foo=bar; bax=baz", outHeaders.Get(COOKIE.ToString()));
    }

    [Fact]
    public void AddHttp2ToHttpHeadersDeduplicatesMatchingAuthorityAndHost()
    {
        IHttp2Headers inHeaders = new DefaultHttp2Headers();
        inHeaders.Authority(Cs("example.com"));
        inHeaders.Add(HOST, Cs("example.com"));

        HttpHeaders outHeaders = new DefaultHttpHeaders();

        HttpConversionUtil.AddHttp2ToHttpHeaders(5, inHeaders, outHeaders, HttpVersion.HTTP_1_1, false, true);
        Assert.Equal(1, outHeaders.GetAll(HOST).Count);
        Assert.Equal("example.com", outHeaders.Get(HOST));
    }

    [Fact]
    public void AddHttp2ToHttpHeadersRejectsConflictingAuthorityAndHost()
    {
        IHttp2Headers inHeaders = new DefaultHttp2Headers();
        inHeaders.Authority(Cs("public.example.com"));
        inHeaders.Add(HOST, Cs("internal-admin.local"));

        HttpHeaders outHeaders = new DefaultHttpHeaders();

        Http2Exception exception = Assert.ThrowsAny<Http2Exception>(() =>
        {
            HttpConversionUtil.AddHttp2ToHttpHeaders(5, inHeaders, outHeaders, HttpVersion.HTTP_1_1, false, true);
        });
        Assert.Equal(Http2Error.PROTOCOL_ERROR, exception.Error());
        // No Host header should have leaked through in an inconsistent state.
        Assert.False(outHeaders.Contains(HOST) && outHeaders.GetAll(HOST).Count > 1);
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

        IHttp2Headers outHeaders = new DefaultHttp2Headers();
        HttpConversionUtil.ToHttp2Headers(inHeaders, outHeaders);

        Assert.False(outHeaders.Contains(CONNECTION));
        Assert.False(outHeaders.Contains(HOST));
        Assert.False(outHeaders.Contains(keepAlive));
        Assert.False(outHeaders.Contains(proxyConnection));
        Assert.False(outHeaders.Contains(TRANSFER_ENCODING));
        Assert.False(outHeaders.Contains(UPGRADE));
    }

    [Fact]
    public void Http2ToHttpHeaderTest()
    {
        IHttp2Headers http2Headers = new DefaultHttp2Headers();
        http2Headers.Status(Cs("200"));
        http2Headers.Path(Cs("/meow")); // HTTP/2 Header response should not contain 'path' in response.
        http2Headers.Set(Cs("cat"), Cs("meow"));

        HttpHeaders httpHeaders = new DefaultHttpHeaders();
        HttpConversionUtil.AddHttp2ToHttpHeaders(3, http2Headers, httpHeaders, HttpVersion.HTTP_1_1, false, true);
        Assert.False(httpHeaders.Contains(HttpConversionUtil.ExtensionHeaderNames.PATH.Text()));
        Assert.Equal("meow", httpHeaders.Get("cat"));

        httpHeaders.Clear();
        HttpConversionUtil.AddHttp2ToHttpHeaders(3, http2Headers, httpHeaders, HttpVersion.HTTP_1_1, false, false);
        Assert.True(httpHeaders.Contains(HttpConversionUtil.ExtensionHeaderNames.PATH.Text()));
        Assert.Equal("meow", httpHeaders.Get("cat"));
    }

    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }

    // CLR: the text of a CharSequence that Java compares with a String (null stays null).
    private static string ToJavaString(ICharSequence s)
    {
        return s?.ToString();
    }

    private static void AssertCsEquals(ICharSequence expected, ICharSequence actual)
    {
        Assert.True(Equals(expected, actual), "expected: <" + expected + "> but was: <" + actual + ">");
    }
}
