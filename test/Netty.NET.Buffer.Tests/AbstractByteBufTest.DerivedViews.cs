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

namespace Netty.NET.Buffer.Tests;

// CLR: the inherited derived-view contents, capacity and release-original bodies of the original
// AbstractByteBufTest, run against every concrete fixture's NewBuffer as in the source.
public abstract partial class AbstractByteBufTest
{
    [Fact]
    public void TestRetainedSliceContents()
    {
        TestSliceContents0(true);
    }

    [Fact]
    public void TestRetainedSliceReleaseOriginal1()
    {
        TestSliceReleaseOriginal(true, true);
    }

    [Fact]
    public void TestRetainedSliceReleaseOriginal2()
    {
        TestSliceReleaseOriginal(true, false);
    }

    [Fact]
    public void TestRetainedSliceReleaseOriginal3()
    {
        TestSliceReleaseOriginal(false, true);
    }

    [Fact]
    public void TestRetainedSliceReleaseOriginal4()
    {
        TestSliceReleaseOriginal(false, false);
    }

    [Fact]
    public void TestRetainedDuplicateReleaseOriginal1()
    {
        TestDuplicateReleaseOriginal(true, true);
    }

    [Fact]
    public void TestRetainedDuplicateReleaseOriginal2()
    {
        TestDuplicateReleaseOriginal(true, false);
    }

    [Fact]
    public void TestRetainedDuplicateReleaseOriginal3()
    {
        TestDuplicateReleaseOriginal(false, true);
    }

    [Fact]
    public void TestRetainedDuplicateReleaseOriginal4()
    {
        TestDuplicateReleaseOriginal(false, false);
    }

    [Fact]
    public void TestMultipleRetainedSliceReleaseOriginal1()
    {
        TestMultipleRetainedSliceReleaseOriginal(true, true);
    }

    [Fact]
    public void TestMultipleRetainedSliceReleaseOriginal2()
    {
        TestMultipleRetainedSliceReleaseOriginal(true, false);
    }

    [Fact]
    public void TestMultipleRetainedSliceReleaseOriginal3()
    {
        TestMultipleRetainedSliceReleaseOriginal(false, true);
    }

    [Fact]
    public void TestMultipleRetainedSliceReleaseOriginal4()
    {
        TestMultipleRetainedSliceReleaseOriginal(false, false);
    }

    [Fact]
    public void TestMultipleRetainedDuplicateReleaseOriginal1()
    {
        TestMultipleRetainedDuplicateReleaseOriginal(true, true);
    }

    [Fact]
    public void TestMultipleRetainedDuplicateReleaseOriginal2()
    {
        TestMultipleRetainedDuplicateReleaseOriginal(true, false);
    }

    [Fact]
    public void TestMultipleRetainedDuplicateReleaseOriginal3()
    {
        TestMultipleRetainedDuplicateReleaseOriginal(false, true);
    }

    [Fact]
    public void TestMultipleRetainedDuplicateReleaseOriginal4()
    {
        TestMultipleRetainedDuplicateReleaseOriginal(false, false);
    }

    [Fact]
    public void TestSliceContents()
    {
        TestSliceContents0(false);
    }

    [Fact]
    public void TestRetainedDuplicateContents()
    {
        TestDuplicateContents0(true);
    }

    [Fact]
    public void TestDuplicateContents()
    {
        TestDuplicateContents0(false);
    }

    [Fact]
    public virtual void TestDuplicateCapacityChange()
    {
        TestDuplicateCapacityChange0(false);
    }

    [Fact]
    public virtual void TestRetainedDuplicateCapacityChange()
    {
        TestDuplicateCapacityChange0(true);
    }

