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
using System.Globalization;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufDiagnosticsTest
{
    public static IEnumerable<object[]> StateCases()
    {
        for (int kind = 0; kind < 6; ++kind)
            for (int limit = 0; limit < (kind < 4 ? 2 : 1); ++limit)
                for (int view = 0; view < 4; ++view) yield return [kind, limit == 0, view];
    }

    [Theory]
    [MemberData(nameof(StateCases))]
    public void OriginalMetadataViewsAndFreedRepresentations(int kind, bool bounded, int viewKind)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf owner = Owner(kind, bounded ? 64 : int.MaxValue, native, out var allocator);
        ByteBuf root = AllocatorLeakDetectionTest.UnwrapLeak(owner);
        string[] names = ["UnpooledHeapByteBuf", "UnpooledDirectByteBuf", "PooledHeapByteBuf", "PooledDirectByteBuf", "CompositeByteBuf", "FixedCompositeByteBuf"];
        string maximum = kind < 4 && bounded ? "/64" : "";
        string viewMaximum = kind == 5 ? "/16" : maximum;
        string components = kind >= 4 ? ", components=2" : "";
        string rootText = names[kind] + "(ridx: 3, widx: 13, cap: 16" + maximum + components + ")";
        try
        {
            root.SetIndex(3, 13).MarkReaderIndex().MarkWriterIndex();
            ByteBuf view = viewKind switch { 0 => root, 1 => root.Slice(2, 8), 2 => root.Duplicate(), _ => root.AsReadOnly() };
            string expected = viewKind switch
            {
                0 => rootText,
                1 => "ByteBufView(ridx: 0, widx: 8, cap: 8/8, unwrapped: " + rootText + ")",
                2 => "ByteBufView(ridx: 3, widx: 13, cap: 16" + viewMaximum + ", unwrapped: " + rootText + ")",
                _ => "ReadOnlyByteBuf(ridx: 3, widx: 13, cap: 16" + maximum + ", unwrapped: " + rootText + ")"
            };
            if (kind == 5 && viewKind == 3) expected = rootText; // Already read-only: AsReadOnly returns itself.
            Assert.Equal(expected, view.ToString()); Assert.Equal(expected, ((object)view).ToString());
            Assert.Equal(3, root.ReaderIndex); Assert.Equal(13, root.WriterIndex); Assert.Equal(1, root.ReferenceCount);
            root.SetIndex(4, 14); root.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(rootText, root.ToString());
            Assert.True(owner.Release());
            string freed = viewKind == 0 || kind == 5 && viewKind == 3 ? names[kind] + "(freed" + components + ")"
                : viewKind == 3 ? "ReadOnlyByteBuf(freed)" : "ByteBufView(freed)";
            Assert.Equal(freed, view.ToString());
        }
        finally { if (owner.ReferenceCount > 0) owner.Release(); allocator?.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    private static ByteBuf Owner(int kind, int max, NativeMemoryAllocator native, out IDisposable disposable)
    {
        disposable = null; var a = new UnpooledByteBufAllocator(false, native);
        if (kind == 0) return new UnpooledHeapByteBuf(a, 16, max);
        if (kind == 1) return new UnpooledDirectByteBuf(a, 16, max, native);
        if (kind is 2 or 3)
        {
            var pooled = new PooledByteBufAllocator(false, maxOrder: 1, nativeAllocator: native); disposable = pooled;
            return kind == 2 ? pooled.HeapBuffer(16, max) : pooled.DirectBuffer(16, max);
        }
        ByteBuf first = Unpooled.WrappedBuffer(new byte[8]), second = Unpooled.WrappedBuffer(new byte[8]);
        return kind == 4 ? new CompositeByteBuf(a, false, 4).AddComponents([first, second], true) : Unpooled.WrappedUnmodifiableBuffer(first, second);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void OrdinaryWrappersShowTheirTypeAndWrappedDiagnosticWithoutRecording(int kind)
    {
        ByteBuf root = new UnpooledHeapByteBuf(16, 64); root.SetIndex(3, 13); var tracker = new Tracker();
        ByteBuf wrapper = kind switch
        {
            0 => new PlainWrapper(root), 1 => new SimpleLeakAwareByteBuf(root, tracker),
            2 => new AdvancedLeakAwareByteBuf(root, tracker), 3 => Unpooled.UnreleasableBuffer(root),
            _ => new SimpleLeakAwareByteBuf(new AdvancedLeakAwareByteBuf(root, tracker), new Tracker())
        };
        string name = kind switch { 0 => "ByteBufDiagnosticsTest+PlainWrapper", 1 => "SimpleLeakAwareByteBuf", 2 => "AdvancedLeakAwareByteBuf", 3 => "UnreleasableByteBuf", _ => "SimpleLeakAwareByteBuf" };
        string Parent(string raw) => kind == 4 ? "AdvancedLeakAwareByteBuf(" + raw + ")" : raw;
        try
        {
            Assert.Equal(name + '(' + Parent("UnpooledHeapByteBuf(ridx: 3, widx: 13, cap: 16/64)") + ')', wrapper.ToString());
            Assert.Equal(0, tracker.Records); Assert.Equal(0, tracker.Closes);
            if (kind == 3) root.Release(); else wrapper.Release();
            Assert.Equal(name + '(' + Parent("UnpooledHeapByteBuf(freed)") + ')', wrapper.ToString());
            Assert.Equal(kind is 2 or 4 ? 1 : 0, tracker.Records); // Only advanced Release records.
        }
        finally { if (root.ReferenceCount > 0) wrapper.Release(); if (root.ReferenceCount > 0) root.Release(); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void CompositeWrappersDelegateWithoutAddingWrapperNamesOrAccessRecords(bool advanced, bool nested)
    {
        var root = new CompositeByteBuf().AddComponents([Unpooled.WrappedBuffer(new byte[8]), Unpooled.WrappedBuffer(new byte[8])], true);
        root.SetIndex(3, 13); var tracker = new Tracker();
        CompositeByteBuf wrapper = advanced ? new AdvancedLeakAwareCompositeByteBuf(root, tracker) : new SimpleLeakAwareCompositeByteBuf(root, tracker);
        if (nested) wrapper = new AdvancedLeakAwareCompositeByteBuf(wrapper, new Tracker());
        try
        {
            Assert.Equal("CompositeByteBuf(ridx: 3, widx: 13, cap: 16, components=2)", wrapper.ToString()); Assert.Equal(0, tracker.Records);
            Assert.True(wrapper.Release()); Assert.Equal("CompositeByteBuf(freed, components=2)", wrapper.ToString());
            Assert.Equal(advanced ? 1 : 0, tracker.Records); Assert.Equal(1, tracker.Closes);
            // CLR clears component references; the freed diagnostic preserves the original count.
            Assert.Equal(0, root.NumComponents);
        }
        finally { if (root.ReferenceCount > 0) wrapper.Release(); }
    }

    [Fact]
    public void EmptySentinelUsesTheOriginalBigEndianNameAfterIgnoredOwnershipOperations()
    {
        ByteBuf empty = Unpooled.EmptyBuffer; Assert.Equal("EmptyByteBufBE", empty.ToString());
        Assert.False(empty.Release()); Assert.Same(empty, empty.Retain(0)); Assert.Equal("EmptyByteBufBE", empty.ToString());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DiagnosticDoesNotReadContentAndFreedPathDoesNotReadCapacityOrUnwrap(bool freed)
    {
        var b = new MetadataOnlyBuffer(freed);
        Assert.Equal(freed ? "ByteBufDiagnosticsTest+MetadataOnlyBuffer(freed)" : "ByteBufDiagnosticsTest+MetadataOnlyBuffer(ridx: 0, widx: 0, cap: 16/64)", b.ToString());
        Assert.Equal(0, b.StorageReads);
    }

    [Fact]
    public void DecimalMetadataKeepsTheOriginalFormatUnderCallerCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture; ByteBuf b = new UnpooledHeapByteBuf(16, 64).SetIndex(3, 13);
        try { CultureInfo.CurrentCulture = new CultureInfo("fa-IR"); Assert.Equal("UnpooledHeapByteBuf(ridx: 3, widx: 13, cap: 16/64)", b.ToString()); }
        finally { CultureInfo.CurrentCulture = previous; b.Release(); }
    }

    private sealed class PlainWrapper(ByteBuf b) : WrappedByteBuf(b);
    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        internal int Records, Closes;
        public void Record() => Records++; public void Record(object hint) => Records++;
        public bool Close(ByteBuf b) { Closes++; return true; }
        public Exception GetCloseStackTraceIfAny() => null;
    }
    private sealed class MetadataOnlyBuffer(bool freed) : ByteBuf(64)
    {
        internal int StorageReads;
        public override int ReferenceCount => freed ? 0 : 1;
        public override int Capacity { get => !freed ? 16 : throw new Exception("Freed capacity accessed"); set => throw new NotSupportedException(); }
        public override bool IsDirect => false;
        public override ByteBuf Unwrap() => !freed ? null : throw new Exception("Freed unwrap accessed");
        protected override Memory<byte> GetMemoryCore(int index, int length) { StorageReads++; throw new Exception("Content accessed"); }
        public override ByteBuf Retain(int increment = 1) => throw new NotSupportedException();
        public override bool Release(int decrement = 1) => throw new NotSupportedException();
    }
}
