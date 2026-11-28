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
using Netty.NET.Common.Internal;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: A Java String passed as a CharSequence is a StringCharSequence (Cs), because a C# string is not an
// ICharSequence.
public class QpackStaticTableTest
{
    [Fact]
    public void TestFieldNotFound()
    {
        Assert.Equal(QpackStaticTable.NOT_FOUND, QpackStaticTable.FindFieldIndex(Cs("x-netty-quic"), Cs("incubating")));
    }

    [Fact]
    public void TestFieldNameAndValueMatch()
    {
        // first in range
        Assert.Equal(15, QpackStaticTable.FindFieldIndex(Cs(":method"), Cs("CONNECT")));
        // last in range
        Assert.Equal(21, QpackStaticTable.FindFieldIndex(Cs(":method"), Cs("PUT")));
        // non-consequent range
        Assert.Equal(24, QpackStaticTable.FindFieldIndex(Cs(":status"), Cs("103")));
        Assert.Equal(69, QpackStaticTable.FindFieldIndex(Cs(":status"), Cs("421")));
    }

    [Fact]
    public void TestFieldNameRefForEmptyField()
    {
        int nameIndex1 = QpackStaticTable.FindFieldIndex(Cs("cookie"), Cs("netty.io"));
        int nameIndex2 = QpackStaticTable.FindFieldIndex(Cs("cookie"), Cs("quic.io"));

        // should give the same name ref for any values
        Assert.NotEqual(QpackStaticTable.NOT_FOUND, nameIndex1);
        Assert.NotEqual(QpackStaticTable.NOT_FOUND, nameIndex2);
        Assert.Equal(nameIndex1, nameIndex2);

        // index should be masked
        Assert.Equal(nameIndex1 & QpackStaticTable.MASK_NAME_REF, QpackStaticTable.MASK_NAME_REF);
        Assert.Equal(5, nameIndex1 ^ QpackStaticTable.MASK_NAME_REF);
    }

    [Fact]
    public void TestFieldNameRefForSingleMatch()
    {
        // note the value differs from static table ("1" rather than "0")
        int nameIndex = QpackStaticTable.FindFieldIndex(Cs("age"), Cs("1"));
        Assert.Equal(2, nameIndex ^ QpackStaticTable.MASK_NAME_REF);
    }

    [Fact]
    public void TestFieldNameRefForMultipleMatches()
    {
        int nameIndex = QpackStaticTable.FindFieldIndex(Cs(":method"), Cs("ALLTHETHINGS"));
        Assert.Equal(15, nameIndex ^ QpackStaticTable.MASK_NAME_REF);
    }

    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }
}
