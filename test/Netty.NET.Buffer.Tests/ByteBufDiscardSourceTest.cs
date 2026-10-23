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

public class ByteBufDiscardSourceTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        private readonly PooledByteBufAllocator _pool;
        public readonly ByteBuf Root;
        public Storage(int provider, int length)
        {
            if (provider >= 3)
                _pool = new PooledByteBufAllocator(nativeAllocator: Native, maxOrder: 1,
                    smallCacheSize: 0, normalCacheSize: 0);
            Root = provider switch
            {
                0 => new UnpooledHeapByteBuf(length),
                1 => new UnpooledDirectByteBuf(length, int.MaxValue, Native),
                2 => new CompositeByteBuf(16, false, Native)
                    .AddComponent(new UnpooledHeapByteBuf(length / 2).SetIndex(0, length / 2), true)
                    .AddComponent(new UnpooledDirectByteBuf(length - length / 2, int.MaxValue, Native)
                        .SetIndex(0, length - length / 2), true),
                3 => new PooledHeapByteBuf((PoolHeapArena)_pool.HeapArenas[0], length, int.MaxValue),
                _ => new PooledDirectByteBuf((PoolDirectArena)_pool.DirectArenas[0], length, int.MaxValue)
            };
            Root.Clear();
        }
        public void Dispose()
        {
            if (Root.ReferenceCount > 0) Root.Release();
            Assert.Equal(0, Root.ReferenceCount);
            _pool?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> OriginalCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int scenario = 0; scenario < 5; ++scenario) yield return [provider, scenario];
    }

    /**
     * The similar test case with {@link #testDiscardReadBytes()} but this one
     * discards a large chunk at once.
     */
    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void OriginalDiscardFixtures(int provider, int scenario)
    {
        const int capacity = 4096;
        using var s = new Storage(provider, scenario >= 3 ? 8 : capacity);
        ByteBuf buffer = s.Root;
        if (scenario >= 3)
        {
            // Clear the buffer so we are sure the reader and writer indices are 0.
            // This is important as we may return a slice from newBuffer(...).
            buffer.Clear();
            Assert.True(buffer.Release());
            if (scenario == 3) Assert.Throws<IllegalReferenceCountException>(() => buffer.DiscardReadBytes());
            else Assert.Throws<IllegalReferenceCountException>(() => buffer.DiscardSomeReadBytes());
            return;
        }
        if (scenario == 2)
        {
            buffer.WriterIndex = buffer.Capacity;
            buffer.ReaderIndex = buffer.WriterIndex;
            Assert.Same(buffer, buffer.DiscardReadBytes());
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(0, buffer.WriterIndex);
            return;
        }
        buffer.WriterIndex = 0;
        if (scenario == 0)
        {
            for (int i = 0; i < buffer.Capacity; i += 4) buffer.WriteInt(i);
        }
        else
        {
            for (int i = 0; i < buffer.Capacity; ++i) buffer.WriteByte(unchecked((byte)i));
        }
        // CLR: independent managed bytes replace the original allocated comparison buffer.
        byte[] copy = ByteBufUtil.GetBytes(buffer, 0, capacity);
        if (scenario == 0)
        {
            // Make sure there's no effect if called when readerIndex is 0.
            buffer.ReaderIndex = capacity / 4; buffer.MarkReaderIndex();
            buffer.WriterIndex = capacity / 3; buffer.MarkWriterIndex();
            buffer.ReaderIndex = 0; buffer.WriterIndex = capacity / 2;
            Assert.Same(buffer, buffer.DiscardReadBytes());
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(capacity / 2, buffer.WriterIndex);
            Assert.Equal(copy[..(capacity / 2)], ByteBufUtil.GetBytes(buffer, 0, capacity / 2));
            buffer.ResetReaderIndex(); Assert.Equal(capacity / 4, buffer.ReaderIndex);
            buffer.ResetWriterIndex(); Assert.Equal(capacity / 3, buffer.WriterIndex);

            // Make sure bytes after writerIndex is not copied.
            buffer.ReaderIndex = 1; buffer.WriterIndex = capacity / 2;
            buffer.DiscardReadBytes();
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(capacity / 2 - 1, buffer.WriterIndex);
            Assert.Equal(copy[1..(capacity / 2)], ByteBufUtil.GetBytes(buffer, 0, capacity / 2 - 1));
            byte[] writable = ByteBufUtil.GetBytes(buffer, capacity / 2 - 1, capacity / 2);
            if (provider != 2)
            {
                // If writable bytes were copied, the test should fail to avoid unnecessary memory bandwidth consumption.
                Assert.False(copy.AsSpan(capacity / 2, capacity / 2).SequenceEqual(writable));
            }
            else
            {
                // Composite buffer does not waste bandwidth on discardReadBytes, but
                // the test will fail in strict mode.
                Assert.Equal(copy[(capacity / 2)..], writable);
            }
            // Marks also should be relocated.
            buffer.ResetReaderIndex(); Assert.Equal(capacity / 4 - 1, buffer.ReaderIndex);
            buffer.ResetWriterIndex(); Assert.Equal(capacity / 3 - 1, buffer.WriterIndex);
        }
        else
        {
            // Discard the first (CAPACITY / 2 - 1) bytes.
            buffer.SetIndex(capacity / 2 - 1, capacity - 1);
            buffer.DiscardReadBytes();
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(capacity / 2, buffer.WriterIndex);
            for (int i = 0; i < capacity / 2; ++i)
                Assert.Equal(copy[(capacity / 2 - 1 + i)..(capacity - 1)],
                    ByteBufUtil.GetBytes(buffer, i, capacity / 2 - i));
        }
        Assert.Equal(1, buffer.ReferenceCount);
    }

    public static IEnumerable<object[]> MarkCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (bool some in new[] { false, true })
        for (int state = 0; state < 8; ++state) yield return [provider, some, state];
    }

    [Theory]
    [MemberData(nameof(MarkCases))]
    public void DiscardPreservesUnreadBytesAndRelocatesMarks(int provider, bool some, int state)
    {
        using var s = new Storage(provider, 16);
        byte[] bytes = [13, 29, 47, 61, 83, 101, 127, 139, 157, 173, 191, 211, 227, 239, 251, 7];
        (int reader, int writer, int markedReader, int markedWriter) = state switch
        {
            0 => (0, 12, 0, 0), 1 => (1, 12, 0, 0), 2 => (7, 12, 2, 4), 3 => (8, 12, 8, 9),
            4 => (9, 12, 10, 11), 5 => (16, 16, 15, 16), 6 => (4, 4, 1, 3), _ => (0, 0, 0, 0)
        };
        ByteBuf buffer = s.Root;
        buffer.SetBytes(0, bytes).SetIndex(markedReader, markedWriter).MarkReaderIndex().MarkWriterIndex();
        buffer.SetIndex(reader, writer);
        // Model the documented storage policies with independent managed bytes.
        // A composite drops prefixes; contiguous storage moves only the unread window.
        int removed = provider == 2
            ? some ? reader / 8 * 8 : reader
            : !some || reader == writer || reader >= 8 ? reader : 0;
        byte[] expected = (byte[])bytes.Clone();
        if (provider == 2) expected = bytes[removed..];
        else if (removed != 0) bytes.AsSpan(reader, writer - reader).CopyTo(expected);
        byte[] unread = bytes[reader..writer];
        Assert.Same(buffer, some ? buffer.DiscardSomeReadBytes() : buffer.DiscardReadBytes());
        Assert.Equal(reader - removed, buffer.ReaderIndex); Assert.Equal(writer - removed, buffer.WriterIndex);
        Assert.Equal(provider == 2 ? 16 - removed : 16, buffer.Capacity);
        Assert.Equal(expected, ByteBufUtil.GetBytes(buffer, 0, buffer.Capacity));
        Assert.Equal(unread, ByteBufUtil.GetBytes(buffer, buffer.ReaderIndex, buffer.ReadableBytes));
        Assert.Equal(1, buffer.ReferenceCount);
        buffer.ResetReaderIndex(); Assert.Equal(Math.Max(0, markedReader - removed), buffer.ReaderIndex);
        buffer.ResetWriterIndex(); Assert.Equal(Math.Max(0, markedWriter - removed), buffer.WriterIndex);
    }

    private sealed class Failure : Exception;
    private sealed class Calls
    {
        public int Count;
        public bool Fail;
        public readonly Failure Error = new();
        public void Copy(ByteBuf receiver, int destination, ByteBuf source, int sourceIndex, int length)
        {
            ++Count; Assert.Same(receiver, source);
            Assert.Equal(0, destination); Assert.Equal(4, sourceIndex); Assert.Equal(4, length);
            if (Fail) throw Error;
        }
    }
    private sealed class Heap(Calls calls) : UnpooledHeapByteBuf(8, 8)
    {
        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        { calls.Copy(this, index, source, sourceIndex, length); return base.SetBytes(index, source, sourceIndex, length); }
    }
    private sealed class Direct(Calls calls, NativeMemoryAllocator native) : UnpooledDirectByteBuf(8, 8, native)
    {
        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        { calls.Copy(this, index, source, sourceIndex, length); return base.SetBytes(index, source, sourceIndex, length); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FullDiscardUsesTheCopyHookAndPublishesIndicesOnlyAfterSuccess(bool direct, bool fail)
    {
        var native = new NativeMemoryAllocator(8);
        var calls = new Calls { Fail = fail };
        ByteBuf buffer = direct ? new Direct(calls, native) : new Heap(calls);
        try
        {
            byte[] bytes = [13, 29, 47, 61, 83, 101, 127, 139];
            buffer.SetBytes(0, bytes).SetIndex(5, 7).MarkReaderIndex().MarkWriterIndex().SetIndex(4, 8);
            if (fail) Assert.Same(calls.Error, Assert.Throws<Failure>(() => buffer.DiscardReadBytes()));
            else Assert.Same(buffer, buffer.DiscardReadBytes());
            Assert.Equal(1, calls.Count); Assert.Equal(fail ? 4 : 0, buffer.ReaderIndex);
            Assert.Equal(fail ? 8 : 4, buffer.WriterIndex);
            Assert.Equal(fail ? bytes : new byte[] { 83, 101, 127, 139, 83, 101, 127, 139 },
                ByteBufUtil.GetBytes(buffer, 0, 8));
            buffer.ResetReaderIndex(); Assert.Equal(fail ? 5 : 1, buffer.ReaderIndex);
            buffer.ResetWriterIndex(); Assert.Equal(fail ? 7 : 3, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount);
        }
        finally { Assert.True(buffer.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }
}
