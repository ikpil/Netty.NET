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
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: The @Deprecated CombinedHttpHeaders(boolean validate) names
// DefaultHttpHeadersFactory.withCombiningHeaders(boolean) and is not ported, so newCombinedHttpHeaders() is
// DefaultHttpHeadersFactory.HeadersFactory().WithCombiningHeaders(true).NewHeaders() (rule 7). A List<string>
// binds to the Iterable<?> overload through IEnumerable<object> covariance, and a String value binds to the Object
// overload as in Java. assertEquals(List<CharSequence>, List<String>) compares Strings in Java; the CLR
// counterpart of that dereference is NullReferenceException. Iterator<? extends CharSequence> of
// assertValueIterator is the generic IIterator<T>, whose elements are compared as strings (ToString()).
public class CombinedHttpHeadersTest
{
    private static readonly ICharSequence HEADER_NAME = new StringCharSequence("testHeader");

    [Fact]
    public void AddCharSequencesCsv()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertCsvValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void AddCharSequencesCsvWithExistingHeader()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.THREE.AsList());
        headers.Add(HEADER_NAME, HeaderValue.FIVE.Subset(4));
        AssertCsvValues(headers, HeaderValue.FIVE);
    }

    [Fact]
    public void AddCombinedHeadersWhenEmpty()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        CombinedHttpHeaders otherHeaders = NewCombinedHttpHeaders();
        otherHeaders.Add(HEADER_NAME, "a");
        otherHeaders.Add(HEADER_NAME, "b");
        headers.Add(otherHeaders);
        Assert.Equal("a,b", headers.Get(HEADER_NAME));
    }

    [Fact]
    public void AddCombinedHeadersWhenNotEmpty()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, "a");
        CombinedHttpHeaders otherHeaders = NewCombinedHttpHeaders();
        otherHeaders.Add(HEADER_NAME, "b");
        otherHeaders.Add(HEADER_NAME, "c");
        headers.Add(otherHeaders);
        Assert.Equal("a,b,c", headers.Get(HEADER_NAME));
    }

    [Fact]
    public void DontCombineSetCookieHeaders()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(SET_COOKIE, "a");
        CombinedHttpHeaders otherHeaders = NewCombinedHttpHeaders();
        otherHeaders.Add(SET_COOKIE, "b");
        otherHeaders.Add(SET_COOKIE, "c");
        headers.Add(otherHeaders);
        Assert.Equal(3, headers.GetAll(SET_COOKIE).Count);
    }

    [Fact]
    public void DontCombineSetCookieHeadersRegardlessOfCase()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add("Set-Cookie", "a");
        CombinedHttpHeaders otherHeaders = NewCombinedHttpHeaders();
        otherHeaders.Add("set-cookie", "b");
        otherHeaders.Add("SET-COOKIE", "c");
        headers.Add(otherHeaders);
        Assert.Equal(3, headers.GetAll(SET_COOKIE).Count);
    }

    [Fact]
    public void SetCombinedHeadersWhenNotEmpty()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, "a");
        CombinedHttpHeaders otherHeaders = NewCombinedHttpHeaders();
        otherHeaders.Add(HEADER_NAME, "b");
        otherHeaders.Add(HEADER_NAME, "c");
        headers.Set(otherHeaders);
        Assert.Equal("b,c", headers.Get(HEADER_NAME));
    }

    [Fact]
    public void AddUncombinedHeaders()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, "a");
        DefaultHttpHeaders otherHeaders = new DefaultHttpHeaders();
        otherHeaders.Add(HEADER_NAME, "b");
        otherHeaders.Add(HEADER_NAME, "c");
        headers.Add(otherHeaders);
        Assert.Equal("a,b,c", headers.Get(HEADER_NAME));
    }

    [Fact]
    public void SetUncombinedHeaders()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, "a");
        DefaultHttpHeaders otherHeaders = new DefaultHttpHeaders();
        otherHeaders.Add(HEADER_NAME, "b");
        otherHeaders.Add(HEADER_NAME, "c");
        headers.Set(otherHeaders);
        Assert.Equal("b,c", headers.Get(HEADER_NAME));
    }

    [Fact]
    public void AddCharSequencesCsvWithValueContainingComma()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.SIX_QUOTED.Subset(4));
        Assert.True(ContentEquals(new StringCharSequence(HeaderValue.SIX_QUOTED.SubsetAsCsvString(4)),
                new StringCharSequence(headers.Get(HEADER_NAME))));
        Assert.Equal(Strings(HeaderValue.SIX_QUOTED.Subset(4)), headers.GetAll(HEADER_NAME));
    }

    [Fact]
    public void AddCharSequencesCsvWithValueContainingCommas()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.EIGHT.Subset(6));
        Assert.True(ContentEquals(new StringCharSequence(HeaderValue.EIGHT.SubsetAsCsvString(6)),
                new StringCharSequence(headers.Get(HEADER_NAME))));
        Assert.Equal(Strings(HeaderValue.EIGHT.Subset(6)), headers.GetAll(HEADER_NAME));
    }

    [Fact]
    public void AddCharSequencesCsvNullValue()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        string value = null;
        Assert.Throws<NullReferenceException>(() => headers.Add(HEADER_NAME, value));
    }

    [Fact]
    public void AddCharSequencesCsvMultipleTimes()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();

        for (int i = 0; i < 5; ++i)
        {
            headers.Add(HEADER_NAME, "value");
        }

        Assert.True(ContentEquals(new StringCharSequence("value,value,value,value,value"),
                new StringCharSequence(headers.Get(HEADER_NAME))));
    }

    [Fact]
    public void AddCharSequenceCsv()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        AddValues(headers, HeaderValue.ONE, HeaderValue.TWO, HeaderValue.THREE);
        AssertCsvValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void AddCharSequenceCsvSingleValue()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        AddValues(headers, HeaderValue.ONE);
        AssertCsvValue(headers, HeaderValue.ONE);
    }

    [Fact]
    public void AddIterableCsv()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertCsvValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void AddIterableCsvWithExistingHeader()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.THREE.AsList());
        headers.Add(HEADER_NAME, HeaderValue.FIVE.Subset(4));
        AssertCsvValues(headers, HeaderValue.FIVE);
    }

    [Fact]
    public void AddIterableCsvSingleValue()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.ONE.AsList());
        AssertCsvValue(headers, HeaderValue.ONE);
    }

    [Fact]
    public void AddIterableCsvEmpty()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, new List<ICharSequence>());
        Assert.Equal(new[] { "" }, headers.GetAll(HEADER_NAME));
    }

    [Fact]
    public void AddObjectCsv()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        AddObjectValues(headers, HeaderValue.ONE, HeaderValue.TWO, HeaderValue.THREE);
        AssertCsvValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void AddObjectsCsv()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertCsvValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void AddObjectsIterableCsv()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertCsvValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void AddObjectsCsvWithExistingHeader()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(HEADER_NAME, HeaderValue.THREE.AsList());
        headers.Add(HEADER_NAME, HeaderValue.FIVE.Subset(4));
        AssertCsvValues(headers, HeaderValue.FIVE);
    }

    [Fact]
    public void SetCharSequenceCsv()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Set(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertCsvValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void SetIterableCsv()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Set(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertCsvValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void SetObjectObjectsCsv()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Set(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertCsvValues(headers, HeaderValue.THREE);
    }

    [Fact]
    public void SetObjectIterableCsv()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Set(HEADER_NAME, HeaderValue.THREE.AsList());
        AssertCsvValues(headers, HeaderValue.THREE);
    }

    private static CombinedHttpHeaders NewCombinedHttpHeaders()
    {
        return (CombinedHttpHeaders) DefaultHttpHeadersFactory.HeadersFactory().WithCombiningHeaders(true).NewHeaders();
    }

    // CLR: the String elements of a List<CharSequence>, for the assertEquals(List, List) comparisons.
    private static IList<string> Strings(IList<ICharSequence> values)
    {
        return values.Select(v => v.ToString()).ToList();
    }

    private static void AssertCsvValues(CombinedHttpHeaders headers, HeaderValue headerValue)
    {
        Assert.True(ContentEquals(headerValue.AsCsv(), new StringCharSequence(headers.Get(HEADER_NAME))));
        Assert.Equal(Strings(headerValue.AsList()), headers.GetAll(HEADER_NAME));
    }

    private static void AssertCsvValue(CombinedHttpHeaders headers, HeaderValue headerValue)
    {
        Assert.True(ContentEquals(new StringCharSequence(headerValue.ToString()),
                new StringCharSequence(headers.Get(HEADER_NAME))));
        Assert.True(ContentEquals(new StringCharSequence(headerValue.ToString()),
                new StringCharSequence(headers.GetAll(HEADER_NAME)[0])));
    }

    private static void AddValues(CombinedHttpHeaders headers, params HeaderValue[] headerValues)
    {
        foreach (HeaderValue v in headerValues)
        {
            headers.Add(HEADER_NAME, v.ToString());
        }
    }

    private static void AddObjectValues(CombinedHttpHeaders headers, params HeaderValue[] headerValues)
    {
        foreach (HeaderValue v in headerValues)
        {
            headers.Add(HEADER_NAME, v.ToString());
        }
    }

    [Fact]
    public void TestGetAll()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Set(HEADER_NAME, new List<string> { "a", "b", "c" });
        Assert.Equal(new List<string> { "a", "b", "c" }, headers.GetAll(HEADER_NAME));
        headers.Set(HEADER_NAME, new List<string> { "a,", "b,", "c," });
        Assert.Equal(new List<string> { "a,", "b,", "c," }, headers.GetAll(HEADER_NAME));
        headers.Set(HEADER_NAME, new List<string> { "a\"", "b\"", "c\"" });
        Assert.Equal(new List<string> { "a\"", "b\"", "c\"" }, headers.GetAll(HEADER_NAME));
        headers.Set(HEADER_NAME, new List<string> { "\"a\"", "\"b\"", "\"c\"" });
        Assert.Equal(new List<string> { "a", "b", "c" }, headers.GetAll(HEADER_NAME));
        headers.Set(HEADER_NAME, "a,b,c");
        Assert.Equal(new List<string> { "a,b,c" }, headers.GetAll(HEADER_NAME));
        headers.Set(HEADER_NAME, "\"a,b,c\"");
        Assert.Equal(new List<string> { "a,b,c" }, headers.GetAll(HEADER_NAME));
    }

    [Fact]
    public void GetAllDontCombineSetCookie()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(SET_COOKIE, "a");
        headers.Add(SET_COOKIE, "b");
        Assert.Equal(2, headers.GetAll(SET_COOKIE).Count);
        Assert.Equal(new List<string> { "a", "b" }, headers.GetAll(SET_COOKIE));
    }

    [Fact]
    public void OwsTrimming()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Set(HEADER_NAME, new List<string> { "\ta", "   ", "  b ", "\t \t" });
        headers.Add(HEADER_NAME, " c, d \t");

        Assert.Equal(new List<string> { "a", "", "b", "", "c, d" }, headers.GetAll(HEADER_NAME));
        Assert.Equal("a,,b,,\"c, d\"", headers.Get(HEADER_NAME));

        Assert.True(headers.ContainsValue(HEADER_NAME, Cs("a"), true));
        Assert.True(headers.ContainsValue(HEADER_NAME, Cs(" a "), true));
        Assert.True(headers.ContainsValue(HEADER_NAME, Cs("a"), true));
        Assert.False(headers.ContainsValue(HEADER_NAME, Cs("a,b"), true));

        Assert.False(headers.ContainsValue(HEADER_NAME, Cs(" c, d "), true));
        Assert.False(headers.ContainsValue(HEADER_NAME, Cs("c, d"), true));
        Assert.True(headers.ContainsValue(HEADER_NAME, Cs(" c "), true));
        Assert.True(headers.ContainsValue(HEADER_NAME, Cs("d"), true));

        Assert.True(headers.ContainsValue(HEADER_NAME, Cs("\t"), true));
        Assert.True(headers.ContainsValue(HEADER_NAME, Cs(""), true));

        Assert.False(headers.ContainsValue(HEADER_NAME, Cs("e"), true));

        HttpHeaders copiedHeaders = NewCombinedHttpHeaders().Add(headers);
        Assert.Equal(new List<string> { "a", "", "b", "", "c, d" }, copiedHeaders.GetAll(HEADER_NAME));
    }

    // CLR: a String passed as a CharSequence argument.
    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }

    [Fact]
    public void ValueIterator()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Set(HEADER_NAME, new List<string> { "\ta", "   ", "  b ", "\t \t" });
        headers.Add(HEADER_NAME, " c, d \t");

        Assert.False(headers.ValueStringIterator(Cs("foo")).HasNext());
        AssertValueIterator(headers.ValueStringIterator(HEADER_NAME));
        Assert.False(headers.ValueCharSequenceIterator(Cs("foo")).HasNext());
        AssertValueIterator(headers.ValueCharSequenceIterator(HEADER_NAME));
    }

    [Fact]
    public void NonCombinableHeaderIterator()
    {
        CombinedHttpHeaders headers = NewCombinedHttpHeaders();
        headers.Add(SET_COOKIE, "c");
        headers.Add(SET_COOKIE, "b");
        headers.Add(SET_COOKIE, "a");

        IIterator<string> strItr = headers.ValueStringIterator(SET_COOKIE);
        Assert.True(strItr.HasNext());
        Assert.Equal("a", strItr.Next());
        Assert.True(strItr.HasNext());
        Assert.Equal("b", strItr.Next());
        Assert.True(strItr.HasNext());
        Assert.Equal("c", strItr.Next());
    }

    private static void AssertValueIterator<T>(IIterator<T> strItr)
    {
        Assert.True(strItr.HasNext());
        Assert.Equal("a", strItr.Next().ToString());
        Assert.True(strItr.HasNext());
        Assert.Equal("", strItr.Next().ToString());
        Assert.True(strItr.HasNext());
        Assert.Equal("b", strItr.Next().ToString());
        Assert.True(strItr.HasNext());
        Assert.Equal("", strItr.Next().ToString());
        Assert.True(strItr.HasNext());
        Assert.Equal("c, d", strItr.Next().ToString());
        Assert.False(strItr.HasNext());
    }
}
