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

public class ByteBufDiscardDispatchTest
{
    private sealed class Failure : Exception;
    private sealed class Calls
    {
        public int Full, Copy;
        public bool ThrowOnFull, ThrowOnCopy;
        public readonly Failure Error = new();
        public (int Destination, int Source, int Length) Range;
        public void BeforeCopy(ByteBuf receiver, int destination, ByteBuf source, int sourceIndex, int length)
        {
            ++Copy;
            Assert.Same(receiver, source);
            Range = (destination, sourceIndex, length);
            if (ThrowOnCopy) throw Error;
        }
        public void BeforeFull()
        {
            ++Full;
            if (ThrowOnFull) throw Error;
        }
    }

    private sealed class Heap(Calls calls) : UnpooledHeapByteBuf(8, 8)
    {
        public override ByteBuf DiscardReadBytes() { calls.BeforeFull(); return base.DiscardReadBytes(); }
        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        { calls.BeforeCopy(this, index, source, sourceIndex, length); return base.SetBytes(index, source, sourceIndex, length); }
    }

    private sealed class Direct(Calls calls, NativeMemoryAllocator native) : UnpooledDirectByteBuf(8, 8, native)
    {
        public override ByteBuf DiscardReadBytes() { calls.BeforeFull(); return base.DiscardReadBytes(); }
        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        { calls.BeforeCopy(this, index, source, sourceIndex, length); return base.SetBytes(index, source, sourceIndex, length); }
    }

    public static IEnumerable<object[]> Positions()
    {
        foreach (bool direct in new[] { false, true })
        foreach (int reader in new[] { 0, 1, 3, 4, 5, 8 }) yield return [direct, reader];
    }

    // AbstractByteBuf.discardSomeReadBytes compacts through setBytes, not discardReadBytes.
    [Theory]
    [MemberData(nameof(Positions))]
    public void PartialDiscardUsesOnlyTheOriginalCopyHookAndAdjustsMarkers(bool direct, int reader)
    {
        var native = new NativeMemoryAllocator(8);
        var calls = new Calls { ThrowOnFull = true };
        ByteBuf buffer = direct ? new Direct(calls, native) : new Heap(calls);
        try
        {
            buffer.WriteBytes(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
            buffer.ReaderIndex = Math.Min(reader + 1, 8);
            buffer.MarkReaderIndex();
            buffer.ReaderIndex = reader;
            buffer.WriterIndex = Math.Max(reader, 7);
            buffer.MarkWriterIndex();
            buffer.WriterIndex = 8;
            Assert.Same(buffer, buffer.DiscardSomeReadBytes());
            bool compact = reader >= 4;
            Assert.Equal(0, calls.Full);
            Assert.Equal(reader is 4 or 5 ? 1 : 0, calls.Copy);
            if (calls.Copy != 0) Assert.Equal((0, reader, 8 - reader), calls.Range);
            Assert.Equal(compact ? 0 : reader, buffer.ReaderIndex);
            Assert.Equal(compact ? 8 - reader : 8, buffer.WriterIndex);
            Assert.Equal(8, buffer.Capacity);
            var content = new byte[buffer.ReadableBytes];
            buffer.GetBytes(buffer.ReaderIndex, content);
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }[reader..], content);
            buffer.ResetReaderIndex();
            Assert.Equal(compact ? reader == 8 ? 0 : 1 : reader + 1, buffer.ReaderIndex);
            buffer.ResetWriterIndex();
            Assert.Equal(compact ? Math.Max(reader, 7) - reader : 7, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount);
        }
        finally { Assert.True(buffer.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCopyDoesNotPublishIndexOrMarkerChanges(bool direct)
    {
        var native = new NativeMemoryAllocator(8);
        var calls = new Calls { ThrowOnCopy = true };
        ByteBuf buffer = direct ? new Direct(calls, native) : new Heap(calls);
        try
        {
            buffer.WriteBytes(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
            buffer.ReaderIndex = 5; buffer.MarkReaderIndex(); buffer.ReaderIndex = 4;
            buffer.WriterIndex = 7; buffer.MarkWriterIndex(); buffer.WriterIndex = 8;
            Assert.Same(calls.Error, Assert.Throws<Failure>(() => buffer.DiscardSomeReadBytes()));
            Assert.Equal(0, calls.Full);
            Assert.Equal(1, calls.Copy);
            Assert.Equal((0, 4, 4), calls.Range);
            Assert.Equal(4, buffer.ReaderIndex);
            Assert.Equal(8, buffer.WriterIndex);
            var content = new byte[8]; buffer.GetBytes(0, content);
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }, content);
            buffer.ResetReaderIndex(); Assert.Equal(5, buffer.ReaderIndex);
            buffer.ResetWriterIndex(); Assert.Equal(7, buffer.WriterIndex);
        }
        finally { Assert.True(buffer.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }
}
