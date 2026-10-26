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
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufLifetimeFixtureTest
{
    private sealed class Scope : IDisposable
    {
        private readonly int _provider;
        private readonly NativeMemoryAllocator _native = new();
        private readonly IByteBufAllocator Allocator;
        public Scope(int provider)
        {
            _provider = provider;
            Allocator = (provider / 2) switch
            {
                1 => new PooledByteBufAllocator(false, maxOrder: 4, smallCacheSize: 0, nativeAllocator: _native),
                2 => new AdaptiveByteBufAllocator(false, false, _native),
                _ => new UnpooledByteBufAllocator(false, true, _native)
            };
        }
        public ByteBuf Buffer(int capacity)
        {
            bool direct = (_provider & 1) != 0;
            ByteBuf Part(int size) => direct ? Allocator.DirectBuffer(size) : Allocator.HeapBuffer(size);
            if (_provider < 6)
                return Part(capacity);
            CompositeByteBuf result = direct ? Allocator.CompositeDirectBuffer() : Allocator.CompositeHeapBuffer();
            try
            {
                int first = (capacity + 1) / 2;
                result.AddComponent(Part(first).WriteZero(first));
                result.AddComponent(Part(capacity - first).WriteZero(capacity - first));
                return result;
            }
            catch { result.Release(); throw; }
        }
        public void Dispose() { (Allocator as IDisposable)?.Dispose(); Assert.Equal(0, _native.ReservedBytes); }
    }
    private static ByteBuf Entry(ByteBuf root, int form) => form switch
    {
        1 => new ByteBufUtilitySourceTest.Wrapper(root),
        2 => root.Duplicate(),
        _ => root
    };
    public static IEnumerable<object[]> StorageCases()
    {
        for (int provider = 0; provider < 8; ++provider)
            for (int form = 0; form < 3; ++form)
                yield return [provider, form];
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestGetBytesAfterRelease(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).GetBytes(0, buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestGetBytesAfterRelease2(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).GetBytes(0, buffer, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestGetBytesAfterRelease3(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).GetBytes(0, buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestGetBytesAfterRelease6(int provider, int form)
    {
        using var scope = new Scope(provider);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).GetBytes(0, new byte[8].AsSpan());
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestGetBytesAfterRelease7(int provider, int form)
    {
        using var scope = new Scope(provider);
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).GetBytes(0, stream, 1);
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestGetBytesAfterRelease8(int provider, int form)
    {
        using var scope = new Scope(provider);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).GetBytes(0, socket, 1);
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestSetBytesAfterRelease(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).SetBytes(0, buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestSetBytesAfterRelease2(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).SetBytes(0, buffer, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestSetBytesAfterRelease3(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).SetBytes(0, buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestSetBytesAfterRelease6(int provider, int form)
    {
        using var scope = new Scope(provider);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).SetBytes(0, new byte[8].AsSpan());
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestSetBytesAfterRelease7(int provider, int form)
    {
        using var scope = new Scope(provider);
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).SetBytes(0, stream, 1);
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestSetBytesAfterRelease8(int provider, int form)
    {
        using var scope = new Scope(provider);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).SetBytes(0, socket, 1);
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadBytesAfterRelease(int provider, int form)
    {
        using var scope = new Scope(provider);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).ReadBytes(1);
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadBytesAfterRelease2(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).ReadBytes(buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadBytesAfterRelease3(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).ReadBytes(buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadBytesAfterRelease4(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).ReadBytes(buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadBytesAfterRelease7(int provider, int form)
    {
        using var scope = new Scope(provider);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).ReadBytes(new byte[8].AsSpan());
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadBytesAfterRelease8(int provider, int form)
    {
        using var scope = new Scope(provider);
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).ReadBytes(stream, 1);
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadBytesAfterRelease9(int provider, int form)
    {
        using var scope = new Scope(provider);
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).ReadBytes(stream, 1);
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadBytesAfterRelease10(int provider, int form)
    {
        using var scope = new Scope(provider);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).ReadBytes(socket, 1);
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestWriteBytesAfterRelease(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).WriteBytes(buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestWriteBytesAfterRelease2(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.CopiedBuffer(new byte[8]);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).WriteBytes(buffer, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestWriteBytesAfterRelease3(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer(scope, form).WriteBytes(buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestWriteBytesAfterRelease6(int provider, int form)
    {
        using var scope = new Scope(provider);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).WriteBytes(new byte[8].AsSpan());
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestWriteBytesAfterRelease7(int provider, int form)
    {
        using var scope = new Scope(provider);
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).WriteBytes(stream, 1);
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestWriteBytesAfterRelease8(int provider, int form)
    {
        using var scope = new Scope(provider);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer(scope, form).WriteBytes(socket, 1);
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestNioBufferAfterRelease(int provider, int form)
    {
        using var scope = new Scope(provider);
        // CLR bounded borrows replace JVM NIO, array and permanent-address exposure.
        ByteBuf buffer = ReleasedBuffer(scope, form);
        Assert.Throws<IllegalReferenceCountException>(() => buffer.AsReadOnlyMemory(buffer.ReaderIndex, buffer.ReadableBytes));
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestNioBufferAfterRelease1(int provider, int form)
    {
        using var scope = new Scope(provider);
        // CLR bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedBuffer(scope, form).AsReadOnlyMemory(0, 1));
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestInternalNioBufferAfterRelease(int provider, int form)
    {
        using var scope = new Scope(provider);
        // CLR bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedBuffer(scope, form).AsMemory(0, 1));
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestNioBuffersAfterRelease(int provider, int form)
    {
        using var scope = new Scope(provider);
        // CLR bounded borrows replace JVM NIO, array and permanent-address exposure.
        ByteBuf buffer = ReleasedBuffer(scope, form);
        Assert.Throws<IllegalReferenceCountException>(() => buffer.AsReadOnlySequence(buffer.ReaderIndex, buffer.ReadableBytes));
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestNioBuffersAfterRelease2(int provider, int form)
    {
        using var scope = new Scope(provider);
        // CLR bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedBuffer(scope, form).AsReadOnlySequence(0, 1));
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestArrayAfterRelease(int provider, int form)
    {
        using var scope = new Scope(provider);
        // CLR bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedBuffer(scope, form).AsMemory(0, 0));
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestMemoryAddressAfterRelease(int provider, int form)
    {
        using var scope = new Scope(provider);
        // CLR bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            using var pin = ReleasedBuffer(scope, form).AsReadOnlyMemory(0, 0).Pin();
        });
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestSliceAfterReleaseRetainedSlice(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        AssertSliceFailAfterRelease(buf, buf2);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestSliceAfterReleaseRetainedSliceDuplicate(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.Duplicate();
        AssertSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestSliceAfterReleaseRetainedSliceRetainedDuplicate(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.RetainedDuplicate();
        AssertSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestSliceAfterReleaseRetainedDuplicate(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertSliceFailAfterRelease(buf, buf2);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestSliceAfterReleaseRetainedDuplicateSlice(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        ByteBuf buf3 = buf2.Slice(0, 1);
        AssertSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedSliceAfterReleaseRetainedSlice(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        AssertRetainedSliceFailAfterRelease(buf, buf2);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedSliceAfterReleaseRetainedSliceDuplicate(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.Duplicate();
        AssertRetainedSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedSliceAfterReleaseRetainedSliceRetainedDuplicate(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.RetainedDuplicate();
        AssertRetainedSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedSliceAfterReleaseRetainedDuplicate(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertRetainedSliceFailAfterRelease(buf, buf2);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedSliceAfterReleaseRetainedDuplicateSlice(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        ByteBuf buf3 = buf2.Slice(0, 1);
        AssertRetainedSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestDuplicateAfterReleaseRetainedSliceDuplicate(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.Duplicate();
        AssertDuplicateFailAfterRelease(buf, buf2, buf3);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestDuplicateAfterReleaseRetainedDuplicate(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertDuplicateFailAfterRelease(buf, buf2);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestDuplicateAfterReleaseRetainedDuplicateSlice(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        ByteBuf buf3 = buf2.Slice(0, 1);
        AssertDuplicateFailAfterRelease(buf, buf2, buf3);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedDuplicateAfterReleaseRetainedDuplicate(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertRetainedDuplicateFailAfterRelease(buf, buf2);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedDuplicateAfterReleaseDuplicate(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.Duplicate();
        AssertRetainedDuplicateFailAfterRelease(buf, buf2);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedDuplicateAfterReleaseRetainedSlice(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        AssertRetainedDuplicateFailAfterRelease(buf, buf2);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedSliceAndRetainedDuplicateContentIsExpected(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
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

        Assert.Equal(slice2.ReferenceCount, dup2.ReferenceCount);
        Assert.Equal(dup2.ReferenceCount, dup1.ReferenceCount);

        // The handler is now done with the original slice
        Assert.True(slice1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.ReferenceCount);
        Assert.Equal(0, slice1.ReferenceCount);
        Assert.Equal(0, slice2.ReferenceCount);
        Assert.Equal(0, dup1.ReferenceCount);
        Assert.Equal(0, dup2.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedDuplicateAndRetainedSliceContentIsExpected(int provider, int form)
    {
        using var scope = new Scope(provider);
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
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

        Assert.Equal(slice2.ReferenceCount, dup2.ReferenceCount);
        Assert.Equal(dup2.ReferenceCount, slice1.ReferenceCount);

        // The handler is now done with the original slice
        Assert.True(dup1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.ReferenceCount);
        Assert.Equal(0, slice1.ReferenceCount);
        Assert.Equal(0, slice2.ReferenceCount);
        Assert.Equal(0, dup1.ReferenceCount);
        Assert.Equal(0, dup2.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestMultipleLevelRetainedSlice1(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestMultipleLevelRetainedSliceWithNonRetained(scope, form, true, true);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestMultipleLevelRetainedSlice2(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestMultipleLevelRetainedSliceWithNonRetained(scope, form, true, false);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestMultipleLevelRetainedSlice3(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestMultipleLevelRetainedSliceWithNonRetained(scope, form, false, true);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestMultipleLevelRetainedSlice4(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestMultipleLevelRetainedSliceWithNonRetained(scope, form, false, false);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedSliceUnreleasable1(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestRetainedSliceUnreleasable(scope, form, true, true);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedSliceUnreleasable2(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestRetainedSliceUnreleasable(scope, form, true, false);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedSliceUnreleasable3(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestRetainedSliceUnreleasable(scope, form, false, true);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedSliceUnreleasable4(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestRetainedSliceUnreleasable(scope, form, false, false);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadRetainedSliceUnreleasable1(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestReadRetainedSliceUnreleasable(scope, form, true, true);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadRetainedSliceUnreleasable2(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestReadRetainedSliceUnreleasable(scope, form, true, false);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadRetainedSliceUnreleasable3(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestReadRetainedSliceUnreleasable(scope, form, false, true);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestReadRetainedSliceUnreleasable4(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestReadRetainedSliceUnreleasable(scope, form, false, false);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedDuplicateUnreleasable1(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestRetainedDuplicateUnreleasable(scope, form, true, true);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedDuplicateUnreleasable2(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestRetainedDuplicateUnreleasable(scope, form, true, false);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedDuplicateUnreleasable3(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestRetainedDuplicateUnreleasable(scope, form, false, true);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRetainedDuplicateUnreleasable4(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestRetainedDuplicateUnreleasable(scope, form, false, false);
    }

    // Test-case trying to reproduce:
    // https://github.com/netty/netty/issues/2843
    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRefCnt(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestRefCnt0(scope, form, false);
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public void TestRefCnt2(int provider, int form)
    {
        using var scope = new Scope(provider);
        TestRefCnt0(scope, form, true);
    }

    private static void AssertSliceFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.ReferenceCount > 0)
            {
                buf.Release();
            }
        }
        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.ReferenceCount);
                buf.Slice();
            });
        }
    }

    private static void AssertRetainedSliceFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.ReferenceCount > 0)
            {
                buf.Release();
            }
        }
        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.ReferenceCount);
                buf.RetainedSlice();
            });
        }
    }

    private static void AssertDuplicateFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.ReferenceCount > 0)
            {
                buf.Release();
            }
        }
        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.ReferenceCount);
                buf.Duplicate();
            });
        }
    }

    private static void AssertRetainedDuplicateFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.ReferenceCount > 0)
            {
                buf.Release();
            }
        }
        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.ReferenceCount);
                buf.RetainedDuplicate();
            });
        }
    }

    private static void TestRetainedSliceUnreleasable(Scope scope, int form, bool initRetainedSlice, bool finalRetainedSlice)
    {
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(8);
        ByteBuf buf1 = initRetainedSlice ? buf.RetainedSlice() : buf.Slice().Retain();
        ByteBuf buf2 = Unpooled.UnreleasableBuffer(buf1);
        ByteBuf buf3 = finalRetainedSlice ? buf2.RetainedSlice() : buf2.Slice().Retain();
        Assert.False(buf3.Release());
        Assert.False(buf2.Release());
        buf1.Release();
        Assert.True(buf.Release());
        Assert.Equal(0, buf1.ReferenceCount);
        Assert.Equal(0, buf.ReferenceCount);
    }

    private static void TestReadRetainedSliceUnreleasable(Scope scope, int form, bool initRetainedSlice, bool finalRetainedSlice)
    {
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(8);
        ByteBuf buf1 = initRetainedSlice ? buf.RetainedSlice() : buf.Slice().Retain();
        ByteBuf buf2 = Unpooled.UnreleasableBuffer(buf1);
        ByteBuf buf3 = finalRetainedSlice ? buf2.ReadRetainedSlice(buf2.ReadableBytes)
                                          : buf2.ReadSlice(buf2.ReadableBytes).Retain();
        Assert.False(buf3.Release());
        Assert.False(buf2.Release());
        buf1.Release();
        Assert.True(buf.Release());
        Assert.Equal(0, buf1.ReferenceCount);
        Assert.Equal(0, buf.ReferenceCount);
    }

    private static void TestRetainedDuplicateUnreleasable(Scope scope, int form, bool initRetainedDuplicate, bool finalRetainedDuplicate)
    {
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
        ByteBuf buf = NewBuffer(8);
        ByteBuf buf1 = initRetainedDuplicate ? buf.RetainedDuplicate() : buf.Duplicate().Retain();
        ByteBuf buf2 = Unpooled.UnreleasableBuffer(buf1);
        ByteBuf buf3 = finalRetainedDuplicate ? buf2.RetainedDuplicate() : buf2.Duplicate().Retain();
        Assert.False(buf3.Release());
        Assert.False(buf2.Release());
        buf1.Release();
        Assert.True(buf.Release());
        Assert.Equal(0, buf1.ReferenceCount);
        Assert.Equal(0, buf.ReferenceCount);
    }

    private static void TestMultipleLevelRetainedSliceWithNonRetained(Scope scope, int form, bool doSlice1, bool doSlice2)
    {
        ByteBuf NewBuffer(int capacity) => Entry(scope.Buffer(capacity), form);
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
        Assert.Equal(slice3.ReferenceCount, slice2.ReferenceCount);
        Assert.Equal(slice3.ReferenceCount, slice4.ReferenceCount);

        // Slice 1 should also release the original underlying buffer without throwing exceptions
        Assert.True(slice1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.ReferenceCount);
        Assert.Equal(0, slice1.ReferenceCount);
        Assert.Equal(0, slice2.ReferenceCount);
        Assert.Equal(0, slice3.ReferenceCount);
    }

    private static ByteBuf ReleasedBuffer(Scope scope, int form)
    {
        ByteBuf buffer = Entry(scope.Buffer(8), form);
        // Clear the buffer so we are sure the reader and writer indices are 0.
        // This is important as we may return a slice from newBuffer(...).
        buffer.Clear();
        Assert.True(buffer.Release());
        return buffer;
    }

    private static void TestRefCnt0(Scope scope, int form, bool parameter)
    {
        for (int i = 0; i < 10; ++i)
        {
            using var latch = new ManualResetEventSlim();
            using var innerLatch = new ManualResetEventSlim();
            ByteBuf buffer = Entry(scope.Buffer(4), form);
            Assert.Equal(1, buffer.ReferenceCount);
            Exception innerThrowable = null;
            int count = int.MaxValue;
            Thread reader = null;
            var releaser = new Thread(() =>
            {
                try
                {
                    Assert.True(parameter ? buffer.Release(buffer.ReferenceCount) : buffer.Release());
                    reader = new Thread(() =>
                    {
                        count = buffer.ReferenceCount;
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
                if (buffer.ReferenceCount > 0)
                    buffer.Release();
            }
        }
    }
}
