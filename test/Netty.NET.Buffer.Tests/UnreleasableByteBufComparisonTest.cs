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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class UnreleasableByteBufComparisonTest
{
    private sealed class Wrapped(ByteBuf buffer) : WrappedByteBuf(buffer);
    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        public int Records, Closes;
        public void Record() => ++Records;
        public void Record(object hint) => ++Records;
        public bool Close(ByteBuf value) { ++Closes; return true; }
    }

    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly Tracker Tracker = new();
        public readonly ByteBuf Root, Facade;
        private readonly ByteBuf _tracked;
        private readonly int _expectedCloses;
        public Storage(int backing, int parent)
        {
            byte[] bytes = [128, 1, 2, 3, 4, 5, 6, 7, 8, 255];
            Root = backing switch
            {
                0 => new UnpooledHeapByteBuf(10, 32).WriteBytes(bytes),
                1 => new UnpooledDirectByteBuf(10, 32, Native).WriteBytes(bytes),
                _ => new CompositeByteBuf(16, false, Native)
                    .AddComponent(Unpooled.WrappedBuffer(bytes[..5]), true)
                    .AddComponent(new UnpooledDirectByteBuf(5, 5, Native).WriteBytes(bytes[5..]), true)
            };
            Root.SetIndex(1, 9).MarkReaderIndex().MarkWriterIndex();
            _expectedCloses = parent == 0 ? 0 : 1;
            _tracked = parent == 0 ? Root : backing == 2
                ? new AdvancedLeakAwareCompositeByteBuf((CompositeByteBuf)Root, Tracker)
                : new AdvancedLeakAwareByteBuf(Root, Tracker);
            ByteBuf view = parent switch
            {
                2 => new Wrapped(_tracked),
                3 => Unpooled.UnreleasableBuffer(_tracked),
                _ => _tracked
            };
            Facade = Unpooled.UnreleasableBuffer(view);
        }
        public void Dispose()
        {
            if (Root.ReferenceCount > 0) Assert.True(_tracked.Release());
            Assert.Equal(0, Root.ReferenceCount);
            Assert.Equal(_expectedCloses, Tracker.Closes);
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        for (int backing = 0; backing < 3; backing++)
        for (int parent = 0; parent < 4; parent++) yield return [backing, parent];
    }

    private static ByteBuf Peer() => Unpooled.WrappedBuffer(new byte[] { 99, 1, 2, 3, 4, 5, 6, 7, 8, 99 }).SetIndex(1, 9);

    [Theory]
    [MemberData(nameof(Cases))]
    public void InstanceComparisonsDelegateWithoutTurningThemIntoLeakAccessRecords(int backing, int parent)
    {
        using var storage = new Storage(backing, parent);
        ByteBuf peer = Peer(), facade = storage.Facade;
        try
        {
            Assert.Equal(608462917, facade.GetHashCode());
            Assert.True(facade.Equals(peer));
            Assert.True(facade.Equals((object)peer));
            Assert.True(((IEquatable<ByteBuf>)facade).Equals(peer));
            Assert.Equal(0, facade.CompareTo(peer));
            Assert.False(facade.Equals((ByteBuf)null));
            Assert.False(facade.Equals((object)null));
            Assert.False(facade.Equals(new object()));
            Assert.Equal(1, facade.CompareTo(null));
            peer.SetByte(1, 9); Assert.True(facade.CompareTo(peer) < 0);
            peer.SetByte(1, 0); Assert.True(facade.CompareTo(peer) > 0);
            peer.SetByte(1, 1);
            var set = new HashSet<ByteBuf> { facade };
            var dictionary = new Dictionary<ByteBuf, string> { [facade] = "value" };
            Assert.Contains(peer, set);
            Assert.Equal("value", dictionary[peer]);
            set.Clear(); dictionary.Clear();
            Assert.Equal(0, storage.Tracker.Records);
            Assert.Equal(0, storage.Tracker.Closes);
            Assert.Equal(1, storage.Root.ReferenceCount);
            Assert.False(facade.Release());
            Assert.Equal(1, storage.Root.ReaderIndex);
            Assert.Equal(9, storage.Root.WriterIndex);
            storage.Root.SetIndex(0, 10).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, storage.Root.ReaderIndex);
            Assert.Equal(9, storage.Root.WriterIndex);
        }
        finally { peer.Release(); }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ParentIdentitySurvivesReleaseWhileDistinctContentAccessStillRequiresOwnership(int backing, int parent)
    {
        using var storage = new Storage(backing, parent);
        ByteBuf peer = Peer(), facade = storage.Facade;
        try
        {
            storage.Dispose();
            Assert.True(facade.Equals(storage.Root));
            Assert.True(facade.Equals((object)storage.Root));
            Assert.Equal(0, facade.CompareTo(storage.Root));
            // CLR reflexive identity and null ordering follow the existing WrappedByteBuf policy.
            Assert.True(facade.Equals(facade));
            Assert.True(facade.Equals((object)facade));
            Assert.Equal(0, facade.CompareTo(facade));
            Assert.Equal(1, facade.CompareTo(null));
            Assert.False(facade.Equals((ByteBuf)null));
            Assert.False(facade.Equals(new object()));
            Assert.Throws<IllegalReferenceCountException>(() => facade.GetHashCode());
            Assert.Throws<IllegalReferenceCountException>(() => facade.Equals(peer));
            Assert.Throws<IllegalReferenceCountException>(() => facade.CompareTo(peer));
            Assert.Equal(0, facade.ReferenceCount);
            Assert.False(facade.Release());
        }
        finally { peer.Release(); }
    }

    private sealed class ObservedHeap : UnpooledHeapByteBuf
    {
        public int HashCalls, ObjectCalls, TypedCalls, CompareCalls;
        public ObservedHeap() : base(10, 32)
        { WriteBytes(new byte[] { 128, 1, 2, 3, 4, 5, 6, 7, 8, 255 }); SetIndex(1, 9); }
        public override int GetHashCode() { ++HashCalls; return base.GetHashCode(); }
        public override bool Equals(object other) { ++ObjectCalls; return base.Equals(other); }
        public override bool Equals(ByteBuf other) { ++TypedCalls; return base.Equals(other); }
        public override int CompareTo(ByteBuf other) { ++CompareCalls; return base.CompareTo(other); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void EachComparisonSurfaceReachesTheParentOverride(int operation)
    {
        var root = new ObservedHeap();
        ByteBuf facade = Unpooled.UnreleasableBuffer(root), peer = Peer();
        try
        {
            switch (operation)
            {
                case 0: Assert.Equal(608462917, facade.GetHashCode()); break;
                case 1: Assert.True(facade.Equals(peer)); break;
                case 2: Assert.True(facade.Equals((object)peer)); break;
                case 3: Assert.Equal(0, facade.CompareTo(peer)); break;
            }
            Assert.Equal(operation == 0 ? 1 : 0, root.HashCalls);
            Assert.Equal(operation == 2 ? 1 : 0, root.ObjectCalls);
            Assert.Equal(operation is 1 or 2 ? 1 : 0, root.TypedCalls);
            Assert.Equal(operation == 3 ? 1 : 0, root.CompareCalls);
        }
        finally { root.Release(); peer.Release(); }
    }
}
