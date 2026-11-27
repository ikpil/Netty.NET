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
using static Netty.NET.Handler.Codec.Http3.QpackUtil;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the internal QpackEncoderDynamicTable, QpackHeaderField and QpackUtil (package-private in Java) are reached
// through InternalsVisibleTo; the static import of QpackUtil.MAX_HEADER_TABLE_SIZE is using static QpackUtil. A Java
// String passed as a CharSequence is a StringCharSequence (Cs), because a C# string is not an ICharSequence
// (docs/codec-http-porting.md, Shared CLR adaptations). The Hamcrest assertThat(reason, actual, matcher) calls keep their
// reason as the assertion message: lessThan(0) is Assert.True(actual < 0, reason), greaterThanOrEqualTo(0) is
// Assert.True(actual >= 0, reason), is(expected) is Assert.Equal(expected, actual, reason), and assertThat(actual,
// is(0)) without a reason is Assert.Equal(0, actual). xUnit creates a new instance of the test class for every test, as
// JUnit does, so insertCount and maxCapacity start at their defaults in each test. assertThrows(QpackException.class,
// ...) is Assert.ThrowsAny<QpackException> (docs/codec-http-porting.md, Core test notes). Math.toIntExact is a checked
// conversion (OverflowException for Java's ArithmeticException) and Math.floorDiv is QpackUtil.FloorDiv (Java's floor
// division). Long.MAX_VALUE is long.MaxValue; the throws clauses have no counterpart.
public class QpackEncoderDynamicTableTest
{
    private static readonly QpackHeaderField emptyHeader = new QpackHeaderField(Cs(""), Cs(""));
    private static readonly QpackHeaderField fooBarHeader = new QpackHeaderField(Cs("foo"), Cs("bar"));
    private static readonly QpackHeaderField fooBar2Header = new QpackHeaderField(Cs("foo"), Cs("bar2"));
    private static readonly QpackHeaderField fooBar3Header = new QpackHeaderField(Cs("foo"), Cs("bar3"));

    private int insertCount;
    private long maxCapacity;

    [Fact]
    public void ZeroCapacityIsAllowed()
    {
        QpackEncoderDynamicTable table = NewDynamicTable(0);

        Assert.True(AddHeader(table, emptyHeader) < 0, "Header addition passed.");
    }

    [Fact]
    public void MaxCapacityIsAllowed()
    {
        QpackEncoderDynamicTable table = NewDynamicTable(MAX_HEADER_TABLE_SIZE);
        AddAndValidateHeader(table, emptyHeader);
    }

    [Fact]
    public void NegativeCapacityIsDisallowed()
    {
        Assert.ThrowsAny<QpackException>(() => NewDynamicTable(-1));
    }

    [Fact]
    public void CapacityTooLarge()
    {
        Assert.ThrowsAny<QpackException>(() => NewDynamicTable(long.MaxValue));
    }

    [Fact]
    public void DelayAck()
    {
        QpackEncoderDynamicTable table = NewDynamicTable(16, 50, 128);

        AddAndValidateHeader(table, emptyHeader);
        AddAndValidateHeader(table, fooBarHeader);
        int idx2 = AddAndValidateHeader(table, fooBar2Header);

        Assert.True(AddHeader(table, fooBarHeader) < 0, "Header addition passed.");

        table.IncrementKnownReceivedCount(3);
        Assert.True(GetEntryIndex(table, emptyHeader) < 0, "Unexpected entry index.");
        Assert.True(GetEntryIndex(table, fooBarHeader) < 0, "Unexpected entry index.");
        Assert.Equal(idx2, GetEntryIndex(table, fooBar2Header), "Unexpected entry index.");

        int idx1 = AddAndValidateHeader(table, emptyHeader);
        Assert.Equal(idx1, GetEntryIndex(table, emptyHeader), "Unexpected entry index.");
        Assert.True(GetEntryIndex(table, fooBar2Header) < 0, "Unexpected entry index.");
    }

