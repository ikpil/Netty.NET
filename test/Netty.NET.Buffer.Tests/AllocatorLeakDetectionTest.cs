/*
 * Copyright 2017 The Netty Project
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
using System.Reflection;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Level is a process-wide policy. These tests exclude concurrent collections and restore it.
[CollectionDefinition(nameof(AllocatorLeakDetectionCollection), DisableParallelization = true)]
public class AllocatorLeakDetectionCollection { }

[Collection(nameof(AllocatorLeakDetectionCollection))]
public class AllocatorLeakDetectionTest : IDisposable
{
    private readonly ResourceLeakDetectorLevel _previous = ResourceLeakDetector.GetLevel();
    public AllocatorLeakDetectionTest() => ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.PARANOID);
    public void Dispose() => ResourceLeakDetector.SetLevel(_previous);

    // Inspection never replaces the public reference used for release.
    internal static ByteBuf UnwrapLeak(ByteBuf buffer)
    {
        while (buffer is SimpleLeakAwareByteBuf or SimpleLeakAwareCompositeByteBuf) buffer = buffer.Unwrap();
        return buffer;
    }
    internal static PooledByteBuf PooledStorage(ByteBuf buffer) => Assert.IsAssignableFrom<PooledByteBuf>(UnwrapLeak(buffer));
    internal static IResourceLeakTracker<ByteBuf> Tracker(ByteBuf buffer)
    {
        if (buffer is SimpleLeakAwareByteBuf)
            return (IResourceLeakTracker<ByteBuf>)typeof(SimpleLeakAwareByteBuf)
                .GetField("_leak", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(buffer);
        if (buffer is SimpleLeakAwareCompositeByteBuf)
            return (IResourceLeakTracker<ByteBuf>)typeof(SimpleLeakAwareCompositeByteBuf)
                .GetField("Leak", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(buffer);
        return null;
    }

    private static IByteBufAllocator Allocator(int kind, bool prefer, NativeMemoryAllocator native)
        => kind is 0 or 3 ? new UnpooledByteBufAllocator(prefer, kind == 3, native)
            : new PooledByteBufAllocator(prefer, poolHeap: kind == 1, poolDirect: kind == 1,
                pageSize: 4096, maxOrder: 1, nativeAllocator: native);

    public static IEnumerable<object[]> FactoryCases()
    {
        for (int kind = 0; kind < 4; ++kind)
            foreach (bool prefer in new[] { false, true })
                foreach (bool disabled in new[] { false, true })
                    for (int method = 0; method < 11; ++method) yield return [kind, prefer, disabled, method];
    }

    [Theory]
    [MemberData(nameof(FactoryCases))]
    public void OriginalAllocatorFactoryScopeEmptySentinelsAndStoragePolicy(int kind, bool prefer, bool disabled, int method)
    {
        if (disabled) ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.DISABLED);
        var native = new NativeMemoryAllocator(); IByteBufAllocator allocator = Allocator(kind, prefer, native);
        ByteBuf b = null;
        try
        {
            b = method switch
            {
                0 => allocator.Buffer(8, 64), 1 => allocator.HeapBuffer(8, 64),
                2 => allocator.DirectBuffer(8, 64), 3 => allocator.IoBuffer(8, 64),
                4 => allocator.CompositeBuffer(2), 5 => allocator.CompositeHeapBuffer(2),
                6 => allocator.CompositeDirectBuffer(2), 7 => allocator.HeapBuffer(0, 0),
                8 => allocator.DirectBuffer(0, 0), 9 => allocator.HeapBuffer(0, 64),
                _ => allocator.DirectBuffer(0, 64)
            };
            bool composite = method is >= 4 and <= 6, empty = method is 7 or 8;
            bool direct = method == 0 ? prefer : method is 2 or 3 or 10;
            bool tracked = !disabled && !empty && kind != 3 && (composite || kind != 0 || direct);
            Assert.Same(allocator, b.Allocator); Assert.Equal(1, b.ReferenceCount);
            Assert.Equal(tracked, Tracker(b) != null);
            if (tracked)
            {
                if (composite) Assert.IsType<AdvancedLeakAwareCompositeByteBuf>(b);
                else Assert.IsType<AdvancedLeakAwareByteBuf>(b);
                Assert.NotNull(b.Unwrap()); Assert.Null(b.Unwrap().Unwrap());
            }
            if (empty)
            {
                Assert.IsType<EmptyByteBuf>(b); Assert.Same(allocator.HeapBuffer(0, 0), b);
                Assert.Same(allocator.DirectBuffer(0, 0), b);
            }
            else if (composite)
            {
                var cb = (CompositeByteBuf)b;
                cb.AddComponent(Unpooled.WrappedBuffer(new byte[] { 1, 2 }), true);
                cb.Capacity = 4; Assert.Equal(2, cb.NumComponents);
                Assert.Equal(method == 4 ? prefer : method == 6, cb.Component(1).IsDirect);
                Assert.Same(allocator, cb.Component(1).Allocator);
            }
            else
            {
                Assert.Equal(direct, b.IsDirect); b.WriteLong(42); Assert.Equal(42, b.ReadLong());
            }
        }
        finally
        {
            if (b != null)
            {
                var tracker = Tracker(b); ByteBuf referent = b.Unwrap();
                b.Release(); if (tracker != null) Assert.False(tracker.Close(referent));
            }
            (allocator as IDisposable)?.Dispose();
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    public static IEnumerable<object[]> ViewCases()
    {
        for (int shape = 0; shape < 6; ++shape)
            for (int view = 0; view < 9; ++view) yield return [shape, view];
    }

    [Theory]
    [MemberData(nameof(ViewCases))]
    public void AutomaticallyTrackedViewsRespectSharedOrIndependentOwnership(int shape, int method)
    {
        var native = new NativeMemoryAllocator(); IByteBufAllocator allocator = Allocator(shape == 0 ? 0 : shape < 3 ? 1 : 2, false, native);
        ByteBuf root = shape == 5 ? allocator.CompositeHeapBuffer().AddComponent(Unpooled.Buffer(16, 16).WriteZero(16), true)
            : shape is 0 or 2 or 4 ? allocator.DirectBuffer(16, 64) : allocator.HeapBuffer(16, 64);
        root.SetIndex(0, 0); for (int i = 0; i < 16; ++i) root.WriteByte(i);
        ByteBuf view = null; var tracker = Tracker(root); ByteBuf referent = root.Unwrap();
        bool retained = method is >= 5;
        try
        {
            view = method switch
            {
                0 => root.Slice(), 1 => root.Slice(3, 8), 2 => root.Duplicate(),
                3 => root.ReadSlice(8), 4 => root.AsReadOnly(), 5 => root.RetainedSlice(),
                6 => root.RetainedSlice(3, 8), 7 => root.RetainedDuplicate(), _ => root.ReadRetainedSlice(8)
            };
            Assert.IsType<AdvancedLeakAwareByteBuf>(view);
            bool independent = retained && shape is 1 or 2;
            var viewTracker = Tracker(view); ByteBuf viewReferent = view.Unwrap();
            if (independent) { Assert.NotSame(tracker, viewTracker); Assert.Equal(1, view.ReferenceCount); }
            else Assert.Same(tracker, viewTracker);
            Assert.Equal(retained ? 2 : 1, root.ReferenceCount);
            Assert.Equal(!retained, root.Release());
            if (retained)
            {
                Assert.Equal(method == 6 ? 3 : 0, view.GetByte(0)); Assert.True(view.Release());
                if (independent) Assert.False(viewTracker.Close(viewReferent));
            }
            else Assert.Throws<IllegalReferenceCountException>(() => view.GetByte(0));
            Assert.False(tracker.Close(referent));
        }
        finally
        {
            if (view != null && retained && view.ReferenceCount > 0) view.Release();
            if (root.ReferenceCount > 0) root.Release(root.ReferenceCount);
            (allocator as IDisposable)?.Dispose();
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalPublicFactoryOverridesDispatchThroughBaseAndInterface(bool preferDirect)
    {
        var native = new NativeMemoryAllocator(); var probe = new DispatchAllocator(preferDirect, native);
        AbstractByteBufAllocator a = probe; IByteBufAllocator contract = probe;
        ByteBuf[] buffers = [a.Buffer(8, 64), contract.Buffer(8, 64), a.IoBuffer(8, 64),
            contract.HeapBuffer(8, 64), contract.DirectBuffer(8, 64), a.CompositeBuffer(2), contract.CompositeBuffer(2)];
        try
        {
            Assert.Equal(2, probe.BufferCalls); Assert.Equal(1, probe.IoCalls); Assert.Equal(2, probe.CompositeCalls);
            Assert.Equal(preferDirect ? 1 : 3, probe.HeapCalls); Assert.Equal(preferDirect ? 4 : 2, probe.DirectCalls);
            Assert.All(buffers, b => Assert.Same(probe, b.Allocator));
            Assert.Equal(preferDirect, buffers[0].IsDirect); Assert.True(buffers[2].IsDirect);
            Assert.False(buffers[3].IsDirect); Assert.True(buffers[4].IsDirect);
            Assert.IsType<AdvancedLeakAwareCompositeByteBuf>(buffers[5]);
            Assert.IsType<AdvancedLeakAwareCompositeByteBuf>(buffers[6]);
        }
        finally { foreach (ByteBuf b in buffers) b.Release(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    private sealed class DispatchAllocator(bool preferDirect, NativeMemoryAllocator native) : AbstractByteBufAllocator(preferDirect)
    {
        internal int BufferCalls, IoCalls, HeapCalls, DirectCalls, CompositeCalls;
        public override bool IsDirectBufferPooled => false;
        public override ByteBuf Buffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        { BufferCalls++; return base.Buffer(initialCapacity, maxCapacity); }
        public override ByteBuf IoBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        { IoCalls++; return base.IoBuffer(initialCapacity, maxCapacity); }
        public override ByteBuf HeapBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        { HeapCalls++; return base.HeapBuffer(initialCapacity, maxCapacity); }
        public override ByteBuf DirectBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        { DirectCalls++; return base.DirectBuffer(initialCapacity, maxCapacity); }
        public override CompositeByteBuf CompositeBuffer(int maxNumComponents = 16)
        { CompositeCalls++; return base.CompositeBuffer(maxNumComponents); }
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
            => new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity);
        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
            => new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity, native);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CopiesAcquireIndependentTrackingThroughCreatorAllocator(bool pooled)
    {
        var native = new NativeMemoryAllocator(); var allocator = Allocator(pooled ? 1 : 0, true, native);
        ByteBuf root = allocator.Buffer(8, 64).WriteLong(42), copy = null;
        try
        {
            copy = root.Copy(); Assert.Same(allocator, copy.Allocator);
            Assert.IsType<AdvancedLeakAwareByteBuf>(copy); Assert.NotSame(Tracker(root), Tracker(copy));
            Assert.True(root.Release()); Assert.Equal(42, copy.ReadLong()); Assert.True(copy.Release());
        }
        finally
        {
            if (root.ReferenceCount > 0) root.Release(); if (copy?.ReferenceCount > 0) copy.Release();
            (allocator as IDisposable)?.Dispose();
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativePinPreservesStorageAfterLogicalTrackingCloses(bool pooled)
    {
        var native = new NativeMemoryAllocator(); var allocator = Allocator(pooled ? 1 : 0, true, native);
        ByteBuf b = allocator.DirectBuffer(8, 8).WriteLong(42); var tracker = Tracker(b); var referent = b.Unwrap();
        var pin = b.AsMemory(0, 8).Pin();
        try
        {
            Assert.True(b.Release()); Assert.False(tracker.Close(referent));
            (allocator as IDisposable)?.Dispose(); Assert.True(native.ReservedBytes > 0);
            if (allocator is UnpooledByteBufAllocator unpooled) Assert.Equal(0, unpooled.Metric.UsedDirectMemory);
        }
        finally { if (b.ReferenceCount > 0) b.Release(); (allocator as IDisposable)?.Dispose(); pin.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }
}
