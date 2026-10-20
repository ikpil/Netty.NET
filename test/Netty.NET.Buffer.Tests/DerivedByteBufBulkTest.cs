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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class DerivedByteBufBulkTest
{
    private sealed record Visit(string Method, int Index, int Length, ByteBuf Peer, int PeerIndex);
    private sealed class Observation
    {
        public readonly List<Visit> Visits = new();
        public bool Fail;
        public void Record(string method, int index, int length, ByteBuf peer = null, int peerIndex = 0)
        {
            Visits.Add(new(method, index, length, peer, peerIndex));
            if (Fail) throw new IOException("parent policy");
        }
    }
    private sealed class ObservedHeap(Observation o) : UnpooledHeapByteBuf(16, 64)
    {
        public override ByteBuf GetBytes(int index, Span<byte> destination)
        {
            o.Record("get-span", index, destination.Length);
            return base.GetBytes(index, destination);
        }
        public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
        {
            o.Record("set-span", index, source.Length);
            return base.SetBytes(index, source);
        }
        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            o.Record("get-buffer", index, length, destination, destinationIndex);
            return base.GetBytes(index, destination, destinationIndex, length);
        }
        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        {
            o.Record("set-buffer", index, length, source, sourceIndex);
            return base.SetBytes(index, source, sourceIndex, length);
        }
    }
    private sealed class ObservedDirect(Observation o, NativeMemoryAllocator native) : UnpooledDirectByteBuf(16, 64, native)
    {
        public override ByteBuf GetBytes(int index, Span<byte> destination)
        {
            o.Record("get-span", index, destination.Length);
            return base.GetBytes(index, destination);
        }
        public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
        {
            o.Record("set-span", index, source.Length);
            return base.SetBytes(index, source);
        }
        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            o.Record("get-buffer", index, length, destination, destinationIndex);
            return base.GetBytes(index, destination, destinationIndex, length);
        }
        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        {
            o.Record("set-buffer", index, length, source, sourceIndex);
            return base.SetBytes(index, source, sourceIndex, length);
        }
    }
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly Observation Observation = new();
        public readonly ByteBuf Root, View, Owner;
        public readonly int Offset;
        public Storage(bool direct, int kind)
        {
            Root = direct ? new ObservedDirect(Observation, Native) : new ObservedHeap(Observation);
            for (int i = 0; i < 16; ++i) Root.WriteByte(i);
            Root.SetIndex(2, 14).MarkReaderIndex().MarkWriterIndex();
            if (kind is >= 5 and <= 9)
                Owner = kind is 6 or 8 ? PooledDerivedByteBuf.NewDuplicate(Root, Root, 2, 14)
                    : PooledDerivedByteBuf.NewSlice(Root, Root, 3, 10);
            View = kind switch
            {
                0 => Root.Duplicate(),
                1 => Root.Slice(3, 10),
                2 => Root.AsReadOnly(),
                3 => Root.Slice(3, 10).Slice(2, 6),
                4 => Root.Slice(3, 10).AsReadOnly(),
                5 or 6 => Owner,
                7 => Owner.Slice(2, 6),
                8 or 9 => Owner.Duplicate(),
                _ => Root.Slice(0, 16)
            };
            Offset = kind is 3 or 7 ? 5 : kind is 1 or 4 or 5 or 9 ? 3 : 0;
            View.SetIndex(Offset == 0 ? 3 : 1, Offset == 0 ? 13 : View.Capacity - 1)
                .MarkReaderIndex().MarkWriterIndex();
        }
        public void Dispose()
        {
            if (Owner?.ReferenceCount > 0) Assert.True(Owner.Release());
            if (Root.ReferenceCount > 0) Assert.True(Root.Release());
            Assert.Equal(0, Native.ReservedBytes);
        }
    }


    private sealed class Peer : IDisposable
    {
        public readonly byte[] Bytes = Enumerable.Range(100, 12).Select(i => (byte)i).ToArray();
        private readonly ByteBuf _owner;
        public readonly ByteBuf Buffer;
        public Peer(int storage, bool input, NativeMemoryAllocator native)
        {
            _owner = storage switch
            {
                0 => null,
                1 => new UnpooledHeapByteBuf(12, 12),
                2 => new UnpooledDirectByteBuf(12, 12, native),
                _ => new CompositeByteBuf()
                    .AddComponent(new UnpooledHeapByteBuf(5, 5).WriteBytes(Bytes.AsSpan(0, 5)), true)
                    .AddComponent(new UnpooledHeapByteBuf(7, 7).WriteBytes(Bytes.AsSpan(5)), true)
            };
            if (_owner == null) return;
            _owner.SetBytes(0, Bytes);
            Buffer = storage == 2 && input ? _owner.AsReadOnly() : _owner;
            Buffer.SetIndex(2, 9).MarkReaderIndex().MarkWriterIndex();
        }
        public byte[] Snapshot() => Buffer == null ? Bytes.ToArray() : ByteBufUtil.GetBytes(Buffer, 0, 12);
        public void Dispose() { if (_owner != null) Assert.True(_owner.Release()); }
    }
    private static byte[] Snapshot(Storage s) => s.Root.AsReadOnlySpan(0, 16).ToArray();
    private static ByteBuf Transfer(ByteBuf b, Peer p, bool input, bool relative, int index = 2, int peerIndex = 4, int length = 3)
    {
        if (p.Buffer == null)
        {
            if (input) return relative ? b.WriteBytes(p.Bytes.AsSpan(peerIndex, length)) : b.SetBytes(index, p.Bytes.AsSpan(peerIndex, length));
            return relative ? b.ReadBytes(p.Bytes.AsSpan(peerIndex, length)) : b.GetBytes(index, p.Bytes.AsSpan(peerIndex, length));
        }
        if (input) return relative ? b.WriteBytes(p.Buffer, length) : b.SetBytes(index, p.Buffer, peerIndex, length);
        return relative ? b.ReadBytes(p.Buffer, length) : b.GetBytes(index, p.Buffer, peerIndex, length);
    }
    private static void Observed(Storage s, Peer p, bool input, int index, int peerIndex, int length = 3)
    {
        Visit v = Assert.Single(s.Observation.Visits);
        Assert.Equal((input ? "set" : "get") + (p.Buffer == null ? "-span" : "-buffer"), v.Method);
        Assert.Equal(index, v.Index); Assert.Equal(length, v.Length);
        Assert.Same(p.Buffer, v.Peer); Assert.Equal(p.Buffer == null ? 0 : peerIndex, v.PeerIndex);
        Assert.Equal(2, s.Root.ReaderIndex); Assert.Equal(14, s.Root.WriterIndex);
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int kind = 0; kind < 11; kind++)
        foreach (bool input in new[] { false, true })
        for (int storage = 0; storage < 4; storage++) yield return [direct, kind, input, storage];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void BulkViewsPreserveParentPolicyPeerIdentityAndOwnCursors(bool direct, int kind, bool input, int storage)
    {
        using var s = new Storage(direct, kind);
        using var p = new Peer(storage, input, s.Native);
        s.View.SetIndex(1, 4).MarkReaderIndex().MarkWriterIndex(); s.Observation.Visits.Clear();
        byte[] expected = Snapshot(s), peer = p.Snapshot();
        if (input && s.View.IsReadOnly)
        {
            Assert.Throws<NotSupportedException>(() => Transfer(s.View, p, true, false));
            Assert.Throws<NotSupportedException>(() => Transfer(s.View, p, true, true));
            Assert.Empty(s.Observation.Visits); Assert.Equal(expected, Snapshot(s)); Assert.Equal(peer, p.Snapshot()); return;
        }
        int refs = s.Root.ReferenceCount;
        Assert.Same(s.View, Transfer(s.View, p, input, false));
        Observed(s, p, input, s.Offset + 2, 4);
        if (input) peer.AsSpan(4, 3).CopyTo(expected.AsSpan(s.Offset + 2));
        else expected.AsSpan(s.Offset + 2, 3).CopyTo(peer.AsSpan(4));
        Assert.Equal(expected, Snapshot(s)); Assert.Equal(peer, p.Snapshot());
        Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(4, s.View.WriterIndex);
        s.View.SetIndex(0, s.View.Capacity).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(4, s.View.WriterIndex);
        if (p.Buffer != null)
        {
            Assert.Equal(2, p.Buffer.ReaderIndex); Assert.Equal(9, p.Buffer.WriterIndex);
            p.Buffer.SetIndex(0, 12).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(2, p.Buffer.ReaderIndex); Assert.Equal(9, p.Buffer.WriterIndex);
        }
        s.View.SetIndex(1, input ? 3 : 4); s.Observation.Visits.Clear();
        int peerIndex = p.Buffer == null ? 4 : input ? 2 : 9;
        Assert.Same(s.View, Transfer(s.View, p, input, true));
        Observed(s, p, input, s.Offset + (input ? 3 : 1), peerIndex);
        if (input) peer.AsSpan(peerIndex, 3).CopyTo(expected.AsSpan(s.Offset + 3));
        else expected.AsSpan(s.Offset + 1, 3).CopyTo(peer.AsSpan(peerIndex));
        Assert.Equal(expected, Snapshot(s)); Assert.Equal(peer, p.Snapshot());
        Assert.Equal(input ? 1 : 4, s.View.ReaderIndex); Assert.Equal(input ? 6 : 4, s.View.WriterIndex);
        if (p.Buffer != null)
        {
            Assert.Equal(input ? 5 : 2, p.Buffer.ReaderIndex); Assert.Equal(input ? 9 : 12, p.Buffer.WriterIndex);
        }
        Assert.Equal(refs, s.Root.ReferenceCount);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            s.View.SetIndex(1, input ? 3 : 4); p.Buffer?.SetIndex(2, 9);
            s.Observation.Visits.Clear(); s.Observation.Fail = true;
            Assert.Throws<IOException>(() => Transfer(s.View, p, input, attempt == 1));
            Observed(s, p, input, s.Offset + (attempt == 0 ? 2 : input ? 3 : 1), attempt == 0 ? 4 : peerIndex);
            Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(input ? 3 : 4, s.View.WriterIndex);
            Assert.Equal(expected, Snapshot(s)); Assert.Equal(peer, p.Snapshot());
            if (p.Buffer != null) { Assert.Equal(2, p.Buffer.ReaderIndex); Assert.Equal(9, p.Buffer.WriterIndex); }
        }
        s.Observation.Fail = false; s.Observation.Visits.Clear();
        Assert.Throws<ArgumentOutOfRangeException>(() => Transfer(s.View, p, input, false, index: s.View.Capacity, length: 1));
        Assert.Empty(s.Observation.Visits);
        Assert.Same(s.View, Transfer(s.View, p, input, false, index: s.View.Capacity, peerIndex: 12, length: 0));
        Observed(s, p, input, s.Offset + s.View.Capacity, 12, 0);
        if (p.Buffer != null)
        {
            s.Observation.Visits.Clear();
            if (input) Assert.Throws<ArgumentNullException>(() => s.View.SetBytes(2, (ByteBuf)null, 0, 1));
            else Assert.Throws<ArgumentNullException>(() => s.View.GetBytes(2, (ByteBuf)null, 0, 1));
            Assert.Empty(s.Observation.Visits);
            // Peer validation belongs to the parent overload, as in the original delegates.
            Assert.Throws<ArgumentOutOfRangeException>(() => Transfer(s.View, p, input, false, peerIndex: 12, length: 1));
            Observed(s, p, input, s.Offset + 2, 12, 1);
            if (!input)
            {
                s.Observation.Visits.Clear();
                ByteBuf readOnly = p.Buffer.AsReadOnly();
                Assert.Throws<NotSupportedException>(() => s.View.GetBytes(2, readOnly, 4, 3));
                Visit rejected = Assert.Single(s.Observation.Visits);
                Assert.Same(readOnly, rejected.Peer); Assert.Equal(s.Offset + 2, rejected.Index);
                Assert.Equal("get-buffer", rejected.Method); Assert.Equal(4, rejected.PeerIndex);
            }
        }
        if (s.Owner != null)
        {
            s.Observation.Visits.Clear(); Assert.True(s.Owner.Release()); Assert.Equal(1, s.Root.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => Transfer(s.View, p, input, false));
            Assert.Empty(s.Observation.Visits);
        }
    }
    public static IEnumerable<object[]> AliasedCases()
    {
        foreach (bool direct in new[] { false, true })
        for (int kind = 0; kind < 11; kind++)
        foreach (bool input in new[] { false, true })
        foreach (bool buffer in new[] { false, true })
        foreach (bool forward in new[] { false, true }) yield return [direct, kind, input, buffer, forward];
    }
    [Theory]
    [MemberData(nameof(AliasedCases))]
    public void OverlappingBulkRangesKeepSnapshotBytesAndViewIdentity(bool direct, int kind, bool input, bool buffer, bool forward)
    {
        using var s = new Storage(direct, kind);
        s.View.SetIndex(1, 4); byte[] expected = Snapshot(s);
        int peerIndex = s.Offset + (forward ? 1 : 3);
        ByteBuf peer = buffer ? s.Root.Slice(peerIndex, 3) : null;
        s.Observation.Visits.Clear();
        ByteBuf Copy()
        {
            if (buffer) return input ? s.View.SetBytes(2, peer, 0, 3) : s.View.GetBytes(2, peer, 0, 3);
            return input ? s.View.SetBytes(2, s.Root.AsReadOnlySpan(peerIndex, 3))
                : s.View.GetBytes(2, s.Root.AsSpan(peerIndex, 3));
        }
        if (input && s.View.IsReadOnly)
        {
            Assert.Throws<NotSupportedException>(() => Copy()); Assert.Empty(s.Observation.Visits);
        }
        else
        {
            Assert.Same(s.View, Copy()); Assert.NotEmpty(s.Observation.Visits);
            Visit v = s.Observation.Visits[0];
            Assert.Equal((input ? "set" : "get") + (buffer ? "-buffer" : "-span"), v.Method);
            Assert.Equal(s.Offset + 2, v.Index); Assert.Equal(3, v.Length); Assert.Same(peer, v.Peer);
            byte[] snapshot = expected.AsSpan(input ? peerIndex : s.Offset + 2, 3).ToArray();
            snapshot.CopyTo(expected.AsSpan(input ? s.Offset + 2 : peerIndex));
        }
        Assert.Equal(expected, Snapshot(s)); Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(4, s.View.WriterIndex);
        Assert.Equal(2, s.Root.ReaderIndex); Assert.Equal(14, s.Root.WriterIndex);
        Assert.Equal(s.Owner == null ? 1 : 2, s.Root.ReferenceCount);
    }
}
