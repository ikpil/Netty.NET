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
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Selected pinned iterator/internal-component contracts and CLR ownership/cache cases.
public class CompositeByteBufEnumerationTest
{
    private static byte[] Bytes(ByteBuf buffer)
    {
        return buffer.ReadableSequence.ToArray();
    }

    [Fact]
    public void TestIteratorClrEnumeratorContract()
    {
        var cbuf = new CompositeByteBuf();
        cbuf.AddComponent(Unpooled.EmptyBuffer);
        cbuf.AddComponent(Unpooled.EmptyBuffer);

        using IEnumerator<ByteBuf> it = cbuf.GetEnumerator();
        try
        {
            // CLR: MoveNext/Current replace hasNext/next; Current is invalid before the first MoveNext.
            Assert.Throws<InvalidOperationException>(() => it.Current);
            Assert.True(it.MoveNext());
            Assert.Same(Unpooled.EmptyBuffer, it.Current);
            // CLR: the enumerator returns the shared borrowed component cache.
            Assert.Same(cbuf.InternalComponent(0), it.Current);
            Assert.True(it.MoveNext());
            Assert.Same(Unpooled.EmptyBuffer, it.Current);
            Assert.False(it.MoveNext());

            // CLR: an exhausted IEnumerator keeps returning false and its Current throws InvalidOperationException
            // instead of next() throwing NoSuchElementException; Reset is unsupported.
            Assert.False(it.MoveNext());
            Assert.Throws<InvalidOperationException>(() => it.Current);
            Assert.Throws<NotSupportedException>(() => it.Reset());
        }
        finally { cbuf.Release(); }
        Assert.Equal(1, Unpooled.EmptyBuffer.RefCnt);
    }

    [Fact]
    public void EmptyIteratorStaysEmptyAfterAddAndRelease()
    {
        var buffer = new CompositeByteBuf();
        using IEnumerator<ByteBuf> iterator = buffer.GetEnumerator();
        buffer.AddComponent(Unpooled.EmptyBuffer);
        Assert.False(iterator.MoveNext());
        Assert.Throws<InvalidOperationException>(() => iterator.Current);
        buffer.Release();
        Assert.False(iterator.MoveNext());
        Assert.Throws<IllegalReferenceCountException>(() => buffer.GetEnumerator());
        Assert.Throws<IllegalReferenceCountException>(() => ((IEnumerable)buffer).GetEnumerator());
    }

