/*
 * Copyright 2015 The Netty Project
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
using System.Linq;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.AsciiString;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpHeaderValues;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: CharSequence constants are StringCharSequence values (a C# string is not an ICharSequence). The @Deprecated
// DefaultHttpHeaders(boolean validate) names DefaultHttpHeaders() and is not ported (owner decision 2026-11-16):
// new DefaultHttpHeaders(true) is new DefaultHttpHeaders(), and new DefaultHttpHeaders(false) is
// DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders() (rule 7). Java's add(null, "foo")
// selects the String overload, so the C# call casts null to string. assertEquals(headers1, headers2) calls
// equals(Object); xUnit's Assert.Equal compares an IEnumerable (HttpHeaders enumerates its entries) element by element,
// which would compare the case-differing names, so the header comparisons assert Equals directly. asList(...) is a
// List<string>, which binds to the Iterable<?> overload through IEnumerable<object> covariance; Collections.singleton
// is a one-element array. Set.toArray(EMPTY_STRINGS) is ToArray(); assertArrayEquals is Assert.Equal of the arrays.
// NullPointerException from checkNotNull is ArgumentNullException and IllegalArgumentException is ArgumentException
// (docs/codec-base-porting.md).
public class DefaultHttpHeadersTest
{
    private static readonly ICharSequence HEADER_NAME = new StringCharSequence("testHeader");
    private static readonly ICharSequence ILLEGAL_VALUE = new StringCharSequence("testHeader\r\nContent-Length:45\r\n\r\n");

    [Fact]
    public void NullHeaderNameNotAllowed()
    {
        Assert.Throws<ArgumentException>(() => new DefaultHttpHeaders().Add((string) null, "foo"));
    }

    [Fact]
    public void EmptyHeaderNameNotAllowed()
    {
        Assert.Throws<ArgumentException>(() => new DefaultHttpHeaders().Add(StringUtil.EMPTY_STRING, "foo"));
    }

    [Fact]
    public void KeysShouldBeCaseInsensitive()
    {
        DefaultHttpHeaders headers = new DefaultHttpHeaders();
        headers.Add(Of("Name"), Of("value1"));
        headers.Add(Of("name"), Of("value2"));
        headers.Add(Of("NAME"), Of("value3"));
        Assert.Equal(3, headers.Size());

        IList<string> values = new List<string> { "value1", "value2", "value3" };

        Assert.Equal(values, headers.GetAll(Of("NAME")));
        Assert.Equal(values, headers.GetAll(Of("name")));
        Assert.Equal(values, headers.GetAll(Of("Name")));
        Assert.Equal(values, headers.GetAll(Of("nAmE")));
    }

    [Fact]
    public void KeysShouldBeCaseInsensitiveInHeadersEquals()
    {
        DefaultHttpHeaders headers1 = new DefaultHttpHeaders();
        headers1.Add(Of("name1"), new List<string> { "value1", "value2", "value3" });
        headers1.Add(Of("nAmE2"), Of("value4"));

        DefaultHttpHeaders headers2 = new DefaultHttpHeaders();
        headers2.Add(Of("naMe1"), new List<string> { "value1", "value2", "value3" });
        headers2.Add(Of("NAME2"), Of("value4"));

        Assert.True(headers1.Equals(headers1));
        Assert.True(headers2.Equals(headers2));
        Assert.True(headers1.Equals(headers2));
        Assert.True(headers2.Equals(headers1));
        Assert.Equal(headers1.GetHashCode(), headers2.GetHashCode());
    }

    [Fact]
    public void TestStringKeyRetrievedAsAsciiString()
    {
        HttpHeaders headers = DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders();

        // Test adding String key and retrieving it using a AsciiString key
        string connection = "keep-alive";
        headers.Add(Of("Connection"), connection);

        // Passes
        string value = headers.GetAsString(new StringCharSequence(HttpHeaderNames.CONNECTION.ToString()));
        Assert.NotNull(value);
        Assert.Equal(connection, value);

        // Passes
        string value2 = headers.GetAsString(HttpHeaderNames.CONNECTION);
        Assert.NotNull(value2);
        Assert.Equal(connection, value2);
    }

    [Fact]
    public void TestAsciiStringKeyRetrievedAsString()
    {
        HttpHeaders headers = DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders();

        // Test adding AsciiString key and retrieving it using a String key
        string cacheControl = "no-cache";
        headers.Add(HttpHeaderNames.CACHE_CONTROL, cacheControl);

        string value = headers.GetAsString(HttpHeaderNames.CACHE_CONTROL);
        Assert.NotNull(value);
        Assert.Equal(cacheControl, value);

        string value2 = headers.GetAsString(new StringCharSequence(HttpHeaderNames.CACHE_CONTROL.ToString()));
        Assert.NotNull(value2);
        Assert.Equal(cacheControl, value2);
    }

    [Fact]
    public void TestRemoveTransferEncodingIgnoreCase()
    {
        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, "Chunked");
        Assert.False(message.Headers().IsEmpty());
        HttpUtil.SetTransferEncodingChunked(message, false);
        Assert.True(message.Headers().IsEmpty());
    }

    // Test for https://github.com/netty/netty/issues/1690
    [Fact]
    public void TestGetOperations()
    {
        HttpHeaders headers = new DefaultHttpHeaders();
        headers.Add(Of("Foo"), Of("1"));
        headers.Add(Of("Foo"), Of("2"));

        Assert.Equal("1", headers.Get(Of("Foo")));

        IList<string> values = headers.GetAll(Of("Foo"));
        Assert.Equal(2, values.Count);
        Assert.Equal("1", values[0]);
        Assert.Equal("2", values[1]);
    }

    [Fact]
    public void TestEqualsIgnoreCase()
    {
        Assert.True(AsciiString.ContentEqualsIgnoreCase(null, null));
        Assert.False(AsciiString.ContentEqualsIgnoreCase(null, new StringCharSequence("foo")));
        Assert.False(AsciiString.ContentEqualsIgnoreCase(new StringCharSequence("bar"), null));
        Assert.True(AsciiString.ContentEqualsIgnoreCase(new StringCharSequence("FoO"), new StringCharSequence("fOo")));
    }

    [Fact]
    public void TestSetNullHeaderValueValidate()
    {
        HttpHeaders headers = new DefaultHttpHeaders();
        Assert.Throws<ArgumentNullException>(() => headers.Set(Of("test"), (ICharSequence) null));
    }

    [Fact]
    public void TestSetNullHeaderValueNotValidate()
    {
        HttpHeaders headers = DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders();
        Assert.Throws<ArgumentNullException>(() => headers.Set(Of("test"), (ICharSequence) null));
    }

    [Fact]
    public void AddCharSequences()
    {
        DefaultHttpHeaders headers = NewDefaultDefaultHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertDefaultValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void AddIterable()
    {
        DefaultHttpHeaders headers = NewDefaultDefaultHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertDefaultValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void AddObjects()
    {
        DefaultHttpHeaders headers = NewDefaultDefaultHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertDefaultValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void SetCharSequences()
    {
        DefaultHttpHeaders headers = NewDefaultDefaultHttpHeaders();
        headers.Set(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertDefaultValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void SetIterable()
    {
        DefaultHttpHeaders headers = NewDefaultDefaultHttpHeaders();
        headers.Set(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertDefaultValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void SetObjectObjects()
    {
        DefaultHttpHeaders headers = NewDefaultDefaultHttpHeaders();
        headers.Set(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertDefaultValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void SetObjectIterable()
    {
        DefaultHttpHeaders headers = NewDefaultDefaultHttpHeaders();
        headers.Set(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertDefaultValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void SetCharSequenceValidatesValue()
    {
        DefaultHttpHeaders headers = NewDefaultDefaultHttpHeaders();
        ArgumentException exception = Assert.Throws<ArgumentException>(() => headers.Set(HEADER_NAME, ILLEGAL_VALUE));
        // CLR: assertTrue(message.contains(name)) is Assert.Contains (an ordinal substring check, xUnit2009).
        Assert.Contains(HEADER_NAME.ToString(), exception.Message);
    }

    [Fact]
    public void SetIterableValidatesValue()
    {
        DefaultHttpHeaders headers = NewDefaultDefaultHttpHeaders();
        ArgumentException exception = Assert.Throws<ArgumentException>(
                () => headers.Set(HEADER_NAME, new[] { ILLEGAL_VALUE }));
        // CLR: assertTrue(message.contains(name)) is Assert.Contains (an ordinal substring check, xUnit2009).
        Assert.Contains(HEADER_NAME.ToString(), exception.Message);
    }

    [Fact]
    public void ToStringOnEmptyHeaders()
    {
        Assert.Equal("DefaultHttpHeaders[]", NewDefaultDefaultHttpHeaders().ToString());
    }

    [Fact]
    public void ToStringOnSingleHeader()
    {
        Assert.Equal("DefaultHttpHeaders[foo: bar]", NewDefaultDefaultHttpHeaders()
                .Add("foo", "bar")
                .ToString());
    }

    [Fact]
    public void ToStringOnMultipleHeaders()
    {
        Assert.Equal("DefaultHttpHeaders[foo: bar, baz: qix]", NewDefaultDefaultHttpHeaders()
                .Add("foo", "bar")
                .Add("baz", "qix")
                .ToString());
    }

    [Fact]
    public void ProvidesHeaderNamesAsArray()
    {
        ISet<string> nettyHeaders = new DefaultHttpHeaders()
                .Add(HttpHeaderNames.CONTENT_LENGTH, 10)
                .Names();

        string[] namesArray = nettyHeaders.ToArray();
        Assert.Equal(namesArray, new string[] { HttpHeaderNames.CONTENT_LENGTH.ToString() });
    }

    [Fact]
    public void Names()
    {
        HttpHeaders headers = new DefaultHttpHeaders()
                .Add(ACCEPT, APPLICATION_JSON)
                .Add(CONTENT_LENGTH, ZERO)
                .Add(CONNECTION, CLOSE);
        Assert.False(headers.IsEmpty());
        Assert.Equal(3, headers.Size());
        ISet<string> names = headers.Names();
        Assert.Equal(3, names.Count);
        Assert.True(names.Contains(ACCEPT.ToString()));
        Assert.True(names.Contains(CONTENT_LENGTH.ToString()));
        Assert.True(names.Contains(CONNECTION.ToString()));
    }

    [Fact]
    public void TestContainsName()
    {
        HttpHeaders headers = new DefaultHttpHeaders()
                .Add(CONTENT_LENGTH, "36");
        Assert.True(headers.Contains("Content-Length"));
        Assert.True(headers.Contains("content-length"));
        Assert.True(headers.Contains(CONTENT_LENGTH));
        headers.Remove(CONTENT_LENGTH);
        Assert.False(headers.Contains("Content-Length"));
        Assert.False(headers.Contains("content-length"));
        Assert.False(headers.Contains(CONTENT_LENGTH));

        Assert.False(headers.Contains("non-existent-name"));
        Assert.False(headers.Contains(new AsciiString("non-existent-name")));
    }

    private static void AssertDefaultValues(DefaultHttpHeaders headers, HeaderValue headerValue)
    {
        Assert.True(ContentEquals(headerValue.AsList()[0], new StringCharSequence(headers.Get(HEADER_NAME))));
        IList<ICharSequence> expected = headerValue.AsList();
        IList<string> actual = headers.GetAll(HEADER_NAME);
        Assert.Equal(expected.Count, actual.Count);
        using IEnumerator<ICharSequence> eItr = expected.GetEnumerator();
        using IEnumerator<string> aItr = actual.GetEnumerator();

        while (eItr.MoveNext())
        {
            aItr.MoveNext();
            Assert.True(ContentEquals(eItr.Current, new StringCharSequence(aItr.Current)));
        }
    }

    private static DefaultHttpHeaders NewDefaultDefaultHttpHeaders()
    {
        return new DefaultHttpHeaders();
    }
}
