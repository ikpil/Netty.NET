/*
 * Copyright 2020 The Netty Project
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

using System;
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: assertThrows(IndexOutOfBoundsException.class, ...) is Assert.ThrowsAny<ArgumentOutOfRangeException> (the
// Java exception mapped as in docs/codec-base-porting.md; docs/codec-http-porting.md, Core test notes:
// "assertThrows(T.class) is Assert.ThrowsAny<T>"), and the anonymous Executables are lambdas. The package-private
// HpackDynamicTable and HpackHeaderField are reached through InternalsVisibleTo.
public class HpackDynamicTableTest
{
    private static readonly AsciiString FOO = AsciiString.Cached("foo");
    private static readonly AsciiString BAR = AsciiString.Cached("bar");
    private static readonly AsciiString HELLO = AsciiString.Cached("hello");
    private static readonly AsciiString WORLD = AsciiString.Cached("world");

    [Fact]
    public void TestLength()
    {
        HpackDynamicTable table = new HpackDynamicTable(100);
        Assert.Equal(0, table.Length());
        HpackHeaderField entry = new HpackHeaderField(FOO, BAR);
        table.Add(entry);
        Assert.Equal(1, table.Length());
        table.Clear();
        Assert.Equal(0, table.Length());
    }

    [Fact]
    public void TestSize()
    {
        HpackDynamicTable table = new HpackDynamicTable(100);
        Assert.Equal(0, table.Size());
        HpackHeaderField entry = new HpackHeaderField(FOO, BAR);
        table.Add(entry);
        Assert.Equal(entry.Size(), table.Size());
        table.Clear();
        Assert.Equal(0, table.Size());
    }

    [Fact]
    public void TestGetEntry()
    {
        HpackDynamicTable table = new HpackDynamicTable(100);
        HpackHeaderField entry = new HpackHeaderField(FOO, BAR);
        table.Add(entry);
        Assert.Equal(entry, table.GetEntry(1));
        table.Clear();

        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
        {
            table.GetEntry(1);
        });
    }

    [Fact]
    public void TestGetEntryExceptionally()
    {
        HpackDynamicTable table = new HpackDynamicTable(1);
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
        {
            table.GetEntry(1);
        });
    }

    [Fact]
    public void TestRemove()
    {
        HpackDynamicTable table = new HpackDynamicTable(100);
        Assert.Null(table.Remove());
        HpackHeaderField entry1 = new HpackHeaderField(FOO, BAR);
        HpackHeaderField entry2 = new HpackHeaderField(HELLO, WORLD);
        table.Add(entry1);
        table.Add(entry2);
        Assert.Equal(entry1, table.Remove());
        Assert.Equal(entry2, table.GetEntry(1));
        Assert.Equal(1, table.Length());
        Assert.Equal(entry2.Size(), table.Size());
    }

    [Fact]
    public void TestSetCapacity()
    {
        HpackHeaderField entry1 = new HpackHeaderField(FOO, BAR);
        HpackHeaderField entry2 = new HpackHeaderField(HELLO, WORLD);
        int size1 = entry1.Size();
        int size2 = entry2.Size();
        HpackDynamicTable table = new HpackDynamicTable(size1 + size2);
        table.Add(entry1);
        table.Add(entry2);
        Assert.Equal(2, table.Length());
        Assert.Equal(size1 + size2, table.Size());
        table.SetCapacity(((long) size1 + size2) * 2); //larger capacity
        Assert.Equal(2, table.Length());
        Assert.Equal(size1 + size2, table.Size());
        table.SetCapacity(size2); //smaller capacity
        //entry1 will be removed
        Assert.Equal(1, table.Length());
        Assert.Equal(size2, table.Size());
        Assert.Equal(entry2, table.GetEntry(1));
        table.SetCapacity(0); //clear all
        Assert.Equal(0, table.Length());
        Assert.Equal(0, table.Size());
    }

    [Fact]
    public void TestAdd()
    {
        HpackDynamicTable table = new HpackDynamicTable(100);
        Assert.Equal(0, table.Size());
        HpackHeaderField entry1 = new HpackHeaderField(FOO, BAR); //size:3+3+32=38
        HpackHeaderField entry2 = new HpackHeaderField(HELLO, WORLD);
        table.Add(entry1); //success
        Assert.Equal(entry1.Size(), table.Size());
        table.SetCapacity(32); //entry1 is removed from table
        Assert.Equal(0, table.Size());
        Assert.Equal(0, table.Length());
        table.Add(entry1); //fail quietly
        Assert.Equal(0, table.Size());
        Assert.Equal(0, table.Length());
        table.SetCapacity(64);
        table.Add(entry1); //success
        Assert.Equal(entry1.Size(), table.Size());
        Assert.Equal(1, table.Length());
        table.Add(entry2); //entry2 is added, but entry1 is removed from table
        Assert.Equal(entry2.Size(), table.Size());
        Assert.Equal(1, table.Length());
        Assert.Equal(entry2, table.GetEntry(1));
    }
}
