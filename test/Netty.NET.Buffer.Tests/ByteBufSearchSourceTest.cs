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

public class ByteBufSearchSourceTest
{
    private static ByteBuf NewBuffer(int provider, int length, NativeMemoryAllocator native)
    {
        if (provider == 0) return new UnpooledHeapByteBuf(length, 64);
        if (provider == 1) return new UnpooledDirectByteBuf(length, 64, native);
        var result = new CompositeByteBuf(16, false, native);
        for (int index = 0; index < length; index += 5)
        {
            int size = Math.Min(5, length - index);
            ByteBuf component = (index / 5) % 2 == 0 ? new UnpooledHeapByteBuf(size, size)
                : new UnpooledDirectByteBuf(size, size, native);
            component.WriterIndex = size;
            result.AddComponent(component, true);
        }
        return result.Clear();
    }

    // AbstractByteBufTest.testSWARIndexOf; identical original ranges and expected positions.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TestSWARIndexOf(int provider)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf buffer = NewBuffer(provider, 16, native);
        try
        {
            buffer.Clear();
            // Ensure the buffer is completely zero'ed.
            buffer.SetZero(0, buffer.Capacity);
            buffer.WriteByte((byte) 0); // 0
            buffer.WriteByte((byte) 0);
            buffer.WriteByte((byte) 0);
            buffer.WriteByte((byte) 0);
            buffer.WriteByte((byte) 0);
            buffer.WriteByte((byte) 0);
            buffer.WriteByte((byte) 0);
            buffer.WriteByte((byte) 0); // 7

            buffer.WriteByte((byte) 0);
            buffer.WriteByte((byte) 0);
            buffer.WriteByte((byte) 0);
            buffer.WriteByte((byte) 1); // 11
            buffer.WriteByte((byte) 2);
            buffer.WriteByte((byte) 3);
            buffer.WriteByte((byte) 4);
            buffer.WriteByte((byte) 1); // 15
            Assert.Equal(11, buffer.IndexOf(0, 12, (byte) 1));
            Assert.Equal(12, buffer.IndexOf(0, 16, (byte) 2));
            Assert.Equal(-1, buffer.IndexOf(0, 11, (byte) 1));
            Assert.Equal(11, buffer.IndexOf(0, 16, (byte) 1));

            // lastIndexOf
            Assert.Equal(15, buffer.IndexOf(16, 0, (byte) 1));
            Assert.Equal(12, buffer.IndexOf(16, 0, (byte) 2));
            Assert.Equal(11, buffer.IndexOf(15, 0, (byte) 1));
            Assert.Equal(-1, buffer.IndexOf(11, 0, (byte) 1));
        }
        finally
        {
            if (buffer.ReferenceCount > 0) buffer.Release();
            Assert.Equal(0, native.ReservedBytes);
        }
    }

    // AbstractByteBufTest.testUnrolledSWARIndexOf; identical original ranges and expected positions.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TestUnrolledSWARIndexOf(int provider)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf buffer = NewBuffer(provider, 15, native);
        try
        {
            buffer.Clear();
            // Ensure the buffer is completely zero'ed.
            buffer.SetZero(0, buffer.Capacity);
            buffer.WriteByte((byte) 0); // 0
            buffer.WriteByte((byte) 1);
            buffer.WriteByte((byte) 2);
            buffer.WriteByte((byte) 3);
            buffer.WriteByte((byte) 4);
            buffer.WriteByte((byte) 5);
            buffer.WriteByte((byte) 6);
            buffer.WriteByte((byte) 7); // 7

            buffer.WriteByte((byte) 8); // 8
            buffer.WriteByte((byte) 9);
            buffer.WriteByte((byte) 10);
            buffer.WriteByte((byte) 11);
            buffer.WriteByte((byte) 12);
            buffer.WriteByte((byte) 13);
            buffer.WriteByte((byte) 14); // 14
            Assert.Equal(15, buffer.Capacity);
            for (int i = 0; i < 14; ++i) {
                Assert.Equal(i, buffer.IndexOf(i, buffer.Capacity, (byte) i));
            }

            // lastIndexOf
            for (int i = 0; i < 14; ++i) {
                Assert.Equal(i, buffer.IndexOf(buffer.Capacity, 0, (byte) i));
            }
        }
        finally
        {
            if (buffer.ReferenceCount > 0) buffer.Release();
            Assert.Equal(0, native.ReservedBytes);
        }
    }

    // AbstractByteBufTest.testIndexOf; identical original ranges and expected positions.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TestIndexOf(int provider)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf buffer = NewBuffer(provider, 16, native);
        try
        {
            buffer.Clear();
            // Ensure the buffer is completely zero'ed.
            buffer.SetZero(0, buffer.Capacity);

            buffer.WriteByte((byte) 1);
            buffer.WriteByte((byte) 2);
            buffer.WriteByte((byte) 3);
            buffer.WriteByte((byte) 2);
            buffer.WriteByte((byte) 1);

            Assert.Equal(-1, buffer.IndexOf(1, 4, (byte) 1));
            Assert.Equal(-1, buffer.IndexOf(4, 1, (byte) 1));
            Assert.Equal(1, buffer.IndexOf(1, 4, (byte) 2));
            Assert.Equal(3, buffer.IndexOf(4, 1, (byte) 2));

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(0, buffer.Capacity + 1, (byte) 0));

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(buffer.Capacity, -1, (byte) 0));

            Assert.Equal(4, buffer.IndexOf(buffer.Capacity + 1, 0, (byte) 1));
            Assert.Equal(0, buffer.IndexOf(-1, buffer.Capacity, (byte) 1));

        }
        finally
        {
            if (buffer.ReferenceCount > 0) buffer.Release();
            Assert.Equal(0, native.ReservedBytes);
        }
    }

    // AbstractByteBufTest.testIndexOfReleaseBuffer; identical original ranges and expected positions.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TestIndexOfReleaseBuffer(int provider)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf buffer = NewBuffer(provider, 16, native);
        try
        {
            buffer.Release();
            if (buffer.Capacity != 0) {
                Assert.Throws<IllegalReferenceCountException>(() => buffer.IndexOf(0, 1, (byte) 1));
            } else {
                Assert.Equal(-1, buffer.IndexOf(0, 1, (byte) 1));
            }

        }
        finally
        {
            if (buffer.ReferenceCount > 0) buffer.Release();
            Assert.Equal(0, native.ReservedBytes);
        }
    }

    // AbstractByteBufTest.testEndiannessIndexOf; identical original ranges and expected positions.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TestEndiannessIndexOf(int provider)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf buffer = NewBuffer(provider, 16, native);
        try
        {
            buffer.Clear();
            const int v = 0x02030201;
            buffer.WriteIntLE(v);
            buffer.WriteByte(0x01);

            Assert.Equal(-1, buffer.IndexOf(1, 4, (byte) 1));
            Assert.Equal(-1, buffer.IndexOf(4, 1, (byte) 1));
            Assert.Equal(1, buffer.IndexOf(1, 4, (byte) 2));
            Assert.Equal(3, buffer.IndexOf(4, 1, (byte) 2));

        }
        finally
        {
            if (buffer.ReferenceCount > 0) buffer.Release();
            Assert.Equal(0, native.ReservedBytes);
        }
    }

    private sealed class Failure : Exception;
    private sealed class ObservedComposite : CompositeByteBuf
    {
        public int Gets;
        public override byte GetByte(int index) { ++Gets; throw new Failure(); }
    }

    public static IEnumerable<object[]> KernelCases()
    {
        foreach (int chunk in new[] { 1, 5, 16 })
        for (int form = 0; form < 4; ++form)
        foreach (bool descending in new[] { false, true })
        foreach (byte value in new byte[] { 128, 7, 255 }) yield return [chunk, form, descending, value];
    }

    [Theory]
    [MemberData(nameof(KernelCases))]
    public void SegmentedSearchUsesStorageIndependentlyOfPublicGetByte(int chunk, int form, bool descending, byte value)
    {
        byte[] bytes = [128, 1, 2, 3, 4, 128, 6, 7, 8, 9, 10, 11, 12, 13, 14, 128];
        var native = new NativeMemoryAllocator();
        var root = new ObservedComposite();
        for (int index = 0; index < bytes.Length; index += chunk)
        {
            int size = Math.Min(chunk, bytes.Length - index);
            ByteBuf component = (index / chunk) % 2 == 0 ? new UnpooledHeapByteBuf(size, size)
                : new UnpooledDirectByteBuf(size, size, native);
            component.WriteBytes(bytes.AsSpan(index, size)); root.AddComponent(component, true);
        }
        root.SetIndex(2, 14).MarkReaderIndex().MarkWriterIndex();
        ByteBuf view = form switch
        {
            1 => root.Duplicate(),
            2 => root.Slice(1, 14),
            3 => root.Slice(1, 14).Slice(1, 12),
            _ => root
        };
        if (form != 0) view.SetIndex(1, view.Capacity - 1).MarkReaderIndex().MarkWriterIndex();
        int offset = form == 2 ? 1 : form == 3 ? 2 : 0;
        int expected = -1;
        for (int i = 0; i < view.Capacity; ++i)
        {
            int index = descending ? view.Capacity - i - 1 : i;
            if (bytes[offset + index] == value) { expected = index; break; }
        }
        try
        {
            Assert.Equal(expected, view.IndexOf(descending ? view.Capacity : 0, descending ? 0 : view.Capacity, value));
            Assert.Equal(0, root.Gets);
            Assert.Equal(bytes.AsSpan(offset, view.Capacity).ToArray(), ByteBufUtil.GetBytes(view, 0, view.Capacity));
            int reader = form == 0 ? 2 : 1, writer = form == 0 ? 14 : view.Capacity - 1;
            Assert.Equal(reader, view.ReaderIndex); Assert.Equal(writer, view.WriterIndex);
            view.SetIndex(0, view.Capacity).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(reader, view.ReaderIndex); Assert.Equal(writer, view.WriterIndex);
            Assert.Equal(2, root.ReaderIndex); Assert.Equal(14, root.WriterIndex);
            Assert.Equal(1, root.ReferenceCount);
        }
        finally { root.Release(); Assert.Equal(0, native.ReservedBytes); }
    }

    private sealed class SearchCalls(int outcome)
    {
        public int Count, From, To;
        public byte Value;
        public readonly Failure Error = new();
        public int Invoke(int from, int to, byte value)
        {
            ++Count; From = from; To = to; Value = value;
            if (outcome == 2) throw Error;
            return outcome == 0 ? 6 : -1;
        }
    }
    private sealed class SearchHeap(SearchCalls calls) : UnpooledHeapByteBuf(16, 64)
    {
        public override int IndexOf(int fromIndex, int toIndex, byte value) => calls.Invoke(fromIndex, toIndex, value);
    }
    private sealed class SearchDirect(SearchCalls calls, NativeMemoryAllocator native) : UnpooledDirectByteBuf(16, 64, native)
    {
        public override int IndexOf(int fromIndex, int toIndex, byte value) => calls.Invoke(fromIndex, toIndex, value);
    }

    public static IEnumerable<object[]> DelegationCases()
    {
        foreach (bool direct in new[] { false, true })
        for (int overload = 0; overload < 3; ++overload)
        for (int outcome = 0; outcome < 3; ++outcome) yield return [direct, overload, outcome];
    }

    [Theory]
    [MemberData(nameof(DelegationCases))]
    public void BytesBeforePreservesIndexOfDispatchRelativeDistanceAndCursors(bool direct, int overload, int outcome)
    {
        var native = new NativeMemoryAllocator();
        var calls = new SearchCalls(outcome);
        ByteBuf buffer = direct ? new SearchDirect(calls, native) : new SearchHeap(calls);
        buffer.SetIndex(2, 12).MarkReaderIndex().MarkWriterIndex();
        int Invoke() => overload switch { 0 => buffer.BytesBefore(128), 1 => buffer.BytesBefore(5, 128), _ => buffer.BytesBefore(3, 8, 128) };
        try
        {
            if (outcome == 2) Assert.Same(calls.Error, Assert.Throws<Failure>(() => Invoke()));
            else Assert.Equal(outcome == 1 ? -1 : overload == 2 ? 3 : 4, Invoke());
            Assert.Equal(1, calls.Count); Assert.Equal(overload == 2 ? 3 : 2, calls.From);
            Assert.Equal(overload == 0 ? 12 : overload == 1 ? 7 : 11, calls.To); Assert.Equal(128, calls.Value);
            Assert.Equal(2, buffer.ReaderIndex); Assert.Equal(12, buffer.WriterIndex);
            buffer.SetIndex(0, 16).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(2, buffer.ReaderIndex); Assert.Equal(12, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount);
        }
        finally { buffer.Release(); Assert.Equal(0, native.ReservedBytes); }
    }
}
