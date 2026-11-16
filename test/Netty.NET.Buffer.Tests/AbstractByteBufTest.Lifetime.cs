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
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// CLR: the inherited released-buffer, derived-view lifetime, unreleasable-wrapper and refCnt bodies of
// the original AbstractByteBufTest, run against every concrete fixture's NewBuffer as in the source.
// ByteBufLifetimeFixtureTest keeps the same bodies over its eight-provider/three-entry-form matrix.
public abstract partial class AbstractByteBufTest
{
    [Fact]
    public void TestGetBytesAfterRelease()
    {
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().GetBytes(0, buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestGetBytesAfterRelease2()
    {
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().GetBytes(0, buffer, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestGetBytesAfterRelease3()
    {
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().GetBytes(0, buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestGetBytesAfterRelease6()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBytes(0, new byte[8].AsSpan());
        });
    }

    [Fact]
    public void TestGetBytesAfterRelease7()
    {
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBytes(0, stream, 1);
        });
    }

    [Fact]
    public void TestGetBytesAfterRelease8()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBytes(0, socket, 1);
        });
    }

    [Fact]
    public void TestSetBytesAfterRelease()
    {
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().SetBytes(0, buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestSetBytesAfterRelease2()
    {
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().SetBytes(0, buffer, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestSetBytesAfterRelease3()
    {
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().SetBytes(0, buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestSetBytesAfterRelease6()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBytes(0, new byte[8].AsSpan());
        });
    }

    [Fact]
    public void TestSetBytesAfterRelease7()
    {
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBytes(0, stream, 1);
        });
    }

    [Fact]
    public void TestSetBytesAfterRelease8()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBytes(0, socket, 1);
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(1);
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease2()
    {
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().ReadBytes(buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadBytesAfterRelease3()
    {
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().ReadBytes(buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadBytesAfterRelease4()
    {
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().ReadBytes(buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadBytesAfterRelease7()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(new byte[8].AsSpan());
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease8()
    {
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(stream, 1);
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease9()
    {
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(stream, 1);
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease10()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(socket, 1);
        });
    }

    [Fact]
    public void TestWriteBytesAfterRelease()
    {
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().WriteBytes(buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestWriteBytesAfterRelease2()
    {
        ByteBuf buffer = Unpooled.CopiedBuffer(new byte[8]);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().WriteBytes(buffer, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestWriteBytesAfterRelease3()
    {
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().WriteBytes(buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestWriteBytesAfterRelease6()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBytes(new byte[8].AsSpan());
        });
    }

    [Fact]
    public void TestWriteBytesAfterRelease7()
    {
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBytes(stream, 1);
        });
    }

    [Fact]
    public void TestWriteBytesAfterRelease8()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBytes(socket, 1);
        });
    }

    [Fact]
    public void TestNioBufferAfterRelease()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        ByteBuf buffer = ReleasedBuffer();
        Assert.Throws<IllegalReferenceCountException>(() => buffer.AsReadOnlyMemory(buffer.ReaderIndex, buffer.ReadableBytes));
    }

    [Fact]
    public void TestNioBufferAfterRelease1()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedBuffer().AsReadOnlyMemory(0, 1));
    }

    [Fact]
    public virtual void TestInternalNioBufferAfterRelease()
    {
        TestInternalNioBufferAfterRelease0<IllegalReferenceCountException>();
    }

    // CLR: the expected exception type is a generic argument instead of a Class<? extends Throwable>.
    protected void TestInternalNioBufferAfterRelease0<T>() where T : Exception
    {
        ByteBuf releasedBuffer = ReleasedBuffer();
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<T>(() => releasedBuffer.AsMemory(releasedBuffer.ReaderIndex, 1));
    }

    [Fact]
    public void TestNioBuffersAfterRelease()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        ByteBuf buffer = ReleasedBuffer();
        Assert.Throws<IllegalReferenceCountException>(() => buffer.AsReadOnlySequence(buffer.ReaderIndex, buffer.ReadableBytes));
    }

    [Fact]
    public void TestNioBuffersAfterRelease2()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedBuffer().AsReadOnlySequence(0, 1));
    }

    [Fact]
    public void TestArrayAfterRelease()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedBuffer().AsMemory(0, 0));
    }

    [Fact]
    public void TestMemoryAddressAfterRelease()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            using var pin = ReleasedBuffer().AsReadOnlyMemory(0, 0).Pin();
        });
    }

    [Fact]
    public void TestSliceAfterReleaseRetainedSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        AssertSliceFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestSliceAfterReleaseRetainedSliceDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.Duplicate();
        AssertSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestSliceAfterReleaseRetainedSliceRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.RetainedDuplicate();
        AssertSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestSliceAfterReleaseRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertSliceFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestSliceAfterReleaseRetainedDuplicateSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        ByteBuf buf3 = buf2.Slice(0, 1);
        AssertSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestRetainedSliceAfterReleaseRetainedSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        AssertRetainedSliceFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestRetainedSliceAfterReleaseRetainedSliceDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.Duplicate();
        AssertRetainedSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestRetainedSliceAfterReleaseRetainedSliceRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.RetainedDuplicate();
        AssertRetainedSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestRetainedSliceAfterReleaseRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertRetainedSliceFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestRetainedSliceAfterReleaseRetainedDuplicateSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        ByteBuf buf3 = buf2.Slice(0, 1);
        AssertRetainedSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestDuplicateAfterReleaseRetainedSliceDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.Duplicate();
        AssertDuplicateFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestDuplicateAfterReleaseRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertDuplicateFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestDuplicateAfterReleaseRetainedDuplicateSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        ByteBuf buf3 = buf2.Slice(0, 1);
        AssertDuplicateFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestRetainedDuplicateAfterReleaseRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertRetainedDuplicateFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestRetainedDuplicateAfterReleaseDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.Duplicate();
        AssertRetainedDuplicateFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestRetainedDuplicateAfterReleaseRetainedSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        AssertRetainedDuplicateFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestRetainedSliceAndRetainedDuplicateContentIsExpected()
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected1 = NewBuffer(6).ResetWriterIndex();
        ByteBuf expected2 = NewBuffer(5).ResetWriterIndex();
        ByteBuf expected3 = NewBuffer(4).ResetWriterIndex();
        ByteBuf expected4 = NewBuffer(3).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected1.WriteBytes(new byte[] { 2, 3, 4, 5, 6, 7 });
        expected2.WriteBytes(new byte[] { 3, 4, 5, 6, 7 });
        expected3.WriteBytes(new byte[] { 4, 5, 6, 7 });
        expected4.WriteBytes(new byte[] { 5, 6, 7 });

        ByteBuf slice1 = buf.RetainedSlice(buf.ReaderIndex + 1, 6);
        Assert.Equal(0, slice1.CompareTo(expected1));
        Assert.Equal(0, slice1.CompareTo(buf.Slice(buf.ReaderIndex + 1, 6)));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        // Advance the reader index on the slice.
        slice1.ReadByte();

        ByteBuf dup1 = slice1.RetainedDuplicate();
        Assert.Equal(0, dup1.CompareTo(expected2));
        Assert.Equal(0, dup1.CompareTo(slice1.Duplicate()));

        // Advance the reader index on dup1.
        dup1.ReadByte();

        ByteBuf dup2 = dup1.Duplicate();
        Assert.Equal(0, dup2.CompareTo(expected3));

        // Advance the reader index on dup2.
        dup2.ReadByte();

        ByteBuf slice2 = dup2.RetainedSlice(dup2.ReaderIndex, 3);
        Assert.Equal(0, slice2.CompareTo(expected4));
        Assert.Equal(0, slice2.CompareTo(dup2.Slice(dup2.ReaderIndex, 3)));

        // Cleanup the expected buffers used for testing.
        Assert.True(expected1.Release());
        Assert.True(expected2.Release());
        Assert.True(expected3.Release());
        Assert.True(expected4.Release());

        slice2.Release();
        dup2.Release();

        Assert.Equal(slice2.RefCnt, dup2.RefCnt);
        Assert.Equal(dup2.RefCnt, dup1.RefCnt);

        // The handler is now done with the original slice
        Assert.True(slice1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
        Assert.Equal(0, dup1.RefCnt);
        Assert.Equal(0, dup2.RefCnt);
    }

    [Fact]
    public void TestRetainedDuplicateAndRetainedSliceContentIsExpected()
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected1 = NewBuffer(6).ResetWriterIndex();
        ByteBuf expected2 = NewBuffer(5).ResetWriterIndex();
        ByteBuf expected3 = NewBuffer(4).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected1.WriteBytes(new byte[] { 2, 3, 4, 5, 6, 7 });
        expected2.WriteBytes(new byte[] { 3, 4, 5, 6, 7 });
        expected3.WriteBytes(new byte[] { 5, 6, 7 });

        ByteBuf dup1 = buf.RetainedDuplicate();
        Assert.Equal(0, dup1.CompareTo(buf));
        Assert.Equal(0, dup1.CompareTo(buf.Slice()));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        // Advance the reader index on the dup.
        dup1.ReadByte();

        ByteBuf slice1 = dup1.RetainedSlice(dup1.ReaderIndex, 6);
        Assert.Equal(0, slice1.CompareTo(expected1));
        Assert.Equal(0, slice1.CompareTo(slice1.Duplicate()));

        // Advance the reader index on slice1.
        slice1.ReadByte();

        ByteBuf dup2 = slice1.Duplicate();
        Assert.Equal(0, dup2.CompareTo(slice1));

        // Advance the reader index on dup2.
        dup2.ReadByte();

        ByteBuf slice2 = dup2.RetainedSlice(dup2.ReaderIndex + 1, 3);
        Assert.Equal(0, slice2.CompareTo(expected3));
        Assert.Equal(0, slice2.CompareTo(dup2.Slice(dup2.ReaderIndex + 1, 3)));

        // Cleanup the expected buffers used for testing.
        Assert.True(expected1.Release());
        Assert.True(expected2.Release());
        Assert.True(expected3.Release());

        slice2.Release();
        slice1.Release();

        Assert.Equal(slice2.RefCnt, dup2.RefCnt);
        Assert.Equal(dup2.RefCnt, slice1.RefCnt);

        // The handler is now done with the original slice
        Assert.True(dup1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
        Assert.Equal(0, dup1.RefCnt);
        Assert.Equal(0, dup2.RefCnt);
    }

    [Fact]
    public void TestMultipleLevelRetainedSlice1()
    {
        TestMultipleLevelRetainedSliceWithNonRetained(true, true);
    }

    [Fact]
    public void TestMultipleLevelRetainedSlice2()
    {
        TestMultipleLevelRetainedSliceWithNonRetained(true, false);
    }

    [Fact]
    public void TestMultipleLevelRetainedSlice3()
    {
        TestMultipleLevelRetainedSliceWithNonRetained(false, true);
    }

    [Fact]
    public void TestMultipleLevelRetainedSlice4()
    {
        TestMultipleLevelRetainedSliceWithNonRetained(false, false);
    }

    [Fact]
    public void TestRetainedSliceUnreleasable1()
    {
        TestRetainedSliceUnreleasable(true, true);
    }

    [Fact]
    public void TestRetainedSliceUnreleasable2()
    {
        TestRetainedSliceUnreleasable(true, false);
    }

    [Fact]
    public void TestRetainedSliceUnreleasable3()
    {
        TestRetainedSliceUnreleasable(false, true);
    }

    [Fact]
    public void TestRetainedSliceUnreleasable4()
    {
        TestRetainedSliceUnreleasable(false, false);
    }

    [Fact]
    public void TestReadRetainedSliceUnreleasable1()
    {
        TestReadRetainedSliceUnreleasable(true, true);
    }

    [Fact]
    public void TestReadRetainedSliceUnreleasable2()
    {
        TestReadRetainedSliceUnreleasable(true, false);
    }

    [Fact]
    public void TestReadRetainedSliceUnreleasable3()
    {
        TestReadRetainedSliceUnreleasable(false, true);
    }

    [Fact]
    public void TestReadRetainedSliceUnreleasable4()
    {
        TestReadRetainedSliceUnreleasable(false, false);
    }

    [Fact]
    public void TestRetainedDuplicateUnreleasable1()
    {
        TestRetainedDuplicateUnreleasable(true, true);
    }

    [Fact]
    public void TestRetainedDuplicateUnreleasable2()
    {
        TestRetainedDuplicateUnreleasable(true, false);
    }

    [Fact]
    public void TestRetainedDuplicateUnreleasable3()
    {
        TestRetainedDuplicateUnreleasable(false, true);
    }

    [Fact]
    public void TestRetainedDuplicateUnreleasable4()
    {
        TestRetainedDuplicateUnreleasable(false, false);
    }

    // Test-case trying to reproduce:
    // https://github.com/netty/netty/issues/2843
    [Fact]
    public void TestRefCnt()
    {
        TestRefCnt0(false);
    }

    [Fact]
    public void TestRefCnt2()
    {
        TestRefCnt0(true);
    }

    private static void AssertSliceFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.RefCnt > 0)
            {
                buf.Release();
            }
        }
        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.RefCnt);
                buf.Slice();
            });
        }
    }

    private static void AssertRetainedSliceFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.RefCnt > 0)
            {
                buf.Release();
            }
        }
        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.RefCnt);
                buf.RetainedSlice();
            });
        }
    }

    private static void AssertDuplicateFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.RefCnt > 0)
            {
                buf.Release();
            }
        }
        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.RefCnt);
                buf.Duplicate();
            });
        }
    }

    private static void AssertRetainedDuplicateFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.RefCnt > 0)
            {
                buf.Release();
            }
        }
        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.RefCnt);
                buf.RetainedDuplicate();
            });
        }
    }

    private void TestRetainedSliceUnreleasable(bool initRetainedSlice, bool finalRetainedSlice)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf buf1 = initRetainedSlice ? buf.RetainedSlice() : buf.Slice().Retain();
        ByteBuf buf2 = Unpooled.UnreleasableBuffer(buf1);
        ByteBuf buf3 = finalRetainedSlice ? buf2.RetainedSlice() : buf2.Slice().Retain();
        Assert.False(buf3.Release());
        Assert.False(buf2.Release());
        buf1.Release();
        Assert.True(buf.Release());
        Assert.Equal(0, buf1.RefCnt);
        Assert.Equal(0, buf.RefCnt);
    }

    private void TestReadRetainedSliceUnreleasable(bool initRetainedSlice, bool finalRetainedSlice)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf buf1 = initRetainedSlice ? buf.RetainedSlice() : buf.Slice().Retain();
        ByteBuf buf2 = Unpooled.UnreleasableBuffer(buf1);
        ByteBuf buf3 = finalRetainedSlice ? buf2.ReadRetainedSlice(buf2.ReadableBytes)
                                          : buf2.ReadSlice(buf2.ReadableBytes).Retain();
        Assert.False(buf3.Release());
        Assert.False(buf2.Release());
        buf1.Release();
        Assert.True(buf.Release());
        Assert.Equal(0, buf1.RefCnt);
        Assert.Equal(0, buf.RefCnt);
    }

    private void TestRetainedDuplicateUnreleasable(bool initRetainedDuplicate, bool finalRetainedDuplicate)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf buf1 = initRetainedDuplicate ? buf.RetainedDuplicate() : buf.Duplicate().Retain();
        ByteBuf buf2 = Unpooled.UnreleasableBuffer(buf1);
        ByteBuf buf3 = finalRetainedDuplicate ? buf2.RetainedDuplicate() : buf2.Duplicate().Retain();
        Assert.False(buf3.Release());
        Assert.False(buf2.Release());
        buf1.Release();
        Assert.True(buf.Release());
        Assert.Equal(0, buf1.RefCnt);
        Assert.Equal(0, buf.RefCnt);
    }

    private void TestMultipleLevelRetainedSliceWithNonRetained(bool doSlice1, bool doSlice2)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected1 = NewBuffer(6).ResetWriterIndex();
        ByteBuf expected2 = NewBuffer(4).ResetWriterIndex();
        ByteBuf expected3 = NewBuffer(2).ResetWriterIndex();
        ByteBuf expected4SliceSlice = NewBuffer(1).ResetWriterIndex();
        ByteBuf expected4DupSlice = NewBuffer(1).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected1.WriteBytes(new byte[] { 2, 3, 4, 5, 6, 7 });
        expected2.WriteBytes(new byte[] { 3, 4, 5, 6 });
        expected3.WriteBytes(new byte[] { 4, 5 });
        expected4SliceSlice.WriteBytes(new byte[] { 5 });
        expected4DupSlice.WriteBytes(new byte[] { 4 });

        ByteBuf slice1 = buf.RetainedSlice(buf.ReaderIndex + 1, 6);
        Assert.Equal(0, slice1.CompareTo(expected1));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf slice2 = slice1.RetainedSlice(slice1.ReaderIndex + 1, 4);
        Assert.Equal(0, slice2.CompareTo(expected2));
        Assert.Equal(0, slice2.CompareTo(slice2.Duplicate()));
        Assert.Equal(0, slice2.CompareTo(slice2.Slice()));

        ByteBuf tmpBuf = slice2.RetainedDuplicate();
        Assert.Equal(0, slice2.CompareTo(tmpBuf));
        tmpBuf.Release();
        tmpBuf = slice2.RetainedSlice();
        Assert.Equal(0, slice2.CompareTo(tmpBuf));
        tmpBuf.Release();

        ByteBuf slice3 = doSlice1 ? slice2.Slice(slice2.ReaderIndex + 1, 2) : slice2.Duplicate();
        if (doSlice1)
        {
            Assert.Equal(0, slice3.CompareTo(expected3));
        }
        else
        {
            Assert.Equal(0, slice3.CompareTo(expected2));
        }

        ByteBuf slice4 = doSlice2 ? slice3.Slice(slice3.ReaderIndex + 1, 1) : slice3.Duplicate();
        if (doSlice1 && doSlice2)
        {
            Assert.Equal(0, slice4.CompareTo(expected4SliceSlice));
        }
        else if (doSlice2)
        {
            Assert.Equal(0, slice4.CompareTo(expected4DupSlice));
        }
        else
        {
            Assert.Equal(0, slice3.CompareTo(slice4));
        }

        // Cleanup the expected buffers used for testing.
        Assert.True(expected1.Release());
        Assert.True(expected2.Release());
        Assert.True(expected3.Release());
        Assert.True(expected4SliceSlice.Release());
        Assert.True(expected4DupSlice.Release());

        // Slice 4, 3, and 2 should effectively "share" a reference count.
        slice4.Release();
        Assert.Equal(slice3.RefCnt, slice2.RefCnt);
        Assert.Equal(slice3.RefCnt, slice4.RefCnt);

        // Slice 1 should also release the original underlying buffer without throwing exceptions
        Assert.True(slice1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
        Assert.Equal(0, slice3.RefCnt);
    }

    private void TestRefCnt0(bool parameter)
    {
        for (int i = 0; i < 10; ++i)
        {
            using var latch = new ManualResetEventSlim();
            using var innerLatch = new ManualResetEventSlim();
            ByteBuf buffer = NewBuffer(4);
            Assert.Equal(1, buffer.RefCnt);
            Exception innerThrowable = null;
            int count = int.MaxValue;
            Thread reader = null;
            var releaser = new Thread(() =>
            {
                try
                {
                    Assert.True(parameter ? buffer.Release(buffer.RefCnt) : buffer.Release());
                    reader = new Thread(() =>
                    {
                        count = buffer.RefCnt;
                        latch.Set();
                    });
                    reader.Start();
                    // Keep Thread alive a bit so the ThreadLocal caches are not freed
                    innerLatch.Wait();
                }
                catch (Exception cause)
                {
                    innerThrowable = cause;
                    latch.Set();
                }
            });
            releaser.Start();
            try
            {
                Assert.True(latch.Wait(TimeSpan.FromSeconds(30)));
                Assert.Null(innerThrowable);
                Assert.Equal(0, count);
            }
            finally
            {
                innerLatch.Set();
                releaser.Join();
                reader?.Join();
                if (buffer.RefCnt > 0)
                    buffer.Release();
            }
        }
    }
}