    [Fact]
    public void TestEmptyIteratorClrEnumeratorContract()
    {
        var buffer = new CompositeByteBuf();
        try
        {
            using IEnumerator<ByteBuf> iterator = buffer.GetEnumerator();
            Assert.False(iterator.MoveNext());
            // IEnumerator combines Java hasNext/next; Current is invalid after exhaustion.
            Assert.Throws<InvalidOperationException>(() => iterator.Current);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void TestIteratorConcurrentModificationAddClrEnumeratorContract()
    {
        var cbuf = new CompositeByteBuf();
        cbuf.AddComponent(Unpooled.EmptyBuffer);

        using IEnumerator<ByteBuf> it = cbuf.GetEnumerator();
        cbuf.AddComponent(Unpooled.EmptyBuffer);

        try
        {
            // CLR: MoveNext combines hasNext/next and detects the component-count change;
            // InvalidOperationException replaces ConcurrentModificationException.
            Assert.Throws<InvalidOperationException>(() => it.MoveNext());
            Assert.Throws<InvalidOperationException>(() => it.Current);
        }
        finally
        {
            cbuf.Release();
        }
    }

    [Fact]
    public void TestIteratorConcurrentModificationRemoveClrEnumeratorContract()
    {
        var cbuf = new CompositeByteBuf();
        cbuf.AddComponent(Unpooled.EmptyBuffer);

        using IEnumerator<ByteBuf> it = cbuf.GetEnumerator();
        cbuf.RemoveComponent(0);

        try
        {
            // CLR: MoveNext combines hasNext/next and detects the component-count change;
            // InvalidOperationException replaces ConcurrentModificationException.
            Assert.Throws<InvalidOperationException>(() => it.MoveNext());
            Assert.Throws<InvalidOperationException>(() => it.Current);
        }
        finally
        {
            cbuf.Release();
        }
    }

    [Fact]
    public void IteratorObservesReplacementWhenCountStaysTheSame()
    {
        ByteBuf first = Unpooled.WrappedBuffer(new byte[] { 1 });
        ByteBuf second = Unpooled.WrappedBuffer(new byte[] { 2 });
        var buffer = new CompositeByteBuf();
        buffer.AddComponent(first);
        using IEnumerator<ByteBuf> iterator = buffer.GetEnumerator();
        buffer.RemoveComponent(0).AddComponent(second);
        try
        {
            Assert.True(iterator.MoveNext());
            Assert.Same(second, iterator.Current);
            Assert.False(iterator.MoveNext());
        }
        finally { buffer.Release(); }
        Assert.Equal(0, first.RefCnt);
        Assert.Equal(0, second.RefCnt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IteratorDisposalAndEarlyForeachExitDoNotReleaseComponents(bool direct)
    {
        var allocator = new NativeMemoryAllocator(64);
        ByteBuf source = direct ? new UnpooledDirectByteBuf(4, allocator: allocator) : Unpooled.Buffer(4);
        source.WriteBytes(new byte[] { 9, 1, 2, 8 }).SetIndex(1, 3);
        var buffer = new CompositeByteBuf();
        buffer.AddComponent(source, true);
        try
        {
            using (IEnumerator<ByteBuf> iterator = buffer.GetEnumerator())
            {
                Assert.True(iterator.MoveNext());
                Assert.Equal(new byte[] { 1, 2 }, Bytes(iterator.Current));
                Assert.Same(buffer.ComponentSlice(0), iterator.Current);
                iterator.Dispose();
                Assert.False(iterator.MoveNext());
                Assert.Throws<InvalidOperationException>(() => iterator.Current);
            }
            foreach (ByteBuf component in buffer)
            {
                component.SetByte(0, 7);
                break;
            }
            Assert.Equal(7, buffer.GetByte(0));
            Assert.Equal(1, source.RefCnt);
            Assert.Equal(1, source.ReaderIndex);
            Assert.Equal(3, source.WriterIndex);
            IEnumerator nongeneric = ((IEnumerable)buffer).GetEnumerator();
            try { Assert.True(nongeneric.MoveNext()); Assert.Same(buffer.InternalComponent(0), nongeneric.Current); }
            finally { ((IDisposable)nongeneric).Dispose(); }
        }
        finally { buffer.Release(); }
        Assert.Equal(0, source.RefCnt);
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void ReleaseInvalidatesNonemptyIteratorWithoutAccessingFreedMemory()
    {
        var buffer = new CompositeByteBuf();
        buffer.AddComponent(Unpooled.WrappedBuffer(new byte[] { 1 }));
        using IEnumerator<ByteBuf> iterator = buffer.GetEnumerator();
        buffer.Release();
        Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
        Assert.Throws<IllegalReferenceCountException>(() => buffer.InternalComponent(0));
        Assert.Throws<IllegalReferenceCountException>(() => buffer.InternalComponentAtOffset(0));
    }

    [Fact]
    public void WholeSourceIsReusedButPartialSourceHasOneCachedSlice()
    {
        ByteBuf whole = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
        ByteBuf partial = Unpooled.WrappedBuffer(new byte[] { 9, 3, 4, 8 }).SetIndex(1, 3);
        var buffer = new CompositeByteBuf();
        buffer.AddComponents(new[] { whole, partial }, true);
        try
        {
            Assert.Same(whole, buffer.ComponentSlice(0));
            ByteBuf cached = buffer.ComponentSlice(1);
            Assert.NotSame(partial, cached);
            Assert.Same(cached, buffer.ComponentSlice(1));
            Assert.Same(cached, buffer.InternalComponent(1));
            Assert.Same(cached, buffer.InternalComponentAtOffset(2));
            Assert.Same(cached, buffer.InternalComponentAtOffset(3));
            Assert.Equal(new byte[] { 3, 4 }, Bytes(cached));
            cached.ReaderIndex = 1;
            Assert.Equal(1, buffer.ComponentSlice(1).ReaderIndex);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(1, partial.ReaderIndex);
            ByteBuf duplicate = buffer.Component(1);
            Assert.NotSame(cached, duplicate);
            Assert.Equal(4, duplicate.Capacity);
            Assert.Equal(1, duplicate.ReaderIndex);
            Assert.Equal(3, duplicate.WriterIndex);
            Assert.NotSame(duplicate, buffer.ComponentAtOffset(2));
            Assert.Equal(new[] { 1, 1 }, new[] { whole.RefCnt, partial.RefCnt });
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void InternalOffsetSkipsEmptyComponentsAndChecksBounds()
    {
        var buffer = new CompositeByteBuf();
        ByteBuf a = Unpooled.WrappedBuffer(new byte[] { 1 });
        ByteBuf b = Unpooled.WrappedBuffer(new byte[] { 2 });
        buffer.AddComponents(new[] { Unpooled.EmptyBuffer, a, Unpooled.EmptyBuffer, b, Unpooled.EmptyBuffer }, true);
        try
        {
            Assert.Same(Unpooled.EmptyBuffer, buffer.InternalComponent(2));
            Assert.Same(a, buffer.InternalComponentAtOffset(0));
            Assert.Same(b, buffer.InternalComponentAtOffset(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.InternalComponent(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.InternalComponent(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.InternalComponentAtOffset(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.InternalComponentAtOffset(2));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ShrinkAndDiscardReplaceCachedViewsWithCorrectCoordinates(bool direct, bool partial)
    {
        var allocator = new NativeMemoryAllocator(64);
        ByteBuf source = direct ? new UnpooledDirectByteBuf(6, allocator: allocator) : Unpooled.Buffer(6);
        source.WriteBytes(new byte[] { 9, 1, 2, 3, 4, 8 });
        if (partial) source.SetIndex(1, 5);
        var buffer = new CompositeByteBuf();
        buffer.AddComponent(source, true);
        try
        {
            ByteBuf original = buffer.InternalComponent(0);
            int initialLength = buffer.Capacity;
            buffer.Capacity = initialLength - 1;
            ByteBuf shrunk = buffer.InternalComponent(0);
            Assert.NotSame(original, shrunk);
            Assert.Equal(initialLength, original.Capacity);
            Assert.Equal(initialLength - 1, shrunk.Capacity);
            Assert.Equal(Bytes(buffer), Bytes(shrunk));
            Assert.Same(shrunk, buffer.ComponentSlice(0));
            buffer.ReaderIndex = 1;
            buffer.DiscardReadBytes();
            ByteBuf discarded = buffer.InternalComponent(0);
            Assert.NotSame(shrunk, discarded);
            Assert.Equal(initialLength - 2, discarded.Capacity);
            Assert.Equal(Bytes(buffer), Bytes(discarded));
            Assert.Equal(0, discarded.ReaderIndex);
            Assert.Equal(discarded.Capacity, discarded.WriterIndex);
            Assert.Same(discarded, buffer.Single());
            Assert.Equal(1, source.RefCnt);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void TrimmingBeforeMaterializingTheCacheUsesUpdatedCapturedRange()
    {
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 9, 1, 2, 3, 4, 8 }).SetIndex(1, 5);
        var buffer = new CompositeByteBuf();
        buffer.AddComponent(source, true);
        try
        {
            buffer.Capacity = 3;
            buffer.ReaderIndex = 1;
            buffer.DiscardReadBytes();
            Assert.Equal(new byte[] { 2, 3 }, Bytes(buffer.InternalComponent(0)));
            Assert.Same(buffer.ComponentSlice(0), buffer.Single());
            Assert.Equal(1, source.ReaderIndex);
            Assert.Equal(5, source.WriterIndex);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void RetainedCachedViewSurvivesRemovalWhileUnretainedViewDoesNot()
    {
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 9, 1, 2, 8 }).SetIndex(1, 3);
        var buffer = new CompositeByteBuf();
        buffer.AddComponent(source, true);
        ByteBuf retained = buffer.InternalComponent(0).Retain();
        // Take temporary ownership of the component.
        buffer.RemoveComponent(0).SetIndex(0, 0);
        buffer.Release();
        Assert.Equal(1, retained.RefCnt);
        Assert.Equal(new byte[] { 1, 2 }, Bytes(retained));
        Assert.True(retained.Release());
        Assert.Equal(0, source.RefCnt);

        var other = new CompositeByteBuf();
        other.AddComponent(Unpooled.WrappedBuffer(new byte[] { 3 }));
        ByteBuf borrowed = other.InternalComponent(0);
        other.RemoveComponent(0);
        Assert.Throws<IllegalReferenceCountException>(() => borrowed.GetByte(0));
        other.Release();
    }

    [Fact]
    public void CachedReadOnlyNestedViewKeepsPermissionsAcrossTrimming()
    {
        var nested = new CompositeByteBuf();
        nested.AddComponents(new[] { Unpooled.WrappedBuffer(new byte[] { 9, 1 }), Unpooled.WrappedBuffer(new byte[] { 2, 8 }) }, true);
        ByteBuf input = nested.AsReadOnly().SetIndex(1, 3);
        var buffer = new CompositeByteBuf();
        buffer.AddComponent(input, true);
        try
        {
            ByteBuf cached = buffer.InternalComponent(0);
            Assert.True(cached.IsReadOnly);
            Assert.Equal(new byte[] { 1, 2 }, Bytes(cached));
            Assert.Throws<NotSupportedException>(() => buffer.Single().SetByte(0, 7));
            buffer.Capacity = 1;
            ByteBuf replacement = buffer.InternalComponent(0);
            Assert.NotSame(cached, replacement);
            Assert.True(replacement.IsReadOnly);
            Assert.Equal(new byte[] { 1 }, Bytes(replacement));
            Assert.Equal(1, nested.RefCnt);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, nested.RefCnt);
    }

    [Fact]
    public void FlattenedWholeComponentCreatesItsOwnSliceInsteadOfReusingSourceCache()
    {
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
        var from = new CompositeByteBuf();
        from.AddComponent(source, true);
        Assert.Same(source, from.ComponentSlice(0));
        var target = new CompositeByteBuf();
        target.AddFlattenedComponents(from, true);
        try
        {
            ByteBuf cached = target.ComponentSlice(0);
            Assert.NotSame(source, cached);
            Assert.Same(cached, target.InternalComponent(0));
            Assert.Same(cached, target.Single());
            cached.ReaderIndex = 1;
            Assert.Equal(0, source.ReaderIndex);
            Assert.Equal(0, target.ReaderIndex);
            Assert.Equal(1, source.RefCnt);
        }
        finally { target.Release(); }
    }

    [Fact]
    public void EnumerationSourcePassedToAddComponentsIsTransferredAsOneBuffer()
    {
        var source = new CompositeByteBuf();
        source.AddComponents(new[] { Unpooled.WrappedBuffer(new byte[] { 1 }), Unpooled.WrappedBuffer(new byte[] { 2 }) }, true);
        var target = new CompositeByteBuf();
        target.AddComponents((IEnumerable<ByteBuf>)source, true);
        try
        {
            Assert.Equal(1, target.NumComponents);
            Assert.Same(source, target.Single());
            Assert.Equal(new byte[] { 1, 2 }, Bytes(target));
            Assert.Equal(1, source.RefCnt);
        }
        finally { target.Release(); }
        Assert.Equal(0, source.RefCnt);
    }

    [Fact]
    public void IndexedInsertionAndRemovalKeepSurvivingComponentCache()
    {
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 9, 1, 2, 8 }).SetIndex(1, 3);
        var buffer = new CompositeByteBuf();
        buffer.AddComponent(source, true);
        try
        {
            ByteBuf cached = buffer.InternalComponent(0);
            buffer.AddComponent(0, Unpooled.WrappedBuffer(new byte[] { 3 }), true);
            Assert.Same(cached, buffer.InternalComponent(1));
            Assert.Same(cached, buffer.InternalComponentAtOffset(1));
            buffer.RemoveComponent(0).SetIndex(0, 2);
            Assert.Same(cached, buffer.InternalComponent(0));
            Assert.Same(cached, buffer.InternalComponentAtOffset(0));
            Assert.Equal(new byte[] { 1, 2 }, Bytes(cached));
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void ConsolidationInvalidatesIteratorAndReleasesOriginalCachedOwnership()
    {
        ByteBuf first = Unpooled.WrappedBuffer(new byte[] { 1 });
        ByteBuf second = Unpooled.WrappedBuffer(new byte[] { 2 });
        var buffer = new CompositeByteBuf(2);
        buffer.AddComponents(new[] { first, second }, true);
        using IEnumerator<ByteBuf> iterator = buffer.GetEnumerator();
        Assert.True(iterator.MoveNext());
        ByteBuf retained = iterator.Current.Retain();
        ByteBuf borrowed = buffer.InternalComponent(1);
        try
        {
            buffer.AddComponent(Unpooled.WrappedBuffer(new byte[] { 3 }), true);
            Assert.Equal(1, buffer.NumComponents);
            Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
            Assert.Throws<IllegalReferenceCountException>(() => borrowed.GetByte(0));
            Assert.Equal(new byte[] { 1 }, Bytes(retained));
            Assert.Equal(new byte[] { 1, 2, 3 }, Bytes(buffer.Single()));
            Assert.NotSame(retained, buffer.InternalComponent(0));
            Assert.Equal(1, first.RefCnt);
            Assert.Equal(0, second.RefCnt);
        }
        finally { buffer.Release(); retained.Release(); }
    }

    [Fact]
    public void FailedSliceMaterializationCanBeRetriedAfterSourceRepair()
    {
        ByteBuf source = Unpooled.Buffer(4).WriteBytes(new byte[] { 9, 1, 2, 8 }).SetIndex(1, 3);
        var buffer = new CompositeByteBuf();
        buffer.AddComponent(source, true);
        try
        {
            source.Capacity = 1;
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.InternalComponent(0));
            Assert.Equal(1, source.RefCnt);
            source.Capacity = 4;
            source.SetByte(1, 1).SetByte(2, 2);
            ByteBuf cached = buffer.InternalComponent(0);
            Assert.Same(cached, buffer.ComponentSlice(0));
            Assert.Equal(new byte[] { 1, 2 }, Bytes(cached));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ComponentSliceFromFlattenedCompositeShouldHaveCorrectView(bool direct, bool pooled)
    {
        // CLR: exercise the original pooled factory as well as heap/native unpooled storage.
        var allocator = new NativeMemoryAllocator(pooled ? 8192 : 64);
        using var pool = pooled ? new PooledByteBufAllocator(direct, pageSize: 4096,
            maxOrder: 1, nativeAllocator: allocator) : null;
        ByteBuf source = pooled ? pool.Buffer(32)
            : direct ? new UnpooledDirectByteBuf(32, allocator: allocator) : Unpooled.Buffer(32);
        CompositeByteBuf first = null, second = null;
        ByteBuf component = null;
        try
        {
            source.WriteBytes(Encoding.ASCII.GetBytes("---01234"));
            first = (pooled ? pool.CompositeBuffer(8) : new CompositeByteBuf(8)).AddFlattenedComponents(source, true);
            source = null;
            Assert.Equal("---", first.ReadCharSequence(3, Encoding.ASCII).ToString());
            second = (pooled ? pool.CompositeBuffer(8) : new CompositeByteBuf(8)).AddFlattenedComponents(first, true);
            first = null;
            Assert.Equal("01234", Encoding.ASCII.GetString(Bytes(second)));

            // Remove the last component from the cumulation, then add it back.
            int tailComponentIndex = second.NumComponents - 1;
            int tailStart = second.ToByteIndex(tailComponentIndex);
            component = second.ComponentSlice(tailComponentIndex);
            Assert.Equal(5, component.ReadableBytes);
            Assert.Equal("01234", Encoding.ASCII.GetString(Bytes(component)));
            Assert.Same(component, second.InternalComponent(tailComponentIndex));
            // Take temporary ownership of the component.
            component.Retain();
            // Remove the component from the composite buf.
            second.RemoveComponent(tailComponentIndex).SetIndex(0, tailStart);
            second.AddFlattenedComponents(component, true);
            component = null;
            Assert.Equal("01234", Encoding.ASCII.GetString(Bytes(second)));
        }
        finally
        {
            // On a success path, only composite2 will be non-null here.
            source?.Release(); first?.Release(); second?.Release(); component?.Release();
        }
        pool?.Dispose();
        Assert.Equal(0, allocator.ReservedBytes);
    }
}
