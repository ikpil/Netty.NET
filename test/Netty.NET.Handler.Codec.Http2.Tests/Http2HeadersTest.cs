/*
 * Copyright 2024 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.IHttp2Headers.PseudoHeaderName;
using PseudoHeaderName = Netty.NET.Handler.Codec.Http2.IHttp2Headers.PseudoHeaderName;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: a Java String passed as a CharSequence (getPseudoHeader(String) binds to getPseudoHeader(CharSequence)) is a
// StringCharSequence, and the StringBuilder, a third CharSequence implementation, is the Common
// AppendableCharSequence (as codec-http's HttpUtilTest; docs/codec-http-porting.md, Shared CLR adaptations).
// isPseudoHeader(String) is the IsPseudoHeader(string) overload. assertSame of the interned ":authority" literal holds
// because AsciiString.Cached keeps the given string for ToString() and C# string literals are interned too.
public class Http2HeadersTest
{

    [Fact]
    public void TestGetPseudoHeader()
    {
        foreach (PseudoHeaderName pseudoHeaderName in PseudoHeaderName.Values())
        {
            Assert.Same(pseudoHeaderName, GetPseudoHeader(pseudoHeaderName.Value()));
            Assert.Same(pseudoHeaderName, GetPseudoHeader(new AsciiString(pseudoHeaderName.Value().Array())));
            Assert.Same(pseudoHeaderName, GetPseudoHeader(new StringCharSequence(pseudoHeaderName.Value().ToString())));
            Assert.Same(pseudoHeaderName,
                    GetPseudoHeader(new StringCharSequence(new string(pseudoHeaderName.Value().ToCharArray()))));
            Assert.Same(pseudoHeaderName, GetPseudoHeader(NewStringBuilder(pseudoHeaderName.Value())));
        }
    }

    [Fact]
    public void PseudoHeaderNamesAreLiterals()
    {
        // despite is an implementation details. is a relevant optimization
        Assert.Same(":authority", PseudoHeaderName.AUTHORITY.Value().ToString());
        Assert.Same(":method", PseudoHeaderName.METHOD.Value().ToString());
        Assert.Same(":path", PseudoHeaderName.PATH.Value().ToString());
        Assert.Same(":scheme", PseudoHeaderName.SCHEME.Value().ToString());
        Assert.Same(":status", PseudoHeaderName.STATUS.Value().ToString());
        Assert.Same(":protocol", PseudoHeaderName.PROTOCOL.Value().ToString());
    }

    [Fact]
    public void TestIsPseudoHeader()
    {
        // same as before but for isPseudoHeader
        foreach (PseudoHeaderName pseudoHeaderName in PseudoHeaderName.Values())
        {
            Assert.True(IsPseudoHeader(pseudoHeaderName.Value()));
            Assert.True(IsPseudoHeader(new AsciiString(pseudoHeaderName.Value().Array())));
            Assert.True(IsPseudoHeader(pseudoHeaderName.Value().ToString()));
            Assert.True(IsPseudoHeader(new string(pseudoHeaderName.Value().ToCharArray())));
            Assert.True(IsPseudoHeader(NewStringBuilder(pseudoHeaderName.Value())));
        }
    }

    // CLR: new StringBuilder(CharSequence) as a CharSequence (see the class comment).
    private static ICharSequence NewStringBuilder(AsciiString value)
    {
        return new AppendableCharSequence(value.Length()).Append(value);
    }
}
