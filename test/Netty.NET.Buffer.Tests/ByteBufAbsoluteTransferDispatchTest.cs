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

public class ByteBufAbsoluteTransferDispatchTest
{
    private sealed class Failure : Exception;
    private sealed class Calls
    {
        public int Count, Index, PeerIndex, Length, FailureMode;
        public bool Whole, SkipCopy;
        public ByteBuf Peer;
        public readonly Failure Error = new();
        public void Before(int index, ByteBuf peer, int peerIndex, int length)
        {
            ++Count; Index = index; Peer = peer; PeerIndex = peerIndex; Length = length;
            if (FailureMode == 1) throw Error;
        }
        public ByteBuf After(ByteBuf result)
        {
            if (FailureMode == 2) throw Error;
            return result;
        }
    }

    private sealed class Heap(Calls calls) : UnpooledHeapByteBuf(8, 16)
    {
        public override ByteBuf GetBytes(int index, ByteBuf destination, int length)
        {
            if (!calls.Whole) return base.GetBytes(index, destination, length);
            calls.Before(index, destination, -1, length); return destination;
        }
        public override ByteBuf SetBytes(int index, ByteBuf source, int length)
        {
            if (!calls.Whole) return base.SetBytes(index, source, length);
            calls.Before(index, source, -1, length); return source;
        }
        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            calls.Before(index, destination, destinationIndex, length);
            return calls.SkipCopy ? destination : calls.After(base.GetBytes(index, destination, destinationIndex, length));
        }
        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        {
            calls.Before(index, source, sourceIndex, length);
            return calls.SkipCopy ? source : calls.After(base.SetBytes(index, source, sourceIndex, length));
        }
    }

    private sealed class Direct(Calls calls, NativeMemoryAllocator native) : UnpooledDirectByteBuf(8, 16, native)
    {
        public override ByteBuf GetBytes(int index, ByteBuf destination, int length)
        {
            if (!calls.Whole) return base.GetBytes(index, destination, length);
            calls.Before(index, destination, -1, length); return destination;
        }
        public override ByteBuf SetBytes(int index, ByteBuf source, int length)
        {
            if (!calls.Whole) return base.SetBytes(index, source, length);
            calls.Before(index, source, -1, length); return source;
        }
        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            calls.Before(index, destination, destinationIndex, length);
            return calls.SkipCopy ? destination : calls.After(base.GetBytes(index, destination, destinationIndex, length));
        }
        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        {
            calls.Before(index, source, sourceIndex, length);
            return calls.SkipCopy ? source : calls.After(base.SetBytes(index, source, sourceIndex, length));
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool set in new[] { false, true })
        for (int scenario = 0; scenario < 11; ++scenario) yield return [direct, set, scenario];
    }

    // AbstractByteBuf.setBytes(index, src, length) validates its own range before
    // dispatching to the indexed copy, and commits the peer cursor only after success.
    [Theory]
    [MemberData(nameof(Cases))]
    public void AbsoluteTransferPreservesRangeGuardDispatchAndPeerCommit(bool direct, bool set, int scenario)
    {
        int length = scenario switch { 0 or 5 => 0, 8 => 6, 9 => -1, _ => 2 };
        int index = scenario switch { 4 => 7, 5 => 9, 6 => -1, 7 => int.MaxValue, _ => 1 };
        var calls = new Calls { FailureMode = scenario switch { 2 => 1, 3 => 2, _ => 0 }, SkipCopy = scenario == 10 };
        var native = new NativeMemoryAllocator();
        ByteBuf owner = direct ? new Direct(calls, native) : new Heap(calls);
        ByteBuf peer = new UnpooledHeapByteBuf(8, 16);
        ByteBuf source = set ? peer : owner, destination = set ? owner : peer;
        try
        {
            source.WriteBytes(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17 });
            destination.WriteBytes(new byte[] { 90, 91, 92, 93, 94, 95, 96, 97 });
            source.SetIndex(2, 7).MarkReaderIndex().MarkWriterIndex();
            destination.SetIndex(1, 3).MarkReaderIndex().MarkWriterIndex();
            ByteBuf Invoke() => set ? owner.SetBytes(index, peer, length) : owner.GetBytes(index, peer, length);
            if (scenario is 2 or 3) Assert.Same(calls.Error, Assert.Throws<Failure>(() => Invoke()));
            else if (scenario is >= 4 and <= 9) Assert.Throws<ArgumentOutOfRangeException>(() => Invoke());
            else Assert.Same(owner, Invoke());

            bool dispatched = !set || scenario is not (>= 4 and <= 9);
            Assert.Equal(dispatched ? 1 : 0, calls.Count);
            if (dispatched)
            {
                Assert.Same(peer, calls.Peer); Assert.Equal(index, calls.Index);
                Assert.Equal(set ? 2 : 3, calls.PeerIndex); Assert.Equal(length, calls.Length);
            }
            bool succeeded = scenario is 0 or 1 or 10;
            Assert.Equal(2 + (set && succeeded ? length : 0), source.ReaderIndex);
            Assert.Equal(7, source.WriterIndex); Assert.Equal(1, destination.ReaderIndex);
            Assert.Equal(3 + (!set && succeeded ? length : 0), destination.WriterIndex);
            byte[] expected = [90, 91, 92, 93, 94, 95, 96, 97];
            if (scenario is 1 or 3)
                Array.Copy(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17 }, set ? 2 : 1, expected, set ? 1 : 3, 2);
            Assert.Equal(expected, ByteBufUtil.GetBytes(destination, 0, 8));
            Assert.Equal(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17 }, ByteBufUtil.GetBytes(source, 0, 8));
            Assert.Equal(8, owner.Capacity); Assert.Equal(8, peer.Capacity);
            source.ResetReaderIndex().ResetWriterIndex(); destination.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(2, source.ReaderIndex); Assert.Equal(7, source.WriterIndex);
            Assert.Equal(1, destination.ReaderIndex); Assert.Equal(3, destination.WriterIndex);
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
    public void WholeTransferReturnsReceiverAndPreservesLengthOverride(bool direct, bool set, bool failure)
    {
        var calls = new Calls { Whole = true, FailureMode = failure ? 1 : 0 };
        var native = new NativeMemoryAllocator();
        ByteBuf owner = direct ? new Direct(calls, native) : new Heap(calls);
        ByteBuf peer = new UnpooledHeapByteBuf(8, 16);
        try
        {
            owner.SetIndex(2, 7); peer.SetIndex(1, 3);
            ByteBuf Invoke() => set ? owner.SetBytes(2, peer) : owner.GetBytes(2, peer);
            if (failure) Assert.Same(calls.Error, Assert.Throws<Failure>(() => Invoke()));
            else Assert.Same(owner, Invoke());
            Assert.Equal(1, calls.Count); Assert.Same(peer, calls.Peer);
            Assert.Equal(2, calls.Index); Assert.Equal(-1, calls.PeerIndex); Assert.Equal(set ? 2 : 5, calls.Length);
            Assert.Equal(2, owner.ReaderIndex); Assert.Equal(7, owner.WriterIndex);
            Assert.Equal(1, peer.ReaderIndex); Assert.Equal(3, peer.WriterIndex);
            Assert.Equal(1, owner.ReferenceCount); Assert.Equal(1, peer.ReferenceCount);
        }
        finally { Assert.True(owner.Release()); Assert.True(peer.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReleasedDestinationRejectsTransferBeforeCallingCopyOverride(bool direct)
    {
        var calls = new Calls { SkipCopy = true };
        var native = new NativeMemoryAllocator();
        ByteBuf owner = direct ? new Direct(calls, native) : new Heap(calls);
        ByteBuf source = new UnpooledHeapByteBuf(8, 16).WriteZero(8).SetIndex(2, 7);
        Assert.True(owner.Release());
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() => owner.SetBytes(1, source, 2));
            Assert.Equal(0, calls.Count); Assert.Equal(2, source.ReaderIndex);
            Assert.Equal(7, source.WriterIndex); Assert.Equal(1, source.ReferenceCount);
        }
        finally { Assert.True(source.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }
}
