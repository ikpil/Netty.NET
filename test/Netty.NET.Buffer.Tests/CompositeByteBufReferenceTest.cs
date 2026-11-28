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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class CompositeByteBufReferenceTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly IByteBufAllocator Allocator;

        public Storage(bool pooled, bool direct)
        {
            Allocator = pooled
                ? new PooledByteBufAllocator(direct, pageSize: 4096, maxOrder: 1, nativeAllocator: Native)
                : new UnpooledByteBufAllocator(direct, Native);
        }

        public void Dispose()
        {
            (Allocator as IDisposable)?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Providers()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true })
            {
                yield return [pooled, direct];
            }
    }

    // CLR: heap/native pooled/unpooled factories; a final cleanup releases whatever a failed assertion left.
    private static void ReleaseAll(params ByteBuf[] buffers)
    {
        foreach (ByteBuf buffer in buffers)
            if (buffer?.RefCnt > 0)
            {
                buffer.Release(buffer.RefCnt);
            }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestReferenceCounts1(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        ByteBuf c1 = storage.Allocator.Buffer().WriteByte(1);
        ByteBuf c2 = storage.Allocator.Buffer().WriteByte(2).Retain();
        ByteBuf c3 = storage.Allocator.Buffer().WriteByte(3).Retain(2);

        CompositeByteBuf buf = storage.Allocator.CompositeBuffer();

        try
        {
            Assert.Equal(1, buf.RefCnt);
            buf.AddComponents(c1, c2, c3);

            Assert.Equal(1, buf.RefCnt);
            // CLR: AddComponents without increaseWriterIndex leaves the writer at zero.
            Assert.Equal(0, buf.WriterIndex);

            // Ensure that c[123]'s refCount did not change.
            Assert.Equal(1, c1.RefCnt);
            Assert.Equal(2, c2.RefCnt);
            Assert.Equal(3, c3.RefCnt);

            Assert.Equal(1, buf.Component(0).RefCnt);
            Assert.Equal(2, buf.Component(1).RefCnt);
            Assert.Equal(3, buf.Component(2).RefCnt);

            // CLR: the caller claims are released first, leaving only the transferred claims.
            Assert.False(c3.Release(2));
            Assert.False(c2.Release());
            Assert.All(new[] { c1, c2, c3 }, c => Assert.Equal(1, c.RefCnt));
            Assert.True(buf.Release());
            Assert.All(new[] { c1, c2, c3 }, c => Assert.Equal(0, c.RefCnt));
        }
        finally
        {
            ReleaseAll(buf, c1, c2, c3);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestReferenceCounts2(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        ByteBuf c1 = storage.Allocator.Buffer().WriteByte(1);
        ByteBuf c2 = storage.Allocator.Buffer().WriteByte(2).Retain();
        ByteBuf c3 = storage.Allocator.Buffer().WriteByte(3).Retain(2);

        CompositeByteBuf bufA = storage.Allocator.CompositeBuffer(), bufB = null;

        try
        {
            bufA.AddComponents(c1, c2, c3).WriterIndex = 3;

            bufB = storage.Allocator.CompositeBuffer();
            bufB.AddComponents(bufA);

            // Ensure that bufA.refCnt() did not change.
            Assert.Equal(1, bufA.RefCnt);

            // Ensure that c[123]'s refCnt did not change.
            Assert.Equal(1, c1.RefCnt);
            Assert.Equal(2, c2.RefCnt);
            Assert.Equal(3, c3.RefCnt);
            // CLR: the borrowed components report the same claims.
            Assert.Equal(1, bufA.Component(0).RefCnt);
            Assert.Equal(2, bufA.Component(1).RefCnt);
            Assert.Equal(3, bufA.Component(2).RefCnt);

            // This should decrease bufA.refCnt().
            Assert.True(bufB.Release());
            Assert.Equal(0, bufB.RefCnt);

            // Ensure bufA.refCnt() changed.
            Assert.Equal(0, bufA.RefCnt);

            // Ensure that c[123]'s refCnt also changed due to the deallocation of bufA.
            Assert.Equal(0, c1.RefCnt);
            Assert.Equal(1, c2.RefCnt);
            Assert.Equal(2, c3.RefCnt);

            Assert.True(c3.Release(2));
            Assert.True(c2.Release());
            // CLR: every claim is gone.
            Assert.All(new[] { c1, c2, c3 }, c => Assert.Equal(0, c.RefCnt));
        }
        finally
        {
            ReleaseAll(bufB, bufA, c1, c2, c3);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestReferenceCounts3(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        ByteBuf c1 = storage.Allocator.Buffer().WriteByte(1);
        ByteBuf c2 = storage.Allocator.Buffer().WriteByte(2).Retain();
        ByteBuf c3 = storage.Allocator.Buffer().WriteByte(3).Retain(2);

        CompositeByteBuf buf = storage.Allocator.CompositeBuffer();

        try
        {
            Assert.Equal(1, buf.RefCnt);

            // CLR: List<ByteBuf> through the IEnumerable overload replaces ArrayList/Iterable.
            var components = new List<ByteBuf> { c1, c2, c3 };
            buf.AddComponents(components);
            // CLR: the composite claim and writer are unchanged.
            Assert.Equal(1, buf.RefCnt);
            Assert.Equal(0, buf.WriterIndex);

            // Ensure that c[123]'s refCount did not change.
            Assert.Equal(1, c1.RefCnt);
            Assert.Equal(2, c2.RefCnt);
            Assert.Equal(3, c3.RefCnt);

            Assert.Equal(1, buf.Component(0).RefCnt);
            Assert.Equal(2, buf.Component(1).RefCnt);
            Assert.Equal(3, buf.Component(2).RefCnt);

            // CLR: the caller claims are released first, leaving only the transferred claims.
            Assert.False(c3.Release(2));
            Assert.False(c2.Release());
            Assert.All(new[] { c1, c2, c3 }, c => Assert.Equal(1, c.RefCnt));
            Assert.True(buf.Release());
            Assert.All(new[] { c1, c2, c3 }, c => Assert.Equal(0, c.RefCnt));
        }
        finally
        {
            ReleaseAll(buf, c1, c2, c3);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestComponentMustBeDuplicate(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        ByteBuf source = storage.Allocator.Buffer(4, 6).SetIndex(1, 3);
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer().AddComponent(source);
        ByteBuf view = null;

        try
        {
            view = composite.Component(0);
            // CLR verifies the duplicate contract without requiring Java's AbstractDerivedByteBuf class hierarchy.
            Assert.NotSame(source, view);
            Assert.Equal(4, view.Capacity);
            Assert.Equal(6, view.MaxCapacity);
            Assert.Equal(2, view.ReadableBytes);
            Assert.Equal(1, view.ReaderIndex);
            Assert.Equal(3, view.WriterIndex);
            view.ReaderIndex = 2;
            Assert.Equal(1, source.ReaderIndex);
            Assert.Equal(1, composite.Component(0).ReaderIndex);
            Assert.Equal(0, composite.ReaderIndex);
            Assert.Equal(1, source.RefCnt);
            view.Retain();
            Assert.Equal(2, source.RefCnt);
            Assert.False(view.Release());
        }
        finally
        {
            composite.Release();
        }

        Assert.Equal(0, source.RefCnt);
        Assert.Equal(0, view.RefCnt);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestAddEmptyBufferRelease(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf cbuf = storage.Allocator.CompositeBuffer();
        ByteBuf buf = storage.Allocator.Buffer();

        try
        {
            Assert.Equal(1, buf.RefCnt);
            cbuf.AddComponent(buf);
            Assert.Equal(1, buf.RefCnt);
            // CLR: the empty source still takes a component slot without contributing bytes.
            Assert.Equal(1, cbuf.NumComponents);
            Assert.Equal(0, cbuf.Capacity);
            Assert.Equal(0, cbuf.WriterIndex);

            cbuf.Release();
            Assert.Equal(0, buf.RefCnt);
        }
        finally
        {
            ReleaseAll(cbuf, buf);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestAddEmptyBuffersRelease(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf cbuf = storage.Allocator.CompositeBuffer();
        ByteBuf buf = storage.Allocator.Buffer();
        ByteBuf buf2 = storage.Allocator.Buffer().WriteInt(1);
        ByteBuf buf3 = storage.Allocator.Buffer();

        try
        {
            Assert.Equal(1, buf.RefCnt);
            Assert.Equal(1, buf2.RefCnt);
            Assert.Equal(1, buf3.RefCnt);

            cbuf.AddComponents(buf, buf2, buf3);
            Assert.Equal(1, buf.RefCnt);
            Assert.Equal(1, buf2.RefCnt);
            Assert.Equal(1, buf3.RefCnt);
            // CLR: empty sources keep their slots; only the four written bytes count.
            Assert.Equal(3, cbuf.NumComponents);
            Assert.Equal(4, cbuf.Capacity);
            Assert.Equal(0, cbuf.WriterIndex);
            Assert.Equal(1, cbuf.GetInt(0));

            cbuf.Release();
            Assert.Equal(0, buf.RefCnt);
            Assert.Equal(0, buf2.RefCnt);
            Assert.Equal(0, buf3.RefCnt);
        }
        finally
        {
            ReleaseAll(cbuf, buf, buf2, buf3);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestAddEmptyBufferInMiddle(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf cbuf = storage.Allocator.CompositeBuffer();
        ByteBuf buf1 = storage.Allocator.Buffer().WriteByte((byte)1);
        ByteBuf buf3 = null;

        try
        {
            cbuf.AddComponent(buf1, true);
            cbuf.AddComponent(Unpooled.EmptyBuffer, true);
            buf3 = storage.Allocator.Buffer().WriteByte((byte)2);
            cbuf.AddComponent(buf3, true);

            Assert.Equal(2, cbuf.ReadableBytes);
            Assert.Equal((byte)1, cbuf.ReadByte());
            Assert.Equal((byte)2, cbuf.ReadByte());

            Assert.Same(Unpooled.EmptyBuffer, cbuf.InternalComponent(1));
            Assert.NotSame(Unpooled.EmptyBuffer, cbuf.InternalComponentAtOffset(1));
            // CLR: the empty slot consumes no byte offset, as testInsertEmptyBufferInMiddle checks.
            Assert.Equal(2, cbuf.Capacity);
            Assert.Equal(3, cbuf.NumComponents);
            var dest = new byte[2];
            cbuf.GetBytes(0, dest);
            Assert.Equal(new byte[] { 1, 2 }, dest);
            cbuf.Release();
            // CLR: the sources are released while the immutable empty singleton stays live.
            Assert.Equal(0, buf1.RefCnt);
            Assert.Equal(0, buf3.RefCnt);
            Assert.Equal(1, Unpooled.EmptyBuffer.RefCnt);
        }
        finally
        {
            ReleaseAll(cbuf, buf1, buf3);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestInsertEmptyBufferInMiddle(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf cbuf = storage.Allocator.CompositeBuffer();
        ByteBuf buf1 = storage.Allocator.Buffer().WriteByte((byte)1);
        ByteBuf buf2 = null;

        try
        {
            cbuf.AddComponent(buf1, true);
            buf2 = storage.Allocator.Buffer().WriteByte((byte)2);
            cbuf.AddComponent(buf2, true);

            // insert empty one between the first two
            cbuf.AddComponent(1, Unpooled.EmptyBuffer, true);

            Assert.Equal(2, cbuf.ReadableBytes);
            Assert.Equal((byte)1, cbuf.ReadByte());
            Assert.Equal((byte)2, cbuf.ReadByte());

            Assert.Equal(2, cbuf.Capacity);
            Assert.Equal(3, cbuf.NumComponents);

            byte[] dest = new byte[2];
            // should skip over the empty one, not throw a java.lang.Error :)
            cbuf.GetBytes(0, dest);

            Assert.Equal(new byte[] { 1, 2 }, dest);
            // CLR: the inserted slot keeps the empty singleton, as testAddEmptyBufferInMiddle checks.
            Assert.Same(Unpooled.EmptyBuffer, cbuf.InternalComponent(1));
            Assert.NotSame(Unpooled.EmptyBuffer, cbuf.InternalComponentAtOffset(1));

            cbuf.Release();
            // CLR: the sources are released while the immutable empty singleton stays live.
            Assert.Equal(0, buf1.RefCnt);
            Assert.Equal(0, buf2.RefCnt);
            Assert.Equal(1, Unpooled.EmptyBuffer.RefCnt);
        }
        finally
        {
            ReleaseAll(cbuf, buf1, buf2);
        }
    }
}
