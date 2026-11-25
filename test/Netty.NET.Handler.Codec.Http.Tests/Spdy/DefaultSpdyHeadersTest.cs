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

using Netty.NET.Common;
using Netty.NET.Handler.Codec.Spdy;

namespace Netty.NET.Handler.Codec.Http.Tests.Spdy;

// CLR: a C# string is not an ICharSequence, so the String keys that Java passes to the CharSequence headers are the
// Common StringCharSequence (the core codec-http mapping of a String passed as CharSequence), and the String value
// "GET"/"/" is a StringCharSequence too; SpdyHeaders.HttpNames.METHOD.toString() is the AsciiString's text.
public class DefaultSpdyHeadersTest
{

    [Fact]
    public void TestStringKeyRetrievedAsAsciiString()
    {
        ISpdyHeaders headers = new DefaultSpdyHeaders();

        // Test adding String key and retrieving it using a AsciiString key
        string method = "GET";
        headers.Add(new StringCharSequence(":method"), new StringCharSequence(method));

        string value = headers.GetAsString(new StringCharSequence(ISpdyHeaders.HttpNames.METHOD.ToString()));
        Assert.NotNull(value);
        Assert.Equal(method, value);

        string value2 = headers.GetAsString(ISpdyHeaders.HttpNames.METHOD);
        Assert.NotNull(value2);
        Assert.Equal(method, value2);
    }

    [Fact]
    public void TestAsciiStringKeyRetrievedAsString()
    {
        ISpdyHeaders headers = new DefaultSpdyHeaders();

        // Test adding AsciiString key and retrieving it using a String key
        string path = "/";
        headers.Add(ISpdyHeaders.HttpNames.PATH, new StringCharSequence(path));

        string value = headers.GetAsString(ISpdyHeaders.HttpNames.PATH);
        Assert.NotNull(value);
        Assert.Equal(path, value);

        string value2 = headers.GetAsString(new StringCharSequence(ISpdyHeaders.HttpNames.PATH.ToString()));
        Assert.NotNull(value2);
        Assert.Equal(path, value2);
    }
}
