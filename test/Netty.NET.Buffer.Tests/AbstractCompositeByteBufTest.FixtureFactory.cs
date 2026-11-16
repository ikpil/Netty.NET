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
using System.Linq;

namespace Netty.NET.Buffer.Tests;

// CLR: the declared AbstractCompositeByteBufTest bodies that use the fixture factories newBuffer() and
// newCompositeBuffer(), so that every concrete composite fixture (BigEndian, Wrapped, SimpleLeakAware and
// AdvancedLeakAware composites) runs them on its own buffer type as in the source. The provider-matrix
// versions remain as CLR supplements in the Composite*Test classes.
public abstract partial class AbstractCompositeByteBufTest
{
    [Fact]
    public void TestIsContiguous()
    {
        ByteBuf buf = NewBuffer(4);
        Assert.False(buf.IsContiguous);
        buf.Release();
    }

    [Fact]
    public void TestAddFlattenedComponents()
    {
        TestAddFlattenedComponents0(false);
    }

    [Fact]
    public void TestAddFlattenedComponentsWithWrappedComposite()
    {
        TestAddFlattenedComponents0(true);
    }

    // CLR: xUnit1024 forbids overloading the TestAddFlattenedComponents test name.
    private void TestAddFlattenedComponents0(bool addWrapped)
    {
        ByteBuf b1 = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        CompositeByteBuf newComposite = NewCompositeBuffer()
                .AddComponent(b1, true)
                .AddFlattenedComponents(b1.Retain(), true)
                .AddFlattenedComponents(Unpooled.EmptyBuffer, true);

        Assert.Equal(2, newComposite.NumComponents);
        Assert.Equal(6, newComposite.Capacity);
        Assert.Equal(6, newComposite.WriterIndex);

        // It is important to use a pooled allocator here to ensure
        // the slices returned by readRetainedSlice are of type
        // PooledSlicedByteBuf, which maintains an independent refcount
        // (so that we can be sure to cover this case)
        ByteBuf buffer = PooledByteBufAllocator.Default.Buffer()
              .WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        // use mixture of slice and retained slice
        ByteBuf s1 = buffer.ReadRetainedSlice(2);
        ByteBuf s2 = s1.RetainedSlice(0, 2);
        ByteBuf s3 = buffer.Slice(0, 2).Retain();
        ByteBuf s4 = s2.RetainedSlice(0, 2);
        buffer.Release();

        CompositeByteBuf compositeToAdd = Unpooled.CompositeBuffer()
            .AddComponent(s1)
            .AddComponent(Unpooled.EmptyBuffer)
            .AddComponents(s2, s3, s4);
        // set readable range to be from middle of first component
        // to middle of penultimate component
        compositeToAdd.SetIndex(1, 5);

        Assert.Equal(1, compositeToAdd.RefCnt);
        Assert.Equal(1, s4.RefCnt);

        ByteBuf compositeCopy = compositeToAdd.Copy();

        if (addWrapped)
        {
            compositeToAdd = new WrappedCompositeByteBuf(compositeToAdd);
        }
        newComposite.AddFlattenedComponents(compositeToAdd, true);

        // verify that added range matches
        ByteBufUtil.Equals(compositeCopy, 0,
                newComposite, 6, compositeCopy.ReadableBytes);

        // should not include empty component or last component
        // (latter outside of the readable range)
        Assert.Equal(5, newComposite.NumComponents);
        Assert.Equal(10, newComposite.Capacity);
        Assert.Equal(10, newComposite.WriterIndex);

        Assert.Equal(0, compositeToAdd.RefCnt);
        // s4 wasn't in added range so should have been jettisoned
        Assert.Equal(0, s4.RefCnt);
        Assert.Equal(1, newComposite.RefCnt);

        // releasing composite should release the remaining components
        newComposite.Release();
        Assert.Equal(0, newComposite.RefCnt);
        Assert.Equal(0, s1.RefCnt);
        Assert.Equal(0, s2.RefCnt);
        Assert.Equal(0, s3.RefCnt);
        Assert.Equal(0, b1.RefCnt);
    }

