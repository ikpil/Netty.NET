/*
 * Copyright 2012 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: java.net.URI is System.Uri (URI.create and new URI are new Uri(...)); CharsetUtil.UTF_8 and
// StandardCharsets.UTF_8 are Encoding.UTF8. Map<String, List<String>> is IDictionary<string, IList<string>>: size() is
// Count, get(k) is the indexer, containsKey is ContainsKey, and the entrySet() iterator is the dictionary enumerator
// (hasNext() is MoveNext(), getKey()/getValue() are Key/Value); keySet().iterator().next() is the first key. Map.equals
// compares the entries in any order, so assertQueryString compares the two maps key by key, as does Map.equals.
// assertEquals(n, collection.size()) keeps the source assertions, hence the xUnit2013 suppression.
#pragma warning disable xUnit2013 // assertEquals(n, size()) keeps the source assertions.
public class QueryStringDecoderTest
{

    [Fact]
    public void TestBasicUris()
    {
        QueryStringDecoder d = new QueryStringDecoder(new Uri("http://localhost/path"));
        Assert.Equal(0, d.Parameters().Count);
    }

    [Fact]
    public void TestBasic()
    {
        QueryStringDecoder d;

        d = new QueryStringDecoder("/foo");
        Assert.Equal("/foo", d.Path());
        Assert.Equal(0, d.Parameters().Count);

        d = new QueryStringDecoder("/foo%20bar");
        Assert.Equal("/foo bar", d.Path());
        Assert.Equal(0, d.Parameters().Count);

        d = new QueryStringDecoder("/foo?a=b=c");
        Assert.Equal("/foo", d.Path());
        Assert.Equal(1, d.Parameters().Count);
        Assert.Equal(1, d.Parameters()["a"].Count);
        Assert.Equal("b=c", d.Parameters()["a"][0]);

        d = new QueryStringDecoder("/foo?a=1&a=2");
        Assert.Equal("/foo", d.Path());
        Assert.Equal(1, d.Parameters().Count);
        Assert.Equal(2, d.Parameters()["a"].Count);
        Assert.Equal("1", d.Parameters()["a"][0]);
        Assert.Equal("2", d.Parameters()["a"][1]);

        d = new QueryStringDecoder("/foo%20bar?a=1&a=2");
        Assert.Equal("/foo bar", d.Path());
        Assert.Equal(1, d.Parameters().Count);
        Assert.Equal(2, d.Parameters()["a"].Count);
        Assert.Equal("1", d.Parameters()["a"][0]);
        Assert.Equal("2", d.Parameters()["a"][1]);

        d = new QueryStringDecoder("/foo?a=&a=2");
        Assert.Equal("/foo", d.Path());
        Assert.Equal(1, d.Parameters().Count);
        Assert.Equal(2, d.Parameters()["a"].Count);
        Assert.Equal("", d.Parameters()["a"][0]);
        Assert.Equal("2", d.Parameters()["a"][1]);

        d = new QueryStringDecoder("/foo?a=1&a=");
        Assert.Equal("/foo", d.Path());
        Assert.Equal(1, d.Parameters().Count);
        Assert.Equal(2, d.Parameters()["a"].Count);
        Assert.Equal("1", d.Parameters()["a"][0]);
        Assert.Equal("", d.Parameters()["a"][1]);

        d = new QueryStringDecoder("/foo?a=1&a=&a=");
        Assert.Equal("/foo", d.Path());
        Assert.Equal(1, d.Parameters().Count);
        Assert.Equal(3, d.Parameters()["a"].Count);
        Assert.Equal("1", d.Parameters()["a"][0]);
        Assert.Equal("", d.Parameters()["a"][1]);
        Assert.Equal("", d.Parameters()["a"][2]);

        d = new QueryStringDecoder("/foo?a=1=&a==2");
        Assert.Equal("/foo", d.Path());
        Assert.Equal(1, d.Parameters().Count);
        Assert.Equal(2, d.Parameters()["a"].Count);
        Assert.Equal("1=", d.Parameters()["a"][0]);
        Assert.Equal("=2", d.Parameters()["a"][1]);

        d = new QueryStringDecoder("/foo?abc=1%2023&abc=124%20");
        Assert.Equal("/foo", d.Path());
        Assert.Equal(1, d.Parameters().Count);
        Assert.Equal(2, d.Parameters()["abc"].Count);
        Assert.Equal("1 23", d.Parameters()["abc"][0]);
        Assert.Equal("124 ", d.Parameters()["abc"][1]);

        d = new QueryStringDecoder("/foo?abc=%7E");
        Assert.Equal("~", d.Parameters()["abc"][0]);
    }

    [Fact]
    public void TestNonHtmlQuery()
    {
        QueryStringDecoder d = QueryStringDecoder.Builder_().Build("/foo?abc=foo+bar");
        Assert.Equal("foo bar", d.Parameters()["abc"][0]);

        d = QueryStringDecoder.Builder_().HtmlQueryDecoding(false).Build("/foo?abc=foo+bar");
        Assert.Equal("foo+bar", d.Parameters()["abc"][0]);
    }

    [Fact]
    public void TestExotic()
    {
        AssertQueryString("", "");
        AssertQueryString("foo", "foo");
        AssertQueryString("foo", "foo?");
        AssertQueryString("/foo", "/foo?");
        AssertQueryString("/foo", "/foo");
        AssertQueryString("?a=", "?a");
        AssertQueryString("foo?a=", "foo?a");
        AssertQueryString("/foo?a=", "/foo?a");
        AssertQueryString("/foo?a=", "/foo?a&");
        AssertQueryString("/foo?a=", "/foo?&a");
        AssertQueryString("/foo?a=", "/foo?&a&");
        AssertQueryString("/foo?a=", "/foo?&=a");
        AssertQueryString("/foo?a=", "/foo?=a&");
        AssertQueryString("/foo?a=", "/foo?a=&");
        AssertQueryString("/foo?a=b&c=d", "/foo?a=b&&c=d");
        AssertQueryString("/foo?a=b&c=d", "/foo?a=b&=&c=d");
        AssertQueryString("/foo?a=b&c=d", "/foo?a=b&==&c=d");
        AssertQueryString("/foo?a=b&c=&x=y", "/foo?a=b&c&x=y");
        AssertQueryString("/foo?a=", "/foo?a=");
        AssertQueryString("/foo?a=", "/foo?&a=");
        AssertQueryString("/foo?a=b&c=d", "/foo?a=b&c=d");
        AssertQueryString("/foo?a=1&a=&a=", "/foo?a=1&a&a=");
    }

    [Fact]
    public void TestSemicolon()
    {
        AssertQueryString("/foo?a=1;2", "/foo?a=1;2", false);
        // ";" should be treated as a normal character, see #8855
        AssertQueryString("/foo?a=1;2", "/foo?a=1%3B2", true);
    }

    [Fact]
    public void TestPathSpecific()
    {
        // decode escaped characters
        Assert.Equal("/foo bar/", new QueryStringDecoder("/foo%20bar/?").Path());
        Assert.Equal("/foo\r\n\\bar/", new QueryStringDecoder("/foo%0D%0A\\bar/?").Path());

        // a 'fragment' after '#' should be cuted (see RFC 3986)
        Assert.Equal("", new QueryStringDecoder("#123").Path());
        Assert.Equal("foo", new QueryStringDecoder("foo?bar#anchor").Path());
        Assert.Equal("/foo-bar", new QueryStringDecoder("/foo-bar#anchor").Path());
        Assert.Equal("/foo-bar", new QueryStringDecoder("/foo-bar#a#b?c=d").Path());

        // '+' is not escape ' ' for the path
        Assert.Equal("+", new QueryStringDecoder("+").Path());
        Assert.Equal("/foo+bar/", new QueryStringDecoder("/foo+bar/?").Path());
        Assert.Equal("/foo++", new QueryStringDecoder("/foo++?index.php").Path());
        Assert.Equal("/foo +", new QueryStringDecoder("/foo%20+?index.php").Path());
        Assert.Equal("/foo+ ", new QueryStringDecoder("/foo+%20").Path());
    }

    [Fact]
    public void TestExcludeFragment()
    {
        // a 'fragment' after '#' should be cuted (see RFC 3986)
        Assert.Equal("a", FirstKey(new QueryStringDecoder("?a#anchor").Parameters()));
        Assert.Equal("b", new QueryStringDecoder("?a=b#anchor").Parameters()["a"][0]);
        Assert.True(new QueryStringDecoder("?#").Parameters().Count == 0);
        Assert.True(new QueryStringDecoder("?#anchor").Parameters().Count == 0);
        Assert.True(new QueryStringDecoder("#?a=b#anchor").Parameters().Count == 0);
        Assert.True(new QueryStringDecoder("?#a=b#anchor").Parameters().Count == 0);
    }

    private static string FirstKey(IDictionary<string, IList<string>> map)
    {
        using IEnumerator<string> keys = map.Keys.GetEnumerator();
        keys.MoveNext();
        return keys.Current;
    }

    [Fact]
    public void TestHashDos()
    {
        StringBuilder buf = new StringBuilder();
        buf.Append('?');

        for (int i = 0; i < 65536; i ++)
        {
            buf.Append('k');
            buf.Append(i);
            buf.Append("=v");
            buf.Append(i);
            buf.Append('&');
        }

        Assert.Equal(1024, new QueryStringDecoder(buf.ToString()).Parameters().Count);
    }

    [Fact]
    public void TestHasPath()
    {
        QueryStringDecoder decoder = new QueryStringDecoder("1=2", false);
        Assert.Equal("", decoder.Path());
        IDictionary<string, IList<string>> @params = decoder.Parameters();
        Assert.Equal(1, @params.Count);
        Assert.True(@params.ContainsKey("1"));
        IList<string> param = @params["1"];
        Assert.NotNull(param);
        Assert.Equal(1, param.Count);
        Assert.Equal("2", param[0]);
    }

    [Fact]
    public void TestUrlDecoding()
    {
        string caffe = Encoding.UTF8.GetString(
                // "Caffé" but instead of putting the literal E-acute in the
                // source file, we directly use the UTF-8 encoding so as to
                // not rely on the platform's default encoding (not portable).
                new byte[] {(byte) 'C', (byte) 'a', (byte) 'f', (byte) 'f', (byte) 0xC3, (byte) 0xA9});
        string[] tests = {
            // Encoded   ->   Decoded or error message substring
            "",               "",
            "foo",            "foo",
            "f+o",            "f o",
            "f++",            "f  ",
            "fo%",            "unterminated escape sequence at index 2 of: fo%",
            "%42",            "B",
            "%5f",            "_",
            "f%4",            "unterminated escape sequence at index 1 of: f%4",
            "%x2",            "invalid hex byte 'x2' at index 1 of '%x2'",
            "%4x",            "invalid hex byte '4x' at index 1 of '%4x'",
            "Caff%C3%A9",     caffe,
            "случайный праздник",               "случайный праздник",
            "случайный%20праздник",             "случайный праздник",
            "случайный%20праздник%20%E2%98%BA", "случайный праздник ☺",
        };

        for (int i = 0; i < tests.Length; i += 2)
        {
            string encoded = tests[i];
            string expected = tests[i + 1];

            try
            {
                string decoded = QueryStringDecoder.DecodeComponent(encoded);
                Assert.Equal(expected, decoded);
            }
            catch (ArgumentException e)
            {
                Assert.Equal(expected, e.Message);
            }
        }
    }

    private static void AssertQueryString(string expected, string actual)
    {
        AssertQueryString(expected, actual, false);
    }

    private static void AssertQueryString(string expected, string actual, bool semicolonIsNormalChar)
    {
        QueryStringDecoder ed = new QueryStringDecoder(expected, Encoding.UTF8, true,
                1024, semicolonIsNormalChar);
        QueryStringDecoder ad = new QueryStringDecoder(actual, Encoding.UTF8, true,
                1024, semicolonIsNormalChar);
        Assert.Equal(ed.Path(), ad.Path());
        AssertMapEquals(ed.Parameters(), ad.Parameters());
    }

    private static void AssertMapEquals(IDictionary<string, IList<string>> expected,
                                        IDictionary<string, IList<string>> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        foreach (KeyValuePair<string, IList<string>> e in expected)
        {
            Assert.True(actual.ContainsKey(e.Key));
            Assert.Equal(e.Value, actual[e.Key]);
        }
    }

    // See #189
    [Fact]
    public void TestURI()
    {
        Uri uri = new Uri("http://localhost:8080/foo?param1=value1&param2=value2&param3=value3");
        QueryStringDecoder decoder = new QueryStringDecoder(uri);
        Assert.Equal("/foo", decoder.Path());
        Assert.Equal("/foo", decoder.RawPath());
        Assert.Equal("param1=value1&param2=value2&param3=value3", decoder.RawQuery());
        IDictionary<string, IList<string>> @params =  decoder.Parameters();
        Assert.Equal(3, @params.Count);
        using IEnumerator<KeyValuePair<string, IList<string>>> entries = @params.GetEnumerator();

        Assert.True(entries.MoveNext());
        KeyValuePair<string, IList<string>> entry = entries.Current;
        Assert.Equal("param1", entry.Key);
        Assert.Equal(1, entry.Value.Count);
        Assert.Equal("value1", entry.Value[0]);

        Assert.True(entries.MoveNext());
        entry = entries.Current;
        Assert.Equal("param2", entry.Key);
        Assert.Equal(1, entry.Value.Count);
        Assert.Equal("value2", entry.Value[0]);

        Assert.True(entries.MoveNext());
        entry = entries.Current;
        Assert.Equal("param3", entry.Key);
        Assert.Equal(1, entry.Value.Count);
        Assert.Equal("value3", entry.Value[0]);

        Assert.False(entries.MoveNext());
    }

    // See #189
    [Fact]
    public void TestURISlashPath()
    {
        Uri uri = new Uri("http://localhost:8080/?param1=value1&param2=value2&param3=value3");
        QueryStringDecoder decoder = new QueryStringDecoder(uri);
        Assert.Equal("/", decoder.Path());
        Assert.Equal("/", decoder.RawPath());
        Assert.Equal("param1=value1&param2=value2&param3=value3", decoder.RawQuery());

        IDictionary<string, IList<string>> @params =  decoder.Parameters();
        Assert.Equal(3, @params.Count);
        using IEnumerator<KeyValuePair<string, IList<string>>> entries = @params.GetEnumerator();

        Assert.True(entries.MoveNext());
        KeyValuePair<string, IList<string>> entry = entries.Current;
        Assert.Equal("param1", entry.Key);
        Assert.Equal(1, entry.Value.Count);
        Assert.Equal("value1", entry.Value[0]);

        Assert.True(entries.MoveNext());
        entry = entries.Current;
        Assert.Equal("param2", entry.Key);
        Assert.Equal(1, entry.Value.Count);
        Assert.Equal("value2", entry.Value[0]);

        Assert.True(entries.MoveNext());
        entry = entries.Current;
        Assert.Equal("param3", entry.Key);
        Assert.Equal(1, entry.Value.Count);
        Assert.Equal("value3", entry.Value[0]);

        Assert.False(entries.MoveNext());
    }

    // See #189
    [Fact]
    public void TestURINoPath()
    {
        Uri uri = new Uri("http://localhost:8080?param1=value1&param2=value2&param3=value3");
        QueryStringDecoder decoder = new QueryStringDecoder(uri);
        Assert.Equal("", decoder.Path());
        Assert.Equal("", decoder.RawPath());
        Assert.Equal("param1=value1&param2=value2&param3=value3", decoder.RawQuery());

        IDictionary<string, IList<string>> @params =  decoder.Parameters();
        Assert.Equal(3, @params.Count);
        using IEnumerator<KeyValuePair<string, IList<string>>> entries = @params.GetEnumerator();

        Assert.True(entries.MoveNext());
        KeyValuePair<string, IList<string>> entry = entries.Current;
        Assert.Equal("param1", entry.Key);
        Assert.Equal(1, entry.Value.Count);
        Assert.Equal("value1", entry.Value[0]);

        Assert.True(entries.MoveNext());
        entry = entries.Current;
        Assert.Equal("param2", entry.Key);
        Assert.Equal(1, entry.Value.Count);
        Assert.Equal("value2", entry.Value[0]);

        Assert.True(entries.MoveNext());
        entry = entries.Current;
        Assert.Equal("param3", entry.Key);
        Assert.Equal(1, entry.Value.Count);
        Assert.Equal("value3", entry.Value[0]);

        Assert.False(entries.MoveNext());
    }

    // See https://github.com/netty/netty/issues/1833
    [Fact]
    public void TestURI2()
    {
        Uri uri = new Uri("http://foo.com/images;num=10?query=name;value=123");
        QueryStringDecoder decoder = new QueryStringDecoder(uri);
        Assert.Equal("/images;num=10", decoder.Path());
        Assert.Equal("/images;num=10", decoder.RawPath());
        Assert.Equal("query=name;value=123", decoder.RawQuery());

        IDictionary<string, IList<string>> @params =  decoder.Parameters();
        Assert.Equal(2, @params.Count);
        using IEnumerator<KeyValuePair<string, IList<string>>> entries = @params.GetEnumerator();

        Assert.True(entries.MoveNext());
        KeyValuePair<string, IList<string>> entry = entries.Current;
        Assert.Equal("query", entry.Key);
        Assert.Equal(1, entry.Value.Count);
        Assert.Equal("name", entry.Value[0]);

        Assert.True(entries.MoveNext());
        entry = entries.Current;
        Assert.Equal("value", entry.Key);
        Assert.Equal(1, entry.Value.Count);
        Assert.Equal("123", entry.Value[0]);

        Assert.False(entries.MoveNext());
    }

    [Fact]
    public void TestEmptyStrings()
    {
        QueryStringDecoder pathSlash = new QueryStringDecoder("path/");
        Assert.Equal("path/", pathSlash.RawPath());
        Assert.Equal("", pathSlash.RawQuery());
        QueryStringDecoder pathQuestion = new QueryStringDecoder("path?");
        Assert.Equal("path", pathQuestion.RawPath());
        Assert.Equal("", pathQuestion.RawQuery());
        QueryStringDecoder empty = new QueryStringDecoder("");
        Assert.Equal("", empty.RawPath());
        Assert.Equal("", empty.RawQuery());
    }
}
