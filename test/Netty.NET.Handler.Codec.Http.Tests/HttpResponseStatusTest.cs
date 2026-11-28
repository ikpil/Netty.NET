/*
 * Copyright 2018 The Netty Project
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
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: The two UNKNOWN loops call the xUnit assertion only when a code does not map to UNKNOWN (the assertion
// would fail with the same values), because an Assert.Equal call per code costs minutes on the CLR. The int loop
// "code > 0; code++" ends by wrapping past int.MaxValue, as in Java (C# int arithmetic is unchecked here).
public class HttpResponseStatusTest
{
    [Fact]
    public void ParseLineStringJustCode()
    {
        Assert.Same(HttpResponseStatus.OK, ParseLine("200"));
    }

    [Fact]
    public void ParseLineStringCodeAndPhrase()
    {
        Assert.Same(HttpResponseStatus.OK, ParseLine("200 OK"));
    }

    [Fact]
    public void ParseLineStringCustomCode()
    {
        HttpResponseStatus customStatus = ParseLine("612");
        Assert.Equal(612, customStatus.Code());
    }

    [Fact]
    public void ParseLineStringCustomCodeAndPhrase()
    {
        HttpResponseStatus customStatus = ParseLine("612 FOO");
        Assert.Equal(612, customStatus.Code());
        Assert.Equal("FOO", customStatus.ReasonPhrase());
    }

    [Fact]
    public void ParseLineStringMalformedCode()
    {
        Assert.Throws<ArgumentException>(() => ParseLine("200a"));
    }

    [Fact]
    public void ParseLineStringMalformedCodeWithPhrase()
    {
        Assert.Throws<ArgumentException>(() => ParseLine("200a foo"));
    }

    [Fact]
    public void ParseLineAsciiStringJustCode()
    {
        Assert.Same(HttpResponseStatus.OK, ParseLine(new AsciiString("200")));
    }

    [Fact]
    public void ParseLineAsciiStringCodeAndPhrase()
    {
        Assert.Same(HttpResponseStatus.OK, ParseLine(new AsciiString("200 OK")));
    }

    [Fact]
    public void ParseLineAsciiStringCustomCode()
    {
        HttpResponseStatus customStatus = ParseLine(new AsciiString("612"));
        Assert.Equal(612, customStatus.Code());
    }

    [Fact]
    public void ParseLineAsciiStringCustomCodeAndPhrase()
    {
        HttpResponseStatus customStatus = ParseLine(new AsciiString("612 FOO"));
        Assert.Equal(612, customStatus.Code());
        Assert.Equal("FOO", customStatus.ReasonPhrase());
    }

    [Fact]
    public void ParseLineAsciiStringMalformedCode()
    {
        Assert.Throws<ArgumentException>(() => ParseLine(new AsciiString("200a")));
    }

    [Fact]
    public void ParseLineAsciiStringMalformedCodeWithPhrase()
    {
        Assert.Throws<ArgumentException>(() => ParseLine(new AsciiString("200a foo")));
    }

    [Fact]
    public void TestHttpStatusClassValueOf()
    {
        // status scope: [100, 600).
        for (int code = 100; code < 600; code ++)
        {
            HttpStatusClass httpStatusClass = HttpStatusClass.ValueOf(code);
            Assert.NotSame(HttpStatusClass.UNKNOWN, httpStatusClass);

            if (HttpStatusClass.INFORMATIONAL.Contains(code))
            {
                Assert.Equal(HttpStatusClass.INFORMATIONAL, httpStatusClass);
            }
            else if (HttpStatusClass.SUCCESS.Contains(code))
            {
                Assert.Equal(HttpStatusClass.SUCCESS, httpStatusClass);
            }
            else if (HttpStatusClass.REDIRECTION.Contains(code))
            {
                Assert.Equal(HttpStatusClass.REDIRECTION, httpStatusClass);
            }
            else if (HttpStatusClass.CLIENT_ERROR.Contains(code))
            {
                Assert.Equal(HttpStatusClass.CLIENT_ERROR, httpStatusClass);
            }
            else if (HttpStatusClass.SERVER_ERROR.Contains(code))
            {
                Assert.Equal(HttpStatusClass.SERVER_ERROR, httpStatusClass);
            }
            else
            {
                Assert.Fail("At least one of the if-branches above must be true");
            }
        }

        // status scope: [Integer.MIN_VALUE, 100).
        for (int code = int.MinValue; code < 100; code ++)
        {
            HttpStatusClass httpStatusClass = HttpStatusClass.ValueOf(code);
            if (httpStatusClass != HttpStatusClass.UNKNOWN)
            {
                Assert.Equal(HttpStatusClass.UNKNOWN, httpStatusClass);
            }
        }

        // status scope: [600, Integer.MAX_VALUE].
        for (int code = 600; code > 0; code ++)
        {
            HttpStatusClass httpStatusClass = HttpStatusClass.ValueOf(code);
            if (httpStatusClass != HttpStatusClass.UNKNOWN)
            {
                Assert.Equal(HttpStatusClass.UNKNOWN, httpStatusClass);
            }
        }
    }
}