    [Fact]
    public void TestIterator()
    {
        CompositeByteBuf cbuf = NewCompositeBuffer();
        cbuf.AddComponent(Unpooled.EmptyBuffer);
        cbuf.AddComponent(Unpooled.EmptyBuffer);

        // CLR: IEnumerator.MoveNext/Current replace Iterator.hasNext/next
        // (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.ienumerator-1?view=net-10.0).
        using IEnumerator<ByteBuf> it = cbuf.GetEnumerator();
        Assert.True(it.MoveNext());
        Assert.Same(Unpooled.EmptyBuffer, it.Current);
        Assert.True(it.MoveNext());
        Assert.Same(Unpooled.EmptyBuffer, it.Current);
        Assert.False(it.MoveNext());

        // CLR: an exhausted enumerator's Current throws InvalidOperationException instead of next() throwing
        // NoSuchElementException.
        Assert.Throws<InvalidOperationException>(() => it.Current);
        cbuf.Release();
    }

    [Fact]
    public void TestEmptyIterator()
    {
        CompositeByteBuf cbuf = NewCompositeBuffer();

        using IEnumerator<ByteBuf> it = cbuf.GetEnumerator();
        Assert.False(it.MoveNext());

        // CLR: an exhausted enumerator's Current throws InvalidOperationException instead of next() throwing
        // NoSuchElementException.
        Assert.Throws<InvalidOperationException>(() => it.Current);
        cbuf.Release();
    }

    [Fact]
    public void TestIteratorConcurrentModificationAdd()
    {
        CompositeByteBuf cbuf = NewCompositeBuffer();
        cbuf.AddComponent(Unpooled.EmptyBuffer);

        using IEnumerator<ByteBuf> it = cbuf.GetEnumerator();
        cbuf.AddComponent(Unpooled.EmptyBuffer);

        try
        {
            // CLR: MoveNext combines hasNext/next and detects the component-count change;
            // InvalidOperationException replaces ConcurrentModificationException, as for BCL collection
            // enumerators (https://learn.microsoft.com/en-us/dotnet/api/system.collections.ienumerator.movenext?view=net-10.0).
            Assert.Throws<InvalidOperationException>(() =>
            {
                it.MoveNext();
            });
        }
        finally
        {
            cbuf.Release();
        }
    }

    [Fact]
    public void TestIteratorConcurrentModificationRemove()
    {
        CompositeByteBuf cbuf = NewCompositeBuffer();
        cbuf.AddComponent(Unpooled.EmptyBuffer);

        using IEnumerator<ByteBuf> it = cbuf.GetEnumerator();
        cbuf.RemoveComponent(0);

        try
        {
            // CLR: MoveNext combines hasNext/next and detects the component-count change;
            // InvalidOperationException replaces ConcurrentModificationException.
            Assert.Throws<InvalidOperationException>(() =>
            {
                it.MoveNext();
            });
        }
        finally
        {
            cbuf.Release();
        }
    }

    [Fact]
    public void TestReleasesItsComponents2()
    {
        // It is important to use a pooled allocator here to ensure
        // the slices returned by readRetainedSlice are of type
        // PooledSlicedByteBuf, which maintains an independent refcount
        // (so that we can be sure to cover this case)
        ByteBuf buffer = PooledByteBufAllocator.Default.Buffer(); // 1

        buffer.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        // use readRetainedSlice this time - produces different kind of slices
        ByteBuf s1 = buffer.ReadRetainedSlice(2); // 2
        ByteBuf s2 = s1.ReadRetainedSlice(2); // 3
        ByteBuf s3 = s2.ReadRetainedSlice(2); // 4
        ByteBuf s4 = s3.ReadRetainedSlice(2); // 5

        // CLR: the deprecated .order(ByteOrder.LITTLE_ENDIAN) facade is omitted; it only swaps the
        // multi-byte accessors and does not affect component ownership.
        ByteBuf composite = NewCompositeBuffer()
            .AddComponent(s1)
            .AddComponents(s2, s3, s4);

        Assert.Equal(1, composite.RefCnt);
        Assert.Equal(2, buffer.RefCnt);

        // releasing composite should release the 4 components
        composite.Release();
        Assert.Equal(0, composite.RefCnt);
        Assert.Equal(1, buffer.RefCnt);

        // last remaining ref to buffer
        buffer.Release();
        Assert.Equal(0, buffer.RefCnt);
    }