    [Fact]
    public void AddAndGet()
    {
        QpackEncoderDynamicTable table = NewDynamicTable(128);

        int idx1 = AddValidateAndAckHeader(table, emptyHeader);
        Assert.Equal(0, idx1);
        Assert.Equal(idx1, GetEntryIndex(table, emptyHeader), "Unexpected entry index.");

        int idx2 = AddValidateAndAckHeader(table, fooBarHeader);
        Assert.Equal(1, idx2);
        Assert.Equal(idx2, GetEntryIndex(table, fooBarHeader), "Unexpected entry index.");

        Assert.Equal(idx1, GetEntryIndex(table, emptyHeader), "Unexpected entry index.");
    }

    [Fact]
    public void NameOnlyMatch()
    {
        QpackEncoderDynamicTable table = NewDynamicTable(128);
        AddValidateAndAckHeader(table, fooBarHeader);
        int lastIdx = AddValidateAndAckHeader(table, fooBar2Header);

        int idx = table.GetEntryIndex(Cs("foo"), Cs("baz"));
        Assert.True(idx < 0, "Unexpected index.");
        Assert.Equal(-lastIdx - 1, idx, "Unexpected index.");
    }

    [Fact]
    public void AddDuplicateEntries()
    {
        QpackEncoderDynamicTable table = NewDynamicTable(128);

        int idx1 = AddValidateAndAckHeader(table, emptyHeader);
        Assert.Equal(idx1, GetEntryIndex(table, emptyHeader), "Unexpected entry index.");

        int idx2 = AddValidateAndAckHeader(table, fooBarHeader);
        Assert.Equal(idx2, GetEntryIndex(table, fooBarHeader), "Unexpected entry index.");

        int idx3 = AddValidateAndAckHeader(table, emptyHeader);
        // Return the most recent entry
        Assert.Equal(idx3, GetEntryIndex(table, emptyHeader), "Unexpected entry index.");
    }

    [Fact]
    public void HashCollisionThenRemove()
    {
        // expected max size: 0.9*128 = 115
        QpackEncoderDynamicTable table = NewDynamicTable(16, 10, 128);
        AddValidateAndAckHeader(table, fooBarHeader); // size = 38
        AddValidateAndAckHeader(table, fooBar2Header); // size = 77

        AddValidateAndAckHeader(table, fooBar3Header); // size = 116, exceeds max threshold, should evict eldest

        Assert.True(GetEntryIndex(table, fooBarHeader) < 0, "Entry found.");
        Assert.True(GetEntryIndex(table, fooBar2Header) >= 0, "Entry not found.");
        Assert.True(GetEntryIndex(table, fooBar3Header) >= 0, "Entry not found.");
    }

    [Fact]
    public void RequiredInsertCountWrapsAround()
    {
        // maxIndex = 2 * maxEntries = 2 * 64/32 = 4
        QpackEncoderDynamicTable table = NewDynamicTable(64);

        AddValidateAndAckHeader(table, emptyHeader);
        AddValidateAndAckHeader(table, emptyHeader);
        AddValidateAndAckHeader(table, emptyHeader);
        AddValidateAndAckHeader(table, emptyHeader);
        AddValidateAndAckHeader(table, emptyHeader);
    }

    [Fact]
    public void IndexWrapsAroundForSingleEntryCapacity()
    {
        // maxIndex = 2 * maxEntries = 2 * 39/32 = 2
        QpackEncoderDynamicTable table = NewDynamicTable(fooBar2Header.Size());
        AddValidateAndAckHeader(table, fooBar2Header);
        VerifyTableEmpty(table);
        AddValidateAndAckHeader(table, fooBar2Header);
    }

    [Fact]
    public void SectionAck()
    {
        QpackEncoderDynamicTable table = NewDynamicTable(128);

        int idx = AddAndValidateHeader(table, fooBarHeader);
        table.AddReferenceToEntry(fooBarHeader.name, fooBarHeader.value, idx);
        table.AcknowledgeInsertCountOnAck(idx);

        Assert.Equal(2, table.EncodedKnownReceivedCount(), "Unexpected known received count.");
    }

