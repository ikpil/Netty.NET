/*
 * Copyright 2021 The Netty Project
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

// CLR: The fixture field fooBar is private readonly. A Java String passed as a CharSequence is a
// StringCharSequence (Cs), because a C# string is not an ICharSequence. assertEquals(int, long) widens the int as
// Java does (a long literal), assertEquals of two QpackHeaderField values compares references in both runtimes
// (QpackHeaderField does not override equals), and assertThrows(QpackException.class,...) is
// Assert.ThrowsAny<QpackException>.
public class QpackDecoderDynamicTableTest
{

    private readonly QpackHeaderField fooBar = new QpackHeaderField(Cs("foo"), Cs("bar"));

    [Fact]
    public void Length()
    {
        QpackDecoderDynamicTable table = NewTable(100);
        Assert.Equal(0, table.Length());
        table.Add(fooBar);
        Assert.Equal(1, table.Length());
        table.Clear();
        Assert.Equal(0, table.Length());
    }

    [Fact]
    public void Size()
    {
        QpackDecoderDynamicTable table = NewTable(100);
        Assert.Equal(0L, table.Size());
        QpackHeaderField entry = new QpackHeaderField(Cs("foo"), Cs("bar"));
        table.Add(entry);
        Assert.Equal(entry.Size(), table.Size());
        table.Clear();
        Assert.Equal(0L, table.Size());
    }

    [Fact]
    public void GetEntry()
    {
        QpackDecoderDynamicTable table = NewTable(100);
        QpackHeaderField entry = new QpackHeaderField(Cs("foo"), Cs("bar"));
        table.Add(entry);
        Assert.Equal(entry, table.GetEntry(0));
        table.Clear();

        Assert.ThrowsAny<QpackException>(() => table.GetEntry(0));
    }

    [Fact]
    public void GetEntryExceptionally()
    {
        QpackDecoderDynamicTable table = NewTable(1);

        Assert.ThrowsAny<QpackException>(() => table.GetEntry(0));
    }

    [Fact]
    public void SetCapacity()
    {
        QpackHeaderField entry1 = new QpackHeaderField(Cs("foo"), Cs("bar"));
        QpackHeaderField entry2 = new QpackHeaderField(Cs("hello"), Cs("world"));
        long size1 = entry1.Size();
        long size2 = entry2.Size();
        QpackDecoderDynamicTable table = NewTable(size1 + size2);
        table.Add(entry1);
        table.Add(entry2);
        Assert.Equal(2, table.Length());
        Assert.Equal(size1 + size2, table.Size());
        Assert.Equal(entry1, table.GetEntry(0));
        Assert.Equal(entry2, table.GetEntry(1));

        table.SetCapacity((size1 + size2) * 2); //larger capacity
        Assert.Equal(2, table.Length());
        Assert.Equal(size1 + size2, table.Size());

        table.SetCapacity(size2); //smaller capacity
        //entry1 will be removed
        Assert.Equal(1, table.Length());
        Assert.Equal(size2, table.Size());
        Assert.Equal(entry2, table.GetEntry(0));
        table.SetCapacity(0); //clear all
        Assert.Equal(0, table.Length());
        Assert.Equal(0L, table.Size());
    }

    [Fact]
    public void Add()
    {
        QpackDecoderDynamicTable table = NewTable(100);
        Assert.Equal(0L, table.Size());
        QpackHeaderField entry1 = new QpackHeaderField(Cs("foo"), Cs("bar")); //size:3+3+32=38
        QpackHeaderField entry2 = new QpackHeaderField(Cs("hello"), Cs("world"));
        table.Add(entry1); //success
        Assert.Equal(entry1.Size(), table.Size());
        Assert.Equal(entry1, table.GetEntry(0));
        table.SetCapacity(32); //entry1 is removed from table
        Assert.Equal(0L, table.Size());
        Assert.Equal(0, table.Length());

        table.SetCapacity(64);
        table.Add(entry1); //success
        Assert.Equal(entry1.Size(), table.Size());
        Assert.Equal(1, table.Length());
        Assert.Equal(entry1, table.GetEntry(0));
        table.Add(entry2); //entry2 is added, but entry1 is removed from table
        Assert.Equal(entry2.Size(), table.Size());
        Assert.Equal(1, table.Length());
        Assert.Equal(entry2, table.GetEntry(1));

        table.SetCapacity(128);
        table.Add(entry1); //success
        Assert.Equal(entry2, table.GetEntry(0));
    }

    private static QpackDecoderDynamicTable NewTable(long capacity)
    {
        QpackDecoderDynamicTable table = new QpackDecoderDynamicTable();
        table.SetCapacity(capacity);
        return table;
    }

    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }
}