    [Fact]
    public void TestReleasesOnShrink()
    {

        ByteBuf b1 = Unpooled.Buffer(2).WriteShort(1);
        ByteBuf b2 = Unpooled.Buffer(2).WriteShort(2);

        // composite takes ownership of s1 and s2
        ByteBuf composite = NewCompositeBuffer()
            .AddComponents(b1, b2);

        Assert.Equal(4, composite.Capacity);

        // reduce capacity down to two, will drop the second component
        composite.Capacity = 2;
        Assert.Equal(2, composite.Capacity);

        // releasing composite should release the components
        composite.Release();
        Assert.Equal(0, composite.RefCnt);
        Assert.Equal(0, b1.RefCnt);
        Assert.Equal(0, b2.RefCnt);
    }

    [Fact]
    public void TestReleasesOnShrink2()
    {
        // It is important to use a pooled allocator here to ensure
        // the slices returned by readRetainedSlice are of type
        // PooledSlicedByteBuf, which maintains an independent refcount
        // (so that we can be sure to cover this case)
        ByteBuf buffer = PooledByteBufAllocator.Default.Buffer();

        buffer.WriteShort(1).WriteShort(2);

        ByteBuf b1 = buffer.ReadRetainedSlice(2);
        ByteBuf b2 = b1.RetainedSlice(b1.ReaderIndex, 2);

        // composite takes ownership of b1 and b2
        ByteBuf composite = NewCompositeBuffer()
            .AddComponents(b1, b2);

        Assert.Equal(4, composite.Capacity);

        // reduce capacity down to two, will drop the second component
        composite.Capacity = 2;
        Assert.Equal(2, composite.Capacity);

        // releasing composite should release the components
        composite.Release();
        Assert.Equal(0, composite.RefCnt);
        Assert.Equal(0, b1.RefCnt);
        Assert.Equal(0, b2.RefCnt);

        // release last remaining ref to buffer
        buffer.Release();
        Assert.Equal(0, buffer.RefCnt);
    }

    [Fact]
    public void TestDecomposeMultiple()
    {
        TestDecompose(150, 500, 3);
    }

    [Fact]
    public void TestDecomposeOne()
    {
        TestDecompose(310, 50, 1);
    }

    [Fact]
    public void TestDecomposeNone()
    {
        TestDecompose(310, 0, 0);
    }

    private void TestDecompose(int offset, int length, int expectedListSize)
    {
        byte[] bytes = new byte[1024];
        // CLR: Random.Shared replaces ThreadLocalRandom.current()
        // (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0).
        Random.Shared.NextBytes(bytes);
        ByteBuf buf = Unpooled.WrappedBuffer(bytes);

        CompositeByteBuf composite = NewCompositeBuffer();
        composite.AddComponents(new[]
                                {
                                    buf.RetainedSlice(100, 200),
                                    buf.RetainedSlice(300, 400),
                                    buf.RetainedSlice(700, 100)
                                }, true);

        ByteBuf slice = composite.Slice(offset, length);
        IReadOnlyList<ByteBuf> bufferList = composite.Decompose(offset, length);
        Assert.Equal(expectedListSize, bufferList.Count);
        ByteBuf wrapped = Unpooled.WrappedBuffer(bufferList.ToArray());

        Assert.Equal(slice, wrapped);
        composite.Release();
        buf.Release();

        foreach (ByteBuf buffer in bufferList)
        {
            Assert.Equal(0, buffer.RefCnt);
        }
    }

    [Fact]
    public void TestDecomposeReturnNonUnwrappedBuffer()
    {
        ByteBuf buf = PooledByteBufAllocator.Default.Buffer(1024);
        buf.WriteZero(1024);
        ByteBuf sliced = buf.RetainedSlice(100, 200);
        sliced.Retain();
        Assert.Equal(2, sliced.RefCnt);
        CompositeByteBuf composite = NewCompositeBuffer();
        composite.AddComponents(new[] { sliced }, true);

        IReadOnlyList<ByteBuf> bufferList = composite.Decompose(0, 100);
        // CLR: analyzer rule xUnit2013 requires Assert.Single for assertEquals(1, bufferList.size())
        // (https://xunit.net/xunit.analyzers/rules/xUnit2013).
        Assert.Single(bufferList);
        ByteBuf decomposed = bufferList[0];
        // CLR: assertSame on two boxed small Integer counts compares the cached values; Assert.Equal
        // compares the counts directly.
        Assert.Equal(sliced.RefCnt, decomposed.RefCnt);
        decomposed.Release();

        Assert.Equal(sliced.RefCnt, decomposed.RefCnt);

        composite.Release();
        buf.Release();

        foreach (ByteBuf buffer in bufferList)
        {
            Assert.Equal(0, buffer.RefCnt);
        }
    }

