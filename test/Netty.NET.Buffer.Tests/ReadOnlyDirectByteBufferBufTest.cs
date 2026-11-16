/*
 * Copyright 2013 The Netty Project
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
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Buffer.Tests.AbstractByteBufTest.StaticImports;

namespace Netty.NET.Buffer.Tests;

// CLR: java.nio.ByteBuffer is replaced by Memory<byte> (src/Netty.NET.Buffer/ReadOnlyMemoryByteBuf.cs).
// put/putInt/putShort/putLong become explicit big-endian writes, flip()/position()/limit() select the
// [position, limit) slice, and asReadOnlyBuffer() is the implicit Memory<T> to ReadOnlyMemory<T> conversion
// (https://learn.microsoft.com/en-us/dotnet/api/system.memory-1.op_implicit?view=net-10.0).
// ReadOnlyBufferException maps to NotSupportedException and IndexOutOfBoundsException to
// ArgumentOutOfRangeException, as in ReadOnlyByteBufTest.cs.
public class ReadOnlyDirectByteBufferBufTest : IDisposable
{
    // CLR: allocateDirect storage is owned native memory; every owner is freed after the test.
    private readonly NativeMemoryAllocator _native = new();
    private readonly List<NativeMemoryOwner> _owners = new();

    protected virtual ByteBuf Buffer(ReadOnlyMemory<byte> buffer)
    {
        return new ReadOnlyMemoryByteBuf(UnpooledByteBufAllocator.Default, buffer);
    }

    protected virtual Memory<byte> Allocate(int size)
    {
        return AllocateDirect(size);
    }

    // CLR: ByteBuffer.allocateDirect zero-fills its content, so the native memory is cleared.
    protected Memory<byte> AllocateDirect(int size)
    {
        NativeMemoryOwner owner = _native.Allocate(size, clear: true);
        _owners.Add(owner);
        return owner.Memory;
    }

    public void Dispose()
    {
        foreach (NativeMemoryOwner owner in _owners) owner.Dispose();
        Assert.Equal(0, _native.ReservedBytes);
    }

    [Fact]
    public void TestIsContiguous()
    {
        ByteBuf buf = Buffer(Allocate(4));
        Assert.True(buf.IsContiguous);
        buf.Release();
    }

    [Fact]
    public void ShouldIndicateNotWritable()
    {
        ByteBuf buf = Buffer(Allocate(8)).Clear();
        try
        {
            Assert.False(buf.IsWritable());
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void ShouldIndicateNotWritableAnyNumber()
    {
        ByteBuf buf = Buffer(Allocate(8)).Clear();
        try
        {
            Assert.False(buf.IsWritable(1));
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void EnsureWritableIntStatusShouldFailButNotThrow()
    {
        ByteBuf buf = Buffer(Allocate(8)).Clear();
        try
        {
            int result = buf.EnsureWritable(1, false);
            Assert.Equal(1, result);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void EnsureWritableForceIntStatusShouldFailButNotThrow()
    {
        ByteBuf buf = Buffer(Allocate(8)).Clear();
        try
        {
            int result = buf.EnsureWritable(1, true);
            Assert.Equal(1, result);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void EnsureWritableShouldThrow()
    {
        ByteBuf buf = Buffer(Allocate(8)).Clear();
        try
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                buf.EnsureWritable(1);
            });
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetByte()
    {
        ByteBuf buf = Buffer(Allocate(8));
        try
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                buf.SetByte(0, 1);
            });
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetInt()
    {
        ByteBuf buf = Buffer(Allocate(8));
        try
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                buf.SetInt(0, 1);
            });
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetShort()
    {
        ByteBuf buf = Buffer(Allocate(8));
        try
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                buf.SetShort(0, 1);
            });
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetMedium()
    {
        ByteBuf buf = Buffer(Allocate(8));
        try
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                buf.SetMedium(0, 1);
            });
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetLong()
    {
        ByteBuf buf = Buffer(Allocate(8));
        try
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                buf.SetLong(0, 1);
            });
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetBytesViaArray()
    {
        ByteBuf buf = Buffer(Allocate(8));
        try
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                // CLR: String.getBytes() uses the platform charset; this ASCII text has the same UTF-8 bytes.
                buf.SetBytes(0, Encoding.UTF8.GetBytes("test"));
            });
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetBytesViaBuffer()
    {
        ByteBuf buf = Buffer(Allocate(8));
        ByteBuf copy = Unpooled.CopyInt(1);
        try
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                buf.SetBytes(0, copy);
            });
        }
        finally
        {
            buf.Release();
            copy.Release();
        }
    }

    [Fact]
    public void TestSetBytesViaStream()
    {
        ByteBuf buf = Buffer(AllocateDirect(8));
        try
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                buf.SetBytes(0, new MemoryStream(Encoding.UTF8.GetBytes("test")), 2);
            });
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestGetReadByte()
    {
        Memory<byte> nioBuffer = Allocate(2);
        new byte[] { (byte)1, (byte)2 }.CopyTo(nioBuffer.Span);
        ByteBuf buf = Buffer(nioBuffer[..2]);

        Assert.Equal(1, buf.GetByte(0));
        Assert.Equal(2, buf.GetByte(1));

        Assert.Equal(1, buf.ReadByte());
        Assert.Equal(2, buf.ReadByte());
        Assert.False(buf.IsReadable());

        buf.Release();
    }

    [Fact]
    public void TestGetReadInt()
    {
        Memory<byte> nioBuffer = Allocate(8);
        BinaryPrimitives.WriteInt32BigEndian(nioBuffer.Span, 1);
        BinaryPrimitives.WriteInt32BigEndian(nioBuffer.Span[4..], 2);
        ByteBuf buf = Buffer(nioBuffer[..8]);

        Assert.Equal(1, buf.GetInt(0));
        Assert.Equal(2, buf.GetInt(4));

        Assert.Equal(1, buf.ReadInt());
        Assert.Equal(2, buf.ReadInt());
        Assert.False(buf.IsReadable());

        buf.Release();
    }

    [Fact]
    public void TestGetReadShort()
    {
        Memory<byte> nioBuffer = Allocate(8);
        BinaryPrimitives.WriteInt16BigEndian(nioBuffer.Span, 1);
        BinaryPrimitives.WriteInt16BigEndian(nioBuffer.Span[2..], 2);
        ByteBuf buf = Buffer(nioBuffer[..4]);

        Assert.Equal(1, buf.GetShort(0));
        Assert.Equal(2, buf.GetShort(2));

        Assert.Equal(1, buf.ReadShort());
        Assert.Equal(2, buf.ReadShort());
        Assert.False(buf.IsReadable());

        buf.Release();
    }

    [Fact]
    public void TestGetReadLong()
    {
        Memory<byte> nioBuffer = Allocate(16);
        BinaryPrimitives.WriteInt64BigEndian(nioBuffer.Span, 1);
        BinaryPrimitives.WriteInt64BigEndian(nioBuffer.Span[8..], 2);
        ByteBuf buf = Buffer(nioBuffer[..16]);

        Assert.Equal(1, buf.GetLong(0));
        Assert.Equal(2, buf.GetLong(8));

        Assert.Equal(1, buf.ReadLong());
        Assert.Equal(2, buf.ReadLong());
        Assert.False(buf.IsReadable());

        buf.Release();
    }

    [Fact]
    public void TestGetBytesByteBuffer()
    {
        byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f', (byte)'g' };
        // Ensure destination buffer is bigger then what is in the ByteBuf.
        // CLR: a heap ByteBuffer destination transfers its remaining() bytes; a Span transfers its length.
        byte[] nioBuffer = new byte[bytes.Length + 1];
        Memory<byte> source = Allocate(bytes.Length);
        bytes.CopyTo(source.Span);
        ByteBuf buffer = Buffer(source[..bytes.Length]);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                buffer.GetBytes(buffer.ReaderIndex, nioBuffer.AsSpan());
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestCopy()
    {
        Memory<byte> nioBuffer = Allocate(16);
        BinaryPrimitives.WriteInt64BigEndian(nioBuffer.Span, 1);
        BinaryPrimitives.WriteInt64BigEndian(nioBuffer.Span[8..], 2);
        ByteBuf buf = Buffer(nioBuffer[..16]);
        ByteBuf copy = buf.Copy();

        Assert.Equal(buf, copy);

        buf.Release();
        copy.Release();
    }

    [Fact]
    public void TestCopyWithOffset()
    {
        Memory<byte> nioBuffer = Allocate(16);
        BinaryPrimitives.WriteInt64BigEndian(nioBuffer.Span, 1);
        BinaryPrimitives.WriteInt64BigEndian(nioBuffer.Span[8..], 2);
        ByteBuf buf = Buffer(nioBuffer[..16]);
        ByteBuf copy = buf.Copy(1, 9);

        Assert.Equal(buf.Slice(1, 9), copy);

        buf.Release();
        copy.Release();
    }

    // Test for https://github.com/netty/netty/issues/1708
    [Fact]
    public void TestWrapBufferWithNonZeroPosition()
    {
        Memory<byte> nioBuffer = Allocate(16);
        BinaryPrimitives.WriteInt64BigEndian(nioBuffer.Span, 1);
        // CLR: flip() sets limit 8 and position(1) starts the captured range at index 1.
        ByteBuf buf = Buffer(nioBuffer[1..8]);

        ByteBuf slice = buf.Slice();
        Assert.Equal(buf, slice);

        buf.Release();
    }

    [Fact]
    public void TestWrapBufferRoundTrip()
    {
        Memory<byte> nioBuffer = Allocate(16);
        BinaryPrimitives.WriteInt32BigEndian(nioBuffer.Span, 1);
        BinaryPrimitives.WriteInt32BigEndian(nioBuffer.Span[4..], 2);
        ByteBuf buf = Buffer(nioBuffer[..8]);

        Assert.Equal(1, buf.ReadInt());

        // CLR: nioBuffer() is the readable ReadOnlyMemory<byte> view.
        ReadOnlyMemory<byte> readable = buf.ReadableMemory;

        // Ensure this can be accessed without throwing a BufferUnderflowException
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(readable.Span));

        buf.Release();
    }

    [Fact]
    public void TestWrapMemoryMapped()
    {
        FileInfo file = PlatformDependent.CreateTempFile("netty-test", "tmp", null);
        FileStream output = null;
        FileStream input = null;
        MemoryMappedFile map = null;
        MemoryMappedViewAccessor view = null;
        MappedMemory mapped = null;
        ByteBuf b1 = null;
        ByteBuf b2 = null;

        try
        {
            output = new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            byte[] bytes = new byte[1024];
            Random.Shared.NextBytes(bytes);
            output.Write(bytes);
            // CLR: FileStream buffers what FileChannel.write has already issued to the file.
            output.Flush();

            input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // CLR: FileChannel.map(READ_ONLY, 0, size) becomes a read-only MemoryMappedFile view
            // (https://learn.microsoft.com/en-us/dotnet/api/system.io.memorymappedfiles.memorymappedfile.createviewaccessor?view=net-10.0)
            // exposed as ReadOnlyMemory<byte> through a test MemoryManager over the mapped pointer.
            map = MemoryMappedFile.CreateFromFile(input, null, 0, MemoryMappedFileAccess.Read,
                HandleInheritability.None, true);
            view = map.CreateViewAccessor(0, input.Length, MemoryMappedFileAccess.Read);
            mapped = new MappedMemory(view, checked((int)input.Length));
            ReadOnlyMemory<byte> m = mapped.Memory;

            b1 = Buffer(m);

            // CLR: duplicate().position(2).limit(4) selects the range [2, 4) of the same mapping.
            ReadOnlyMemory<byte> dup = m;
            dup = dup[2..4];

            b2 = Buffer(dup);

            Assert.Equal(b2, b1.Slice(2, 2));
        }
        finally
        {
            if (b1 != null)
            {
                b1.Release();
            }
            if (b2 != null)
            {
                b2.Release();
            }
            if (output != null)
            {
                output.Dispose();
            }
            // CLR: the mapping is released explicitly before the input channel closes; the JVM
            // unmaps a MappedByteBuffer only when it is garbage collected.
            ((IDisposable)mapped)?.Dispose();
            view?.Dispose();
            map?.Dispose();
            if (input != null)
            {
                input.Dispose();
            }
            file.Delete();
        }
    }

    [Fact]
    public void TestDuplicate()
    {
        ByteBuf buf = Buffer(Allocate(8));
        try
        {
            Assert.True(buf.Duplicate().IsReadOnly);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSlice()
    {
        ByteBuf buf = Buffer(Allocate(8));
        try
        {
            Assert.True(buf.Slice().IsReadOnly);
        }
        finally
        {
            buf.Release();
        }
    }

    public enum DerivedParam
    {
        None,
        Duplicate,
        Slice
    }

    // CLR: @EnumSource supplies every DerivedParam value.
    [Theory]
    [InlineData(DerivedParam.None)]
    [InlineData(DerivedParam.Duplicate)]
    [InlineData(DerivedParam.Slice)]
    public void TestIsWritable(DerivedParam param)
    {
        Memory<byte> buffer = Allocate(24);
        ByteBuf buf = Buffer(buffer);
        buf.WriterIndex = 8;

        switch (param)
        {
            case DerivedParam.None:
                break;
            case DerivedParam.Duplicate:
                buf = buf.Duplicate();
                break;
            case DerivedParam.Slice:
                buf = buf.Slice(0, buf.Capacity);
                buf.WriterIndex = 8;
                break;
        }
        try
        {
            Assert.False(buf.IsWritable());
            Assert.False(buf.IsWritable(1));
        }
        finally
        {
            buf.Release();
        }
    }

    // CLR: the original @Timeout(30) bound is enforced inside the shared AbstractByteBufTest helper.
    [Fact]
    public void TestDuplicateReadGatheringByteChannelMultipleThreads()
    {
        byte[] bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        Memory<byte> nioBuffer = Allocate(bytes.Length);
        bytes.CopyTo(nioBuffer.Span);
        ByteBuf buffer = Buffer(nioBuffer[..bytes.Length]);
        try
        {
            TestReadGatheringByteChannelMultipleThreads(buffer, bytes, false);
        }
        finally
        {
            buffer.Release();
        }
    }

    // CLR: the original @Timeout(30) bound is enforced inside the shared AbstractByteBufTest helper.
    [Fact]
    public void TestSliceReadGatheringByteChannelMultipleThreads()
    {
        byte[] bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        Memory<byte> nioBuffer = Allocate(bytes.Length);
        bytes.CopyTo(nioBuffer.Span);
        ByteBuf buffer = Buffer(nioBuffer[..bytes.Length]);
        try
        {
            TestReadGatheringByteChannelMultipleThreads(buffer, bytes, true);
        }
        finally
        {
            buffer.Release();
        }
    }

    // CLR: the original @Timeout(30) bound is enforced inside the shared AbstractByteBufTest helper.
    [Fact]
    public void TestDuplicateReadOutputStreamMultipleThreads()
    {
        byte[] bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        Memory<byte> nioBuffer = Allocate(bytes.Length);
        bytes.CopyTo(nioBuffer.Span);
        ByteBuf buffer = Buffer(nioBuffer[..bytes.Length]);
        try
        {
            TestReadOutputStreamMultipleThreads(buffer, bytes, false);
        }
        finally
        {
            buffer.Release();
        }
    }

    // CLR: the original @Timeout(30) bound is enforced inside the shared AbstractByteBufTest helper.
    [Fact]
    public void TestSliceReadOutputStreamMultipleThreads()
    {
        byte[] bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        Memory<byte> nioBuffer = Allocate(bytes.Length);
        bytes.CopyTo(nioBuffer.Span);
        ByteBuf buffer = Buffer(nioBuffer[..bytes.Length]);
        try
        {
            TestReadOutputStreamMultipleThreads(buffer, bytes, true);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public async Task TestDuplicateBytesInArrayMultipleThreads()
    {
        byte[] bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        Memory<byte> nioBuffer = Allocate(bytes.Length);
        bytes.CopyTo(nioBuffer.Span);
        ByteBuf buffer = Buffer(nioBuffer[..bytes.Length]);
        try
        {
            await TestBytesInArrayMultipleThreads(buffer, bytes, false);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public async Task TestSliceBytesInArrayMultipleThreads()
    {
        byte[] bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        Memory<byte> nioBuffer = Allocate(bytes.Length);
        bytes.CopyTo(nioBuffer.Span);
        ByteBuf buffer = Buffer(nioBuffer[..bytes.Length]);
        try
        {
            await TestBytesInArrayMultipleThreads(buffer, bytes, true);
        }
        finally
        {
            buffer.Release();
        }
    }

    // CLR: the original @Timeout(30) bound is enforced inside the shared AbstractByteBufTest helper.
    [Fact]
    public void TestToStringMultipleThreads1()
    {
        string expected = "Hello, World!";
        byte[] bytes = Encoding.Latin1.GetBytes(expected);
        Memory<byte> nioBuffer = Allocate(bytes.Length);
        bytes.CopyTo(nioBuffer.Span);
        ByteBuf buffer = Buffer(nioBuffer[..bytes.Length]);
        try
        {
            TestToStringMultipleThreads0(buffer);
        }
        finally
        {
            buffer.Release();
        }
    }

    // CLR: the original @Timeout(60) bound is enforced inside the shared AbstractByteBufTest helper.
    [Fact]
    public void TestCopyMultipleThreads()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            TestCopyMultipleThreads0(buffer);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadOnlyRelease()
    {
        ByteBuf buf = Buffer(Allocate(8));
        buf.WriterIndex = 4;
        TestRelease(buf, buf.AsReadOnly());
    }

    [Fact]
    public void TestDuplicateRelease()
    {
        ByteBuf buf = Buffer(Allocate(8));
        buf.WriterIndex = 4;
        TestRelease(buf, buf.Duplicate());
    }

    [Fact]
    public void TestSliceRelease()
    {
        ByteBuf buf = Buffer(Allocate(8));
        buf.WriterIndex = 4;
        TestRelease(buf, buf.Slice());
    }

    [Fact]
    public void TestDuplicateDuplicateRelease()
    {
        ByteBuf buf = Buffer(Allocate(8));
        buf.WriterIndex = 4;
        TestRelease(buf, buf.Duplicate().Duplicate());
    }

    [Fact]
    public void TestDuplicateSliceRelease()
    {
        ByteBuf buf = Buffer(Allocate(8));
        buf.WriterIndex = 4;
        TestRelease(buf, buf.Duplicate().Duplicate());
    }

    [Fact]
    public void TestSliceDuplicateRelease()
    {
        ByteBuf buf = Buffer(Allocate(8));
        buf.WriterIndex = 4;
        TestRelease(buf, buf.Slice().Duplicate());
    }

    [Fact]
    public void TestSliceSliceRelease()
    {
        ByteBuf buf = Buffer(Allocate(8));
        buf.WriterIndex = 4;
        TestRelease(buf, buf.Slice().Slice());
    }

    private static void TestRelease(ByteBuf parent, ByteBuf derived)
    {
        Assert.Equal(1, parent.RefCnt);
        Assert.True(derived.Release());
        Assert.Equal(0, parent.RefCnt);
    }

    [Fact]
    public void EnsureWritableWithForceAsReadyOnly()
    {
        EnsureWritableReadOnly(true);
    }

    [Fact]
    public void EnsureWritableWithOutForceAsReadOnly()
    {
        EnsureWritableReadOnly(false);
    }

    private void EnsureWritableReadOnly(bool force)
    {
        ByteBuf buffer = Buffer(Allocate(8));
        Assert.Equal(1, buffer.EnsureWritable(8, force));
        buffer.Release();
    }

    [Fact]
    public void TestReadyOnlyNioBuffer()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBuffer(buffer);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlySliceNioBuffer()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBuffer(buffer.Slice());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlyDuplicateNioBuffer()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBuffer(buffer.Duplicate());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlyNioBufferWithPositionLength()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBufferWithPositionLength(buffer);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlySliceNioBufferWithPositionLength()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBufferWithPositionLength(buffer.Slice());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlyDuplicateNioBufferWithPositionLength()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBufferWithPositionLength(buffer.Duplicate());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlyNioBuffers()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBuffers(buffer);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlySliceNioBuffers()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBuffers(buffer.Slice());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlyDuplicateNioBuffers()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBuffers(buffer.Duplicate());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlyNioBuffersWithPositionLength()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBuffersWithPositionLength(buffer);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlySliceNioBuffersWithPositionLength()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBuffersWithPositionLength(buffer.Slice());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlyDuplicateNioBuffersWithPositionLength()
    {
        ByteBuf buffer = NewRandomReadOnlyBuffer();
        try
        {
            AssertReadyOnlyNioBuffersWithPositionLength(buffer.Duplicate());
        }
        finally
        {
            buffer.Release();
        }
    }

    private ByteBuf NewRandomReadOnlyBuffer()
    {
        byte[] bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        Memory<byte> nioBuffer = Allocate(bytes.Length);
        bytes.CopyTo(nioBuffer.Span);
        return Buffer(nioBuffer[..bytes.Length]);
    }

    // CLR: test-only MemoryManager over a mapped view; SafeBuffer.AcquirePointer exposes the view address
    // and must be paired with ReleasePointer
    // (https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.safebuffer.acquirepointer?view=net-10.0),
    // and PointerOffset locates the view start
    // (https://learn.microsoft.com/en-us/dotnet/api/system.io.memorymappedfiles.memorymappedviewaccessor.pointeroffset?view=net-10.0).
    private sealed unsafe class MappedMemory : MemoryManager<byte>
    {
        private readonly MemoryMappedViewAccessor _view;
        private readonly int _length;
        private byte* _base;

        internal MappedMemory(MemoryMappedViewAccessor view, int length)
        {
            _view = view;
            _length = length;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref _base);
        }

        private byte* Start => _base + _view.PointerOffset;

        public override Span<byte> GetSpan() => new(Start, _length);

        public override MemoryHandle Pin(int elementIndex = 0) => new(Start + elementIndex);

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (_base != null)
            {
                _view.SafeMemoryMappedViewHandle.ReleasePointer();
                _base = null;
            }
        }
    }
}

public abstract partial class AbstractByteBufTest
{
    // CLR: ReadOnlyDirectByteBufferBufTest statically imports these AbstractByteBufTest helpers
    // (assertReadyOnlyNioBuffer*, testBytesInArrayMultipleThreads, testCopyMultipleThreads0,
    // testReadGatheringByteChannelMultipleThreads, testReadOutputStreamMultipleThreads and
    // testToStringMultipleThreads0). The single CLR bodies stay in AbstractByteBufTest; this nested type
    // gives them the original static-import names without duplicating them or clashing with test names.
    internal static class StaticImports
    {
        internal static void AssertReadyOnlyNioBuffer(ByteBuf buffer)
            => AbstractByteBufTest.AssertReadyOnlyNioBuffer(buffer);

        internal static void AssertReadyOnlyNioBufferWithPositionLength(ByteBuf buffer)
            => AbstractByteBufTest.AssertReadyOnlyNioBufferWithPositionLength(buffer);

        internal static void AssertReadyOnlyNioBuffers(ByteBuf buffer)
            => AbstractByteBufTest.AssertReadyOnlyNioBuffers(buffer);

        internal static void AssertReadyOnlyNioBuffersWithPositionLength(ByteBuf buffer)
            => AbstractByteBufTest.AssertReadyOnlyNioBuffersWithPositionLength(buffer);

        internal static Task TestBytesInArrayMultipleThreads(ByteBuf buffer, byte[] expectedBytes, bool slice)
            => AssertBytesInArrayMultipleThreads(buffer, expectedBytes, slice);

        internal static void TestCopyMultipleThreads0(ByteBuf buffer)
            => AssertCopyMultipleThreads(buffer);

        internal static void TestReadGatheringByteChannelMultipleThreads(ByteBuf buffer, byte[] expectedBytes, bool slice)
            => AssertReadGatheringByteChannelMultipleThreads(buffer, expectedBytes, slice);

        internal static void TestReadOutputStreamMultipleThreads(ByteBuf buffer, byte[] expectedBytes, bool slice)
            => AssertReadOutputStreamMultipleThreads(buffer, expectedBytes, slice);

        internal static void TestToStringMultipleThreads0(ByteBuf buffer)
            => AssertToStringMultipleThreads(buffer);
    }
}