    // CLR: xUnit1024 forbids overloading the TestDuplicateCapacityChange test name.
    private void TestDuplicateCapacityChange0(bool retainedDuplicate)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf dup = retainedDuplicate ? buf.RetainedDuplicate() : buf.Duplicate();
        try
        {
            dup.Capacity = 10;
            Assert.Equal(buf.Capacity, dup.Capacity);
            dup.Capacity = 5;
            Assert.Equal(buf.Capacity, dup.Capacity);
        }
        finally
        {
            if (retainedDuplicate)
            {
                dup.Release();
            }
            buf.Release();
        }
    }

    // CLR: xUnit1024 forbids overloading the TestSliceContents test name.
    private void TestSliceContents0(bool retainedSlice)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected = NewBuffer(3).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected.WriteBytes(new byte[] { 4, 5, 6 });
        ByteBuf slice = retainedSlice ? buf.RetainedSlice(buf.ReaderIndex + 3, 3)
                                      : buf.Slice(buf.ReaderIndex + 3, 3);
        try
        {
            Assert.Equal(0, slice.CompareTo(expected));
            Assert.Equal(0, slice.CompareTo(slice.Duplicate()));
            ByteBuf b = slice.RetainedDuplicate();
            Assert.Equal(0, slice.CompareTo(b));
            b.Release();
            Assert.Equal(0, slice.CompareTo(slice.Slice(0, slice.Capacity)));
        }
        finally
        {
            if (retainedSlice)
            {
                slice.Release();
            }
            buf.Release();
            expected.Release();
        }
    }

    private void TestSliceReleaseOriginal(bool retainedSlice1, bool retainedSlice2)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected1 = NewBuffer(3).ResetWriterIndex();
        ByteBuf expected2 = NewBuffer(2).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected1.WriteBytes(new byte[] { 6, 7, 8 });
        expected2.WriteBytes(new byte[] { 7, 8 });
        ByteBuf slice1 = retainedSlice1 ? buf.RetainedSlice(buf.ReaderIndex + 5, 3)
                                        : buf.Slice(buf.ReaderIndex + 5, 3).Retain();
        Assert.Equal(0, slice1.CompareTo(expected1));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf slice2 = retainedSlice2 ? slice1.RetainedSlice(slice1.ReaderIndex + 1, 2)
                                        : slice1.Slice(slice1.ReaderIndex + 1, 2).Retain();
        Assert.Equal(0, slice2.CompareTo(expected2));

        // Cleanup the expected buffers used for testing.
        Assert.True(expected1.Release());
        Assert.True(expected2.Release());

        // The handler created a slice of the slice and is now done with it.
        slice2.Release();

        // The handler is now done with the original slice
        Assert.True(slice1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
    }

    private void TestDuplicateReleaseOriginal(bool retainedDuplicate1, bool retainedDuplicate2)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected = NewBuffer(8).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected.WriteBytes(buf, buf.ReaderIndex, buf.ReadableBytes);
        ByteBuf dup1 = retainedDuplicate1 ? buf.RetainedDuplicate()
                                          : buf.Duplicate().Retain();
        Assert.Equal(0, dup1.CompareTo(expected));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf dup2 = retainedDuplicate2 ? dup1.RetainedDuplicate()
                                          : dup1.Duplicate().Retain();
        Assert.Equal(0, dup2.CompareTo(expected));

        // Cleanup the expected buffers used for testing.
        Assert.True(expected.Release());

        // The handler created a slice of the slice and is now done with it.
        dup2.Release();

        // The handler is now done with the original slice
        Assert.True(dup1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, dup1.RefCnt);
        Assert.Equal(0, dup2.RefCnt);
    }

    private void TestMultipleRetainedSliceReleaseOriginal(bool retainedSlice1, bool retainedSlice2)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected1 = NewBuffer(3).ResetWriterIndex();
        ByteBuf expected2 = NewBuffer(2).ResetWriterIndex();
        ByteBuf expected3 = NewBuffer(2).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected1.WriteBytes(new byte[] { 6, 7, 8 });
        expected2.WriteBytes(new byte[] { 7, 8 });
        expected3.WriteBytes(new byte[] { 6, 7 });
        ByteBuf slice1 = retainedSlice1 ? buf.RetainedSlice(buf.ReaderIndex + 5, 3)
                                        : buf.Slice(buf.ReaderIndex + 5, 3).Retain();
        Assert.Equal(0, slice1.CompareTo(expected1));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf slice2 = retainedSlice2 ? slice1.RetainedSlice(slice1.ReaderIndex + 1, 2)
                                        : slice1.Slice(slice1.ReaderIndex + 1, 2).Retain();
        Assert.Equal(0, slice2.CompareTo(expected2));

        // The handler created a slice of the slice and is now done with it.
        slice2.Release();

        ByteBuf slice3 = slice1.RetainedSlice(slice1.ReaderIndex, 2);
        Assert.Equal(0, slice3.CompareTo(expected3));

        // The handler created another slice of the slice and is now done with it.
        slice3.Release();

        // The handler is now done with the original slice
        Assert.True(slice1.Release());

        // Cleanup the expected buffers used for testing.
        Assert.True(expected1.Release());
        Assert.True(expected2.Release());
        Assert.True(expected3.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
        Assert.Equal(0, slice3.RefCnt);
    }

    private void TestMultipleRetainedDuplicateReleaseOriginal(bool retainedDuplicate1, bool retainedDuplicate2)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected = NewBuffer(8).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected.WriteBytes(buf, buf.ReaderIndex, buf.ReadableBytes);
        ByteBuf dup1 = retainedDuplicate1 ? buf.RetainedDuplicate()
                                          : buf.Duplicate().Retain();
        Assert.Equal(0, dup1.CompareTo(expected));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf dup2 = retainedDuplicate2 ? dup1.RetainedDuplicate()
                                          : dup1.Duplicate().Retain();
        Assert.Equal(0, dup2.CompareTo(expected));
        Assert.Equal(0, dup2.CompareTo(dup2.Duplicate()));
        Assert.Equal(0, dup2.CompareTo(dup2.Slice()));

        ByteBuf tmpBuf = dup2.RetainedDuplicate();
        Assert.Equal(0, dup2.CompareTo(tmpBuf));
        tmpBuf.Release();
        tmpBuf = dup2.RetainedSlice();
        Assert.Equal(0, dup2.CompareTo(tmpBuf));
        tmpBuf.Release();

        // The handler created a slice of the slice and is now done with it.
        dup2.Release();

        ByteBuf dup3 = dup1.RetainedDuplicate();
        Assert.Equal(0, dup3.CompareTo(expected));

        // The handler created another slice of the slice and is now done with it.
        dup3.Release();

        // The handler is now done with the original slice
        Assert.True(dup1.Release());

        // Cleanup the expected buffers used for testing.
        Assert.True(expected.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, dup1.RefCnt);
        Assert.Equal(0, dup2.RefCnt);
        Assert.Equal(0, dup3.RefCnt);
    }

    // CLR: xUnit1024 forbids overloading the TestDuplicateContents test name.
    private void TestDuplicateContents0(bool retainedDuplicate)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        ByteBuf dup = retainedDuplicate ? buf.RetainedDuplicate() : buf.Duplicate();
        try
        {
            Assert.Equal(0, dup.CompareTo(buf));
            Assert.Equal(0, dup.CompareTo(dup.Duplicate()));
            ByteBuf b = dup.RetainedDuplicate();
            Assert.Equal(0, dup.CompareTo(b));
            b.Release();
            Assert.Equal(0, dup.CompareTo(dup.Slice(dup.ReaderIndex, dup.ReadableBytes)));
        }
        finally
        {
            if (retainedDuplicate)
            {
                dup.Release();
            }
            buf.Release();
        }
    }

    [Fact]
    public void TestDuplicateRelease()
    {
        ByteBuf buf = NewBuffer(8);
        Assert.Equal(1, buf.RefCnt);
        Assert.True(buf.Duplicate().Release());
        Assert.Equal(0, buf.RefCnt);
    }
}