    [Fact]
    public void TestDiscardSomeReadBytesCorrectlyUpdatesLastAccessed()
    {
        TestDiscardCorrectlyUpdatesLastAccessed(true);
    }

    [Fact]
    public void TestDiscardReadBytesCorrectlyUpdatesLastAccessed()
    {
        TestDiscardCorrectlyUpdatesLastAccessed(false);
    }

    private void TestDiscardCorrectlyUpdatesLastAccessed(bool discardSome)
    {
        CompositeByteBuf cbuf = NewCompositeBuffer();
        List<ByteBuf> buffers = new List<ByteBuf>(4);
        for (int i = 0; i < 4; i++)
        {
            ByteBuf buf = Unpooled.Buffer().WriteInt(i);
            cbuf.AddComponent(buf, true);
            buffers.Add(buf);
        }

        // Skip the first 2 bytes which means even if we call discard*ReadBytes() later we can no drop the first
        // component as it is still used.
        cbuf.SkipBytes(2);
        if (discardSome)
        {
            cbuf.DiscardSomeReadBytes();
        }
        else
        {
            cbuf.DiscardReadBytes();
        }
        Assert.Equal(4, cbuf.NumComponents);

        // Now skip 3 bytes which means we should be able to drop the first component on the next discard*ReadBytes()
        // call.
        cbuf.SkipBytes(3);

        if (discardSome)
        {
            cbuf.DiscardSomeReadBytes();
        }
        else
        {
            cbuf.DiscardReadBytes();
        }
        Assert.Equal(3, cbuf.NumComponents);
        // Now skip again 3 bytes which should bring our readerIndex == start of the 3 component.
        cbuf.SkipBytes(3);

        // Read one int (4 bytes) which should bring our readerIndex == start of the 4 component.
        Assert.Equal(2, cbuf.ReadInt());
        if (discardSome)
        {
            cbuf.DiscardSomeReadBytes();
        }
        else
        {
            cbuf.DiscardReadBytes();
        }

        // Now all except the last component should have been dropped / released.
        Assert.Equal(1, cbuf.NumComponents);
        Assert.Equal(3, cbuf.ReadInt());
        if (discardSome)
        {
            cbuf.DiscardSomeReadBytes();
        }
        else
        {
            cbuf.DiscardReadBytes();
        }
        Assert.Equal(0, cbuf.NumComponents);

        // These should have been released already.
        foreach (ByteBuf buffer in buffers)
        {
            Assert.Equal(0, buffer.RefCnt);
        }
        Assert.True(cbuf.Release());
    }

    [Fact]
    public void ForEachByteOnNestedCompositeByteBufMustSeeEntireFlattenedContents()
    {
        CompositeByteBuf buf = NewCompositeBuffer();
        buf.AddComponent(NewCompositeBuffer().AddComponents(new[]
                {
                    Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                    Unpooled.WrappedBuffer(new byte[] { 4, 5, 6 })
                }, true), true);
        byte[] arrayAsc = new byte[6];
        byte[] arrayDesc = new byte[6];
        // CLR: Func<byte, bool> processors with captured counters replace the anonymous ByteProcessor classes.
        int ascIndex = 0;
        buf.ForEachByte(value =>
        {
            arrayAsc[ascIndex++] = value;
            return true;
        });
        int descIndex = 0;
        buf.ForEachByteDesc(value =>
        {
            arrayDesc[descIndex++] = value;
            return true;
        });
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, arrayAsc);
        Assert.Equal(new byte[] { 6, 5, 4, 3, 2, 1 }, arrayDesc);
        buf.Release();
    }

    [Fact]
    public void TestFindComponent()
    {
        CompositeByteBuf composite = NewCompositeBuffer();

        ByteBuf b1 = NewBuffer(10);
        b1.WriteByte('a');
        composite.AddComponent(b1, true);

        ByteBuf b2 = NewBuffer(10);
        b2.WriteByte('b');
        composite.AddComponent(b2, true);

        ByteBuf b3 = NewBuffer(10);
        b3.WriteByte('c');
        composite.AddComponent(b3, true);

        Assert.Equal((byte)'a', composite.ReadByte());
        composite.SkipBytes(1);
        Assert.Equal((byte)'c', composite.ReadByte());

        composite.Release();
    }
}
