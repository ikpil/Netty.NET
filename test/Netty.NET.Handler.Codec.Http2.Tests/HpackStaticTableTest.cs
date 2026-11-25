/*
 * Copyright 2022 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the String arguments, which Java passes as CharSequence, are StringCharSequence values (a C# string is not
// an ICharSequence; docs/codec-http-porting.md, Shared CLR adaptations) built by the private Of(string); the
// package-private HpackStaticTable is reached through InternalsVisibleTo.
public class HpackStaticTableTest
{

    [Fact]
    public void TestEmptyHeaderName()
    {
        Assert.Equal(-1, HpackStaticTable.GetIndex(Of("")));
    }

    [Fact]
    public void TestMissingHeaderName()
    {
        Assert.Equal(-1, HpackStaticTable.GetIndex(Of("missing")));
    }

    [Fact]
    public void TestExistingHeaderName()
    {
        Assert.Equal(6, HpackStaticTable.GetIndex(Of(":scheme")));
    }

    [Fact]
    public void TestMissingHeaderNameAndValue()
    {
        Assert.Equal(-1, HpackStaticTable.GetIndexInsensitive(Of("missing"), Of("value")));
    }

    [Fact]
    public void TestMissingHeaderNameButValueExists()
    {
        Assert.Equal(-1, HpackStaticTable.GetIndexInsensitive(Of("missing"), Of("https")));
    }

    [Fact]
    public void TestExistingHeaderNameAndValueFirstMatch()
    {
        Assert.Equal(6, HpackStaticTable.GetIndexInsensitive(Of(":scheme"), Of("http")));
    }

    [Fact]
    public void TestExistingHeaderNameAndValueSecondMatch()
    {
        Assert.Equal(7, HpackStaticTable.GetIndexInsensitive(
          AsciiString.Cached(":scheme"), AsciiString.Cached("https")));
    }

    [Fact]
    public void TestExistingHeaderNameAndEmptyValueMismatch()
    {
        Assert.Equal(-1, HpackStaticTable.GetIndexInsensitive(Of(":scheme"), Of("")));
    }

    [Fact]
    public void TestExistingHeaderNameAndEmptyValueMatch()
    {
        Assert.Equal(27, HpackStaticTable.GetIndexInsensitive(Of("content-language"), Of("")));
    }

    [Fact]
    public void TestExistingHeaderNameButMissingValue()
    {
        Assert.Equal(-1, HpackStaticTable.GetIndexInsensitive(Of(":scheme"), Of("missing")));
    }

    private static ICharSequence Of(string s)
    {
        return new StringCharSequence(s);
    }
}
