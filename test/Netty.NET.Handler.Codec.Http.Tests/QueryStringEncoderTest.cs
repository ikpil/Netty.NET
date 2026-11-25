/*
 * Copyright 2014 The Netty Project
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
using System.Text;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: new URI(String) is new Uri(string, UriKind.RelativeOrAbsolute), the counterpart of QueryStringEncoder.toUri().
// StandardCharsets.UTF_16 is Encoding.BigEndianUnicode (UTF-16 in big-endian byte order). The JDK "UTF-16" charset
// writes a big-endian byte-order mark when encoding
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html), so Java expects
// "%FE%FF%00%A5"; .NET Encoding.GetBytes never writes the preamble, which "can be prefixed to the sequence of bytes
// resulting from the encoding process"
// (https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.getpreamble?view=net-10.0), so the C# expectation
// of testNonDefaultEncoding is "%00%A5": the same big-endian UTF-16 code unit without the mark.
public class QueryStringEncoderTest
{

    [Fact]
    public void TestDefaultEncoding()
    {
        QueryStringEncoder e;

        e = new QueryStringEncoder("/foo");
        e.AddParam("a", "b=c");
        Assert.Equal("/foo?a=b%3Dc", e.ToString());
        Assert.Equal(new Uri("/foo?a=b%3Dc", UriKind.RelativeOrAbsolute), e.ToUri());

        e = new QueryStringEncoder("/foo/¥");
        e.AddParam("a", "¥");
        Assert.Equal("/foo/¥?a=%C2%A5", e.ToString());
        Assert.Equal(new Uri("/foo/¥?a=%C2%A5", UriKind.RelativeOrAbsolute), e.ToUri());

        e = new QueryStringEncoder("/foo/¥");
        e.AddParam("a", "abc¥");
        Assert.Equal("/foo/¥?a=abc%C2%A5", e.ToString());
        Assert.Equal(new Uri("/foo/¥?a=abc%C2%A5", UriKind.RelativeOrAbsolute), e.ToUri());

        e = new QueryStringEncoder("/foo");
        e.AddParam("a", "1");
        e.AddParam("b", "2");
        Assert.Equal("/foo?a=1&b=2", e.ToString());
        Assert.Equal(new Uri("/foo?a=1&b=2", UriKind.RelativeOrAbsolute), e.ToUri());

        e = new QueryStringEncoder("/foo");
        e.AddParam("a", "1");
        e.AddParam("b", "");
        e.AddParam("c", null);
        e.AddParam("d", null);
        Assert.Equal("/foo?a=1&b=&c&d", e.ToString());
        Assert.Equal(new Uri("/foo?a=1&b=&c&d", UriKind.RelativeOrAbsolute), e.ToUri());

        e = new QueryStringEncoder("/foo");
        e.AddParam("test", "a~b");
        Assert.Equal("/foo?test=a~b", e.ToString());
        Assert.Equal(new Uri("/foo?test=a~b", UriKind.RelativeOrAbsolute), e.ToUri());
    }

    [Fact]
    public void TestNonDefaultEncoding()
    {
        QueryStringEncoder e = new QueryStringEncoder("/foo/¥", Encoding.BigEndianUnicode);
        e.AddParam("a", "¥");
        Assert.Equal("/foo/¥?a=%00%A5", e.ToString());
        Assert.Equal(new Uri("/foo/¥?a=%00%A5", UriKind.RelativeOrAbsolute), e.ToUri());
    }

    [Fact]
    public void TestWhitespaceEncoding()
    {
        QueryStringEncoder e = new QueryStringEncoder("/foo");
        e.AddParam("a", "b c");
        Assert.Equal("/foo?a=b%20c", e.ToString());
        Assert.Equal(new Uri("/foo?a=b%20c", UriKind.RelativeOrAbsolute), e.ToUri());
    }
}
