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

using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: Http3HeadersSink is internal (package-private in Java) and reached through InternalsVisibleTo;
// accept(CharSequence, CharSequence) is Accept(ICharSequence, ICharSequence), so a Java String passed as a CharSequence
// is a StringCharSequence (Http3TestUtils.Cs), and Http3Headers.PseudoHeaderName.X.value() is
// IHttp3Headers.PseudoHeaderName.X.Value() (an AsciiString). new AsciiString(CharSequence) is the
// AsciiString(ICharSequence) constructor. assertThrows(T.class, ...) is Assert.ThrowsAny<T>; the @MethodSource tests
// are [Theory]s with [MemberData] over public static TheoryData (xUnit reads public data members; List<String> is
// string[], as HttpConversionUtilTest), with the same rows; Stream<Arguments> and Arguments.of are TheoryData rows.
public class Http3HeadersSinkTest
{

    [Fact]
    public void TestHeaderSizeExceeded()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 32, false, false);
        AddMandatoryPseudoHeaders(sink, false);

        Http3Exception e = Assert.ThrowsAny<Http3Exception>(() => sink.Finish());
        Http3TestUtils.AssertException(Http3ErrorCode.H3_EXCESSIVE_LOAD, e);
    }

    [Fact]
    public void TestHeaderSizeNotExceed()
    {
        IHttp3Headers headers = new DefaultHttp3Headers();
        Http3HeadersSink sink = new Http3HeadersSink(headers, 64, false, false);
        AddMandatoryPseudoHeaders(sink, false);
        sink.Finish();
    }

    [Fact]
    public void TestPseudoHeaderFollowsNormalHeader()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(Cs("name"), Cs("value"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.AUTHORITY.Value(), Cs("value"));
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    [Fact]
    public void TestInvalidatePseudoHeader()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(Cs(":invalid"), Cs("value"));
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    [Fact]
    public void TestMixRequestResponsePseudoHeaders()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("value"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.STATUS.Value(), Cs("value"));
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    [Fact]
    public void TestValidPseudoHeadersRequest()
    {
        IHttp3Headers headers = new DefaultHttp3Headers();
        Http3HeadersSink sink = new Http3HeadersSink(headers, 512, true, false);
        AddMandatoryPseudoHeaders(sink, true);
        sink.Finish();
    }

    [Fact]
    public void TestValidPseudoHeadersResponse()
    {
        IHttp3Headers headers = new DefaultHttp3Headers();
        Http3HeadersSink sink = new Http3HeadersSink(headers, 512, true, false);
        AddMandatoryPseudoHeaders(sink, false);
        sink.Finish();
    }

    [Fact]
    public void TestDuplicatePseudoHeader()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        AddMandatoryPseudoHeaders(sink, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.AUTHORITY.Value(), Cs("value"));
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    [Fact]
    public void TestMandatoryPseudoHeaderMissingRequest()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("GET"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.PATH.Value(), Cs("/"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.SCHEME.Value(), Cs("https"));
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    [Fact]
    public void TestMandatoryPseudoHeaderMissingResponse()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    [Fact]
    public void TestInvalidPseudoHeadersForConnect()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("CONNECT"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.PATH.Value(), Cs("/"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.SCHEME.Value(), Cs("https"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.AUTHORITY.Value(), Cs("value"));
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    [Fact]
    public void TestValidPseudoHeadersForConnect()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("CONNECT"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.AUTHORITY.Value(), Cs("value"));
        sink.Finish();
    }

    [Fact]
    public void TestTrailersWithRequestPseudoHeaders()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, true);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("CONNECT"));
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    [Fact]
    public void TestTrailersWithResponsePseudoHeaders()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, true);
        sink.Accept(IHttp3Headers.PseudoHeaderName.STATUS.Value(), Cs("200"));
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    [Fact]
    public void TestAuthorityNotRequiredForOptionsWildcard()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("OPTIONS"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.PATH.Value(), Cs("*"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.SCHEME.Value(), Cs("https"));
        sink.Finish();
    }

    [Fact]
    public void TestOptionsNonWildcardWithAuthority()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("OPTIONS"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.PATH.Value(), Cs("/something"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.SCHEME.Value(), Cs("https"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.AUTHORITY.Value(), Cs("example.com:4433"));
        sink.Finish();
    }

    [Fact]
    public void TestOptionsNonWildcardWithHost()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("OPTIONS"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.PATH.Value(), Cs("/something"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.SCHEME.Value(), Cs("https"));
        sink.Accept(new AsciiString(HttpHeaderNames.HOST), Cs("example.com:4433"));
        sink.Finish();
    }

    [Fact]
    public void TestAuthorityOrHostRequiredForOptionsNonWildcard()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("OPTIONS"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.PATH.Value(), Cs("/something"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.SCHEME.Value(), Cs("https"));
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    [Fact]
    public void TestHostExistsInsteadOfAuthority()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("GET"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.PATH.Value(), Cs("/"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.SCHEME.Value(), Cs("https"));
        sink.Accept(new AsciiString(HttpHeaderNames.HOST), Cs("example.com:4433"));
        sink.Finish();
    }

    [Theory]
    [MemberData(nameof(MatchingAuthorityAndHost))]
    public void TestMatchingAuthorityAndHost(string authority, string[] hosts)
    {
        Http3HeadersSink sink = NewRequestSink(authority, hosts);
        sink.Finish();
    }

    [Theory]
    [MemberData(nameof(ConflictingAuthorityAndHost))]
    public void TestConflictingAuthorityAndHost(string authority, string[] hosts)
    {
        Http3HeadersSink sink = NewRequestSink(authority, hosts);
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    // :authority, host
    public static TheoryData<string, string[]> MatchingAuthorityAndHost()
    {
        return new TheoryData<string, string[]>
        {
            { "example.com:4433", new[] { "example.com:4433" } },
            { "Example.COM:4433", new[] { "example.com:4433" } },
            { "example.com:4433", new[] { "example.com:4433", "example.com:4433" } },
            { null, new[] { "example.com:4433" } },
            { null, new[] { "example.com:4433", "example.com:4433" } }
        };
    }

    // :authority, host
    public static TheoryData<string, string[]> ConflictingAuthorityAndHost()
    {
        return new TheoryData<string, string[]>
        {
            { "public.example.com", new[] { "internal-admin.local" } },
            { "public.example.com", new[] { "public.example.com", "internal-admin.local" } },
            { "example.com", new[] { "example.com:4433" } },
            { null, new[] { "public.example.com", "internal-admin.local" } }
        };
    }

    private static Http3HeadersSink NewRequestSink(string authority, string[] hosts)
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("GET"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.PATH.Value(), Cs("/"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.SCHEME.Value(), Cs("https"));
        if (authority != null)
        {
            sink.Accept(IHttp3Headers.PseudoHeaderName.AUTHORITY.Value(), Cs(authority));
        }
        foreach (string host in hosts)
        {
            sink.Accept(new AsciiString(HttpHeaderNames.HOST), Cs(host));
        }
        return sink;
    }

    [Fact]
    public void TestConflictingAuthorityAndHostForConnect()
    {
        Http3HeadersSink sink = new Http3HeadersSink(new DefaultHttp3Headers(), 512, true, false);
        sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("CONNECT"));
        sink.Accept(IHttp3Headers.PseudoHeaderName.AUTHORITY.Value(), Cs("public.example.com"));
        sink.Accept(new AsciiString(HttpHeaderNames.HOST), Cs("internal-admin.local"));
        Assert.ThrowsAny<Http3HeadersValidationException>(() => sink.Finish());
    }

    private static void AddMandatoryPseudoHeaders(Http3HeadersSink sink, bool req)
    {
        if (req)
        {
            sink.Accept(IHttp3Headers.PseudoHeaderName.METHOD.Value(), Cs("GET"));
            sink.Accept(IHttp3Headers.PseudoHeaderName.PATH.Value(), Cs("/"));
            sink.Accept(IHttp3Headers.PseudoHeaderName.SCHEME.Value(), Cs("https"));
            sink.Accept(IHttp3Headers.PseudoHeaderName.AUTHORITY.Value(), Cs("value"));
        }
        else
        {
            sink.Accept(IHttp3Headers.PseudoHeaderName.STATUS.Value(), Cs("200"));
        }
    }
}