    [Fact]
    public void SectionAckOutOfOrder()
    {
        QpackEncoderDynamicTable table = NewDynamicTable(128);

        int idx1 = AddAndValidateHeader(table, fooBarHeader);
        table.AddReferenceToEntry(fooBarHeader.name, fooBarHeader.value, idx1);

        int idx2 = AddAndValidateHeader(table, fooBarHeader);
        table.AddReferenceToEntry(fooBarHeader.name, fooBarHeader.value, idx2);

        table.AcknowledgeInsertCountOnAck(idx2);
        Assert.Equal(3, table.EncodedKnownReceivedCount(), "Unexpected known received count.");

        table.AcknowledgeInsertCountOnAck(idx1);
        Assert.Equal(3, table.EncodedKnownReceivedCount(), "Unexpected known received count."); // already acked
    }

    [Fact]
    public void MultipleReferences()
    {
        // maxIndex = 2 * maxEntries = 2 * 39/32 = 2
        QpackEncoderDynamicTable table = NewDynamicTable(fooBar3Header.Size());

        int idx1 = AddAndValidateHeader(table, fooBar3Header);
        table.AddReferenceToEntry(fooBar3Header.name, fooBar3Header.value, idx1);
        table.AddReferenceToEntry(fooBar3Header.name, fooBar3Header.value, idx1);

        table.AcknowledgeInsertCountOnAck(idx1);

        // first entry still active
        Assert.True(AddHeader(table, fooBar2Header) < 0, "Header added");

        table.AcknowledgeInsertCountOnAck(idx1);
        VerifyTableEmpty(table);
        AddAndValidateHeader(table, fooBarHeader);
    }

    private void VerifyTableEmpty(QpackEncoderDynamicTable table)
    {
        Assert.Equal(0, table.InsertCount());
        insertCount = 0;
    }

    private int GetEntryIndex(QpackEncoderDynamicTable table, QpackHeaderField emptyHeader)
    {
        return table.GetEntryIndex(emptyHeader.name, emptyHeader.value);
    }

    private int AddHeader(QpackEncoderDynamicTable table, QpackHeaderField header)
    {
        int idx = table.Add(header.name, header.value, header.Size());
        if (idx >= 0)
        {
            insertCount++;
        }
        return idx;
    }

    private int AddAndValidateHeader(QpackEncoderDynamicTable table, QpackHeaderField header)
    {
        int addedIdx = AddHeader(table, header);
        Assert.True(addedIdx >= 0, "Header addition failed.");
        VerifyInsertCount(table);
        return addedIdx;
    }

    private int AddValidateAndAckHeader(QpackEncoderDynamicTable table, QpackHeaderField header)
    {
        int addedIdx = AddAndValidateHeader(table, header);
        table.AddReferenceToEntry(header.name, header.value, addedIdx);
        table.AcknowledgeInsertCountOnAck(addedIdx);
        return addedIdx;
    }

    private QpackEncoderDynamicTable NewDynamicTable(int arraySizeHint, int expectedFreeCapacityPercentage,
                                                     long maxCapacity)
    {
        return SetMaxTableCapacity(maxCapacity,
                new QpackEncoderDynamicTable(arraySizeHint, expectedFreeCapacityPercentage));
    }

    private QpackEncoderDynamicTable NewDynamicTable(long maxCapacity)
    {
        return SetMaxTableCapacity(maxCapacity, new QpackEncoderDynamicTable());
    }

    private QpackEncoderDynamicTable SetMaxTableCapacity(long maxCapacity, QpackEncoderDynamicTable table)
    {
        table.MaxTableCapacity(maxCapacity);
        this.maxCapacity = maxCapacity;
        return table;
    }

    private void VerifyInsertCount(QpackEncoderDynamicTable table)
    {
        Assert.Equal(ExpectedInsertCount(), table.EncodedRequiredInsertCount(table.InsertCount()),
                "Unexpected required insert count.");
    }

    private int ExpectedInsertCount()
    {
        return insertCount == 0 ? 0 : checked((int) ((insertCount % (2 * FloorDiv(maxCapacity, 32))) + 1));
    }

    // CLR: a Java String passed as a CharSequence (see the class comment).
    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }
}
