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

public class ByteBufRelativeTransferDispatchTest
{
    private sealed class Failure : Exception;

    private sealed class Calls
    {
        public int Count, Index, Length, Mode;
        public ByteBuf Peer;
        public readonly Failure Error = new();

        public void Before(ByteBuf peer, int index, int length)
        {
            ++Count; Peer = peer; Index = index; Length = length;
            if (Mode is 1 or 5) throw Error;
        }

        public ByteBuf After(ByteBuf result)
        {
            if (Mode == 2) throw Error;
            return result;
        }
    }

    private sealed class Heap(Calls calls, int capacity) : UnpooledHeapByteBuf(capacity, 16)
    {
        public override ByteBuf ReadBytes(ByteBuf destination, int length)
        {
            if (calls.Mode < 4) return base.ReadBytes(destination, length);
            calls.Before(destination, -1, length); return destination;
        }

        public override ByteBuf WriteBytes(ByteBuf source, int length)
        {
            if (calls.Mode < 4) return base.WriteBytes(source, length);
            calls.Before(source, -1, length); return source;
        }

        public override ByteBuf ReadBytes(ByteBuf destination, int destinationIndex, int length)
        {
            calls.Before(destination, destinationIndex, length);
            return calls.Mode == 3 ? destination : calls.After(base.ReadBytes(destination, destinationIndex, length));
        }

        public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
        {
            calls.Before(source, sourceIndex, length);
            return calls.Mode == 3 ? source : calls.After(base.WriteBytes(source, sourceIndex, length));
        }
    }

    private sealed class Direct(Calls calls, int capacity, NativeMemoryAllocator native)
        : UnpooledDirectByteBuf(capacity, 16, native)
    {
        public override ByteBuf ReadBytes(ByteBuf destination, int length)
        {
            if (calls.Mode < 4) return base.ReadBytes(destination, length);
            calls.Before(destination, -1, length); return destination;
        }

        public override ByteBuf WriteBytes(ByteBuf source, int length)
        {
            if (calls.Mode < 4) return base.WriteBytes(source, length);
            calls.Before(source, -1, length); return source;
        }

        public override ByteBuf ReadBytes(ByteBuf destination, int destinationIndex, int length)
        {
            calls.Before(destination, destinationIndex, length);
            return calls.Mode == 3 ? destination : calls.After(base.ReadBytes(destination, destinationIndex, length));
        }

        public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
        {
            calls.Before(source, sourceIndex, length);
            return calls.Mode == 3 ? source : calls.After(base.WriteBytes(source, sourceIndex, length));
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool write in new[] { false, true })
        for (int scenario = 0; scenario < 9; ++scenario) yield return [direct, write, scenario];
    }

    // AbstractByteBuf's relative overload calls the indexed readBytes/writeBytes overload,
    // then commits the peer cursor only if that virtual operation returns successfully.
    [Theory]
    [MemberData(nameof(Cases))]
    public void RelativeTransferPreservesIndexedOverrideAndCursorCommitOrder(bool direct, bool write, int scenario)
    {
        int length = scenario switch { 0 => 0, 4 => 6, 5 => 8, 6 => 5, 7 => -1, _ => 2 };
        var calls = new Calls { Mode = scenario switch { 2 => 1, 3 => 2, 8 => 3, _ => 0 } };
        var native = new NativeMemoryAllocator();
        ByteBuf owner = direct ? new Direct(calls, write ? 4 : 8, native) : new Heap(calls, write ? 4 : 8);
        ByteBuf peer = new UnpooledHeapByteBuf(8, 16);
        ByteBuf source = write ? peer : owner, destination = write ? owner : peer;
        try
        {
            source.WriteBytes(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17 });
            source.SetIndex(2, 7).MarkReaderIndex().MarkWriterIndex();
            destination.SetByte(0, 99).SetIndex(0, 1).MarkReaderIndex().MarkWriterIndex();
            ByteBuf Invoke() => write ? owner.WriteBytes(peer, length) : owner.ReadBytes(peer, length);
            if (scenario is 2 or 3)
                Assert.Same(calls.Error, Assert.Throws<Failure>(() => Invoke()));
            else if (scenario is 4 or 5 or 7)
                Assert.Throws<ArgumentOutOfRangeException>(() => Invoke());
            else Assert.Same(owner, Invoke());

            bool dispatched = write ? scenario is not (4 or 5 or 7) : scenario != 5;
            Assert.Equal(dispatched ? 1 : 0, calls.Count);
            if (dispatched)
            {
                Assert.Same(peer, calls.Peer);
                Assert.Equal(write ? 2 : 1, calls.Index);
                Assert.Equal(length, calls.Length);
            }
            bool copied = scenario is 0 or 1 or 3 or 6;
            int copiedLength = copied ? length : 0;
            bool succeeded = scenario is 0 or 1 or 6 or 8;
            int peerLength = succeeded ? length : 0;
            Assert.Equal(2 + (write ? peerLength : copiedLength), source.ReaderIndex);
            Assert.Equal(7, source.WriterIndex);
            Assert.Equal(0, destination.ReaderIndex);
            Assert.Equal(1 + (write ? copiedLength : peerLength), destination.WriterIndex);
            Assert.Equal(write && scenario == 6 ? 16 : write ? 4 : 8, owner.Capacity);
            Assert.Equal(99, destination.GetByte(0));
            for (int i = 0; i < copiedLength; ++i) Assert.Equal(12 + i, destination.GetByte(1 + i));
            source.ResetReaderIndex().ResetWriterIndex();
            destination.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(2, source.ReaderIndex); Assert.Equal(7, source.WriterIndex);
            Assert.Equal(0, destination.ReaderIndex); Assert.Equal(1, destination.WriterIndex);
            Assert.Equal(1, source.ReferenceCount); Assert.Equal(1, destination.ReferenceCount);
        }
        finally { Assert.True(owner.Release()); Assert.True(peer.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public void WholeTransferPreservesReceiverReturnAndLengthOverride(bool direct, bool write, bool failure)
    {
        var calls = new Calls { Mode = failure ? 5 : 4 };
        var native = new NativeMemoryAllocator();
        ByteBuf owner = direct ? new Direct(calls, 8, native) : new Heap(calls, 8);
        ByteBuf peer = new UnpooledHeapByteBuf(8, 16);
        try
        {
            owner.SetIndex(2, 7); peer.SetIndex(1, 3);
            ByteBuf Invoke() => write ? owner.WriteBytes(peer) : owner.ReadBytes(peer);
            if (failure) Assert.Same(calls.Error, Assert.Throws<Failure>(() => Invoke()));
            else Assert.Same(owner, Invoke());
            Assert.Equal(1, calls.Count); Assert.Same(peer, calls.Peer);
            Assert.Equal(-1, calls.Index); Assert.Equal(write ? 2 : 5, calls.Length);
            Assert.Equal(2, owner.ReaderIndex); Assert.Equal(7, owner.WriterIndex);
            Assert.Equal(1, peer.ReaderIndex); Assert.Equal(3, peer.WriterIndex);
            Assert.Equal(1, owner.ReferenceCount); Assert.Equal(1, peer.ReferenceCount);
        }
        finally { Assert.True(owner.Release()); Assert.True(peer.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }
}
