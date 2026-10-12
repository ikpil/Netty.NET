/*
 * Copyright 2016 The Netty Project
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
using System.Runtime.CompilerServices;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

public class SimpleLeakAwareByteBufTest
{
    private static ByteBuf Owner(int kind)
    {
        byte[] bytes = [0, 1, 2, 3, 4, 5, 6, 7];
        return kind switch
        {
            0 => Unpooled.Buffer(8, 32).WriteBytes(bytes),
            1 => Unpooled.DirectBuffer(8, 32).WriteBytes(bytes),
            2 => Unpooled.WrappedBuffer(bytes).AsReadOnly(),
            3 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(bytes[..3]), true)
                .AddComponent(Unpooled.WrappedBuffer(bytes[3..]), true),
            4 => Unpooled.WrappedUnmodifiableBuffer(Unpooled.WrappedBuffer(bytes[..3]), Unpooled.WrappedBuffer(bytes[3..])),
            5 => Unpooled.WrappedBuffer(new byte[] { 9, 0, 1, 2, 3, 4, 5, 6, 7, 9 }).Slice(1, 8),
            6 => Unpooled.WrappedBuffer(bytes).Duplicate(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    public static IEnumerable<object[]> ViewCases()
    {
        for (int kind = 0; kind < 7; kind++)
            for (int operation = 0; operation < 9; operation++) yield return [kind, operation];
    }

    private static ByteBuf View(ByteBuf buffer, int operation) => operation switch
    {
        0 => buffer.Slice(),
        1 => buffer.Slice(2, 3),
        2 => buffer.Duplicate(),
        3 => buffer.ReadSlice(2),
        4 => buffer.AsReadOnly(),
        5 => buffer.RetainedSlice(),
        6 => buffer.RetainedSlice(2, 3),
        7 => buffer.RetainedDuplicate(),
        8 => buffer.ReadRetainedSlice(2),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    [Theory]
    [MemberData(nameof(ViewCases))]
    public void OriginalViewContractsShareTrackerAndCloseOnlyAtFinalRelease(int kind, int operation)
    {
        ByteBuf origin = Owner(kind).SetIndex(1, 7);
        var tracker = new Tracker(origin);
        var buffer = new SimpleLeakAwareByteBuf(origin, tracker);
        ByteBuf view = View(buffer, operation);
        bool retained = operation >= 5;
        try
        {
            Assert.IsType<SimpleLeakAwareByteBuf>(view);
            Assert.Same(origin, buffer.Unwrap());
            Assert.Same(origin.Allocator, view.Allocator);
            Assert.Equal(retained ? 2 : 1, origin.ReferenceCount);
            Assert.Equal(operation is 3 or 8 ? 3 : 1, origin.ReaderIndex);
            string expected = operation is 1 or 6 ? "020304" : operation is 3 or 8 ? "0102" : "010203040506";
            Assert.Equal(expected, ByteBufUtil.HexDump(view));
            Assert.Equal(origin.IsReadOnly || operation == 4, view.IsReadOnly);
            Assert.Same(view, view.Touch(new object()));
            Assert.Equal(0, tracker.Records); Assert.Equal(0, tracker.Closes);
            if (retained)
            {
                Assert.False(kind % 2 == 0 ? buffer.Release() : view.Release());
                Assert.Equal(0, tracker.Closes);
                Assert.True(kind % 2 == 0 ? view.Release() : buffer.Release());
            }
            else Assert.True(view.Release());
            Assert.Equal(1, tracker.Closes);
            Assert.Equal(0, origin.ReferenceCount);
            Assert.Same(origin, tracker.ClosedObject);
        }
        finally { if (origin.ReferenceCount > 0) buffer.Release(origin.ReferenceCount); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void CopyHasIndependentLifetimeAndDoesNotCloseTheOriginalTracker(int kind)
    {
        ByteBuf origin = Owner(kind).SetIndex(1, 7);
        var tracker = new Tracker(origin);
        var buffer = new SimpleLeakAwareByteBuf(origin, tracker);
        ByteBuf copy = buffer.Copy();
        try
        {
            if (copy is SimpleLeakAwareByteBuf)
                Assert.NotSame(tracker, AllocatorLeakDetectionTest.Tracker(copy));
            Assert.Equal("010203040506", ByteBufUtil.HexDump(copy));
            Assert.True(buffer.Equals(copy));
            Assert.Equal(buffer.GetHashCode(), copy.GetHashCode());
            Assert.True(copy.Release());
            Assert.Equal(0, tracker.Closes); Assert.Equal(1, buffer.ReferenceCount);
            Assert.True(buffer.Release()); Assert.Equal(1, tracker.Closes);
        }
        finally
        {
            if (copy.ReferenceCount > 0) copy.Release();
            if (origin.ReferenceCount > 0) buffer.Release();
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void OriginalDoubleFreeDiagnosticsIncludeTheOriginalCloseCall(int mechanism)
    {
        ByteBuf origin = Unpooled.Buffer(1, 1);
        var detector = new ResourceLeakDetector<ByteBuf>(typeof(ByteBuf), 1);
        IResourceLeakTracker<ByteBuf> tracker = detector.TrackForcibly(origin);
        ByteBuf buffer = new SimpleLeakAwareByteBuf(origin, tracker);
        OriginalCloseCall(buffer, mechanism);
        Assert.False(ByteBufUtil.IsAccessible(buffer));
        IllegalReferenceCountException failure = Assert.Throws<IllegalReferenceCountException>(() => buffer.Release());
        Exception closeTrace = Assert.Single(ThrowableUtil.GetSuppressed(failure));
        Assert.Same(tracker.GetCloseStackTraceIfAny(), closeTrace);
        Assert.Contains(nameof(OriginalCloseCall), closeTrace.ToString());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OriginalCloseCall(ByteBuf buffer, int mechanism)
    {
        if (mechanism == 0) buffer.Release();
        else ((IReferenceCounted)buffer).Release(1);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void AllGuardedReferenceOperationsAttachCloseDiagnosticsAfterRelease(int operation)
    {
        ByteBuf origin = Unpooled.Buffer(1, 1).WriteByte(1);
        var tracker = new Tracker(origin) { Trace = new Exception("original close") };
        var buffer = new SimpleLeakAwareByteBuf(origin, tracker);
        buffer.Release();
        IllegalReferenceCountException failure = Assert.Throws<IllegalReferenceCountException>(() =>
        {
            switch (operation)
            {
                case 0: buffer.Retain(); break;
                case 1: ((IReferenceCounted)buffer).Retain(2); break;
                case 2: buffer.Release(); break;
                case 3: ((IReferenceCounted)buffer).Release(2); break;
                case 4: buffer.RetainedSlice(); break;
                case 5: buffer.RetainedSlice(0, 1); break;
                case 6: buffer.RetainedDuplicate(); break;
                case 7: buffer.ReadRetainedSlice(1); break;
            }
        });
        Assert.Same(tracker.Trace, Assert.Single(ThrowableUtil.GetSuppressed(failure)));
        Assert.Equal(1, tracker.Closes);
    }

    [Fact]
    public void CountValidationAndFailedRetainDoNotCloseOrChangeOwnership()
    {
        var failure = new IllegalReferenceCountException("injected retain failure");
        ByteBuf origin = Unpooled.Buffer(1, 1);
        var parent = new FailingRetainBuffer(origin, failure);
        var tracker = new Tracker(parent) { Trace = new Exception("close diagnostic") };
        var buffer = new SimpleLeakAwareByteBuf(parent, tracker);
        try
        {
            Assert.Same(failure, Assert.Throws<IllegalReferenceCountException>(() => buffer.Retain()));
            Assert.Empty(ThrowableUtil.GetSuppressed(failure));
            Assert.Equal(0, tracker.Closes); Assert.Equal(1, origin.ReferenceCount);
            Assert.Throws<ArgumentException>(() => buffer.Release(0));
            Assert.Throws<IllegalReferenceCountException>(() => buffer.Release(2));
            Assert.Equal(0, tracker.Closes); Assert.Equal(1, origin.ReferenceCount);
        }
        finally { buffer.Release(); }
        Assert.Equal(1, tracker.Closes);
    }

    [Fact]
    public void SharedFactoryOverridePreservesDerivedWrapperAndOriginalTrackedIdentity()
    {
        ByteBuf origin = Unpooled.Buffer(2, 2).WriteShort(258);
        var tracker = new Tracker(origin);
        var buffer = new DerivedLeakAware(origin, origin, tracker);
        ByteBuf first = buffer.RetainedDuplicate(), second = first.Slice().AsReadOnly();
        try
        {
            Assert.IsType<DerivedLeakAware>(first); Assert.IsType<DerivedLeakAware>(second);
            Assert.False(buffer.Release()); Assert.Equal(0, tracker.Closes);
            Assert.True(second.Release()); Assert.Same(origin, tracker.ClosedObject);
        }
        finally { if (origin.ReferenceCount > 0) buffer.Release(origin.ReferenceCount); }
    }

    [Fact]
    public void SimpleTouchDoesNotRecordOrForwardHintsAndRetainReturnsFacade()
    {
        var origin = new TouchBuffer();
        var tracker = new Tracker(origin);
        var buffer = new SimpleLeakAwareByteBuf(origin, tracker);
        try
        {
            Assert.Same(buffer, buffer.Touch());
            Assert.Same(buffer, ReferenceCountUtil.Touch(buffer, new object()));
            Assert.Equal(0, tracker.Records); Assert.Equal(0, origin.Touches);
            Assert.Same(buffer, ReferenceCountUtil.Retain(buffer, 3));
            Assert.Equal(4, origin.ReferenceCount);
            Assert.False(buffer.Release(3)); Assert.Equal(0, tracker.Closes);
        }
        finally { buffer.Release(); }
        Assert.Equal(1, tracker.Closes);
    }

    [Fact]
    public void InvalidWrapperArgumentsLeaveCallerReferencesUntouched()
    {
        ByteBuf origin = Unpooled.Buffer(1, 1);
        var tracker = new Tracker(origin);
        try
        {
            Assert.Throws<ArgumentNullException>(() => new SimpleLeakAwareByteBuf(null, tracker));
            Assert.Throws<ArgumentNullException>(() => new SimpleLeakAwareByteBuf(origin, null));
            Assert.Throws<ArgumentNullException>(() => new SimpleLeakAwareByteBuf(origin, null, tracker));
            Assert.Equal(1, origin.ReferenceCount); Assert.Equal(0, tracker.Closes);
        }
        finally { origin.Release(); }
    }

    [Fact]
    public void ImmortalAndUnreleasableParentsNeverFalselyCloseTheTracker()
    {
        var emptyTracker = new Tracker(Unpooled.EmptyBuffer);
        var empty = new SimpleLeakAwareByteBuf(Unpooled.EmptyBuffer, emptyTracker);
        Assert.Same(empty, empty.Retain());
        Assert.False(empty.Release());
        Assert.Equal(0, emptyTracker.Closes);

        ByteBuf origin = Unpooled.Buffer(1, 1).WriteByte(1);
        ByteBuf parent = Unpooled.UnreleasableBuffer(origin);
        var tracker = new Tracker(parent);
        var wrapper = new SimpleLeakAwareByteBuf(parent, tracker);
        try
        {
            Assert.Same(wrapper, wrapper.Retain());
            Assert.False(wrapper.Release());
            Assert.Equal(1, origin.ReferenceCount);
            Assert.Equal(0, tracker.Closes);
        }
        finally { origin.Release(); }
    }

    [Fact]
    public void TrackerClosesAtLogicalReleaseWhileNativePinPreservesPhysicalStorage()
    {
        var native = new NativeMemoryAllocator(8);
        var allocator = new UnpooledByteBufAllocator(true, native);
        ByteBuf origin = allocator.DirectBuffer(1, 1).WriteByte(7);
        var tracker = new Tracker(origin);
        var wrapper = new SimpleLeakAwareByteBuf(origin, tracker);
        try
        {
            using (var pin = wrapper.AsMemory(0, 1).Pin())
            {
                Assert.True(wrapper.Release());
                Assert.Equal(1, tracker.Closes);
                Assert.Equal(0, allocator.Metric.UsedDirectMemory);
                Assert.Equal(1, native.ReservedBytes);
            }
            Assert.Equal(0, native.ReservedBytes);
        }
        finally { if (origin.ReferenceCount > 0) wrapper.Release(); }
    }

    [Fact]
    public void NativeGrowthAndEncodingKeepSharedIndicesAllocatorAndTracker()
    {
        var native = new NativeMemoryAllocator(64);
        var allocator = new UnpooledByteBufAllocator(true, native);
        ByteBuf origin = allocator.DirectBuffer(1, 32).WriteByte(1);
        var tracker = new Tracker(origin);
        var wrapper = new SimpleLeakAwareByteBuf(origin, tracker);
        try
        {
            Assert.Equal(2, ByteBufUtil.WriteUtf8(wrapper, "é".AsSpan()));
            Assert.Equal(3, origin.WriterIndex);
            Assert.Equal("01c3a9", ByteBufUtil.HexDump(wrapper));
            Assert.Same(allocator, wrapper.Allocator);
            Assert.Equal(origin.Capacity, native.ReservedBytes);
            Assert.Equal(origin.Capacity, allocator.Metric.UsedDirectMemory);
            wrapper.Capacity = 3;
            Assert.Equal(3, origin.Capacity);
            Assert.Equal(0, tracker.Closes); Assert.Equal(0, tracker.Records);
        }
        finally { wrapper.Release(); }
        Assert.Equal(1, tracker.Closes); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void WrappedBaseForwardsStorageIndicesMarksComparisonAndDerivedViews(int kind)
    {
        ByteBuf origin = Owner(kind);
        var buffer = new PlainWrapper(origin);
        try
        {
            Assert.Equal(origin.Capacity, buffer.Capacity);
            Assert.Equal(origin.MaxCapacity, buffer.MaxCapacity);
            Assert.Equal(origin.IsDirect, buffer.IsDirect);
            Assert.Equal(origin.CanWrite(1), buffer.CanWrite(1));
            Assert.Same(origin.Allocator, buffer.Allocator);
            Assert.Equal(0x01020304, buffer.GetInt(1));
            buffer.SetIndex(1, 7).MarkReaderIndex().MarkWriterIndex();
            Assert.Equal(1, origin.ReaderIndex); Assert.Equal(7, origin.WriterIndex);
            origin.SetIndex(2, 6).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(7, buffer.WriterIndex);
            Assert.Equal("010203040506", ByteBufUtil.HexDump(buffer));
            Assert.Equal(4, buffer.IndexOf(1, 7, 4));
            Assert.Equal(0, buffer.CompareTo(origin));
            Assert.Same(buffer, buffer.AsByteBuf());
            Assert.IsNotType<PlainWrapper>(buffer.Duplicate());
            Assert.Equal(1, buffer.ReadByte());
        }
        finally { buffer.Release(); }
    }

    private sealed class PlainWrapper(ByteBuf buffer) : WrappedByteBuf(buffer);
    private sealed class FailingRetainBuffer(ByteBuf buffer, IllegalReferenceCountException failure) : WrappedByteBuf(buffer)
    {
        public override ByteBuf Retain(int increment = 1) => throw failure;
    }
    private sealed class DerivedLeakAware(ByteBuf buffer, ByteBuf tracked, IResourceLeakTracker<ByteBuf> leak)
        : SimpleLeakAwareByteBuf(buffer, tracked, leak)
    {
        protected override SimpleLeakAwareByteBuf NewLeakAwareByteBuf(ByteBuf buffer, ByteBuf trackedByteBuf,
            IResourceLeakTracker<ByteBuf> leakTracker) => new DerivedLeakAware(buffer, trackedByteBuf, leakTracker);
    }
    private sealed class TouchBuffer() : UnpooledHeapByteBuf(1, 1)
    {
        internal int Touches;
        public override ByteBuf Touch(object hint = null) { Touches++; return this; }
    }
    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Closes, Records;
        internal ByteBuf ClosedObject;
        internal Exception Trace;
        public void Record() => Records++;
        public void Record(object hint) => Records++;
        public bool Close(ByteBuf trackedObject)
        {
            Assert.Same(expected, trackedObject);
            Assert.Equal(0, trackedObject.ReferenceCount);
            ClosedObject = trackedObject;
            Closes++;
            return Closes == 1;
        }
        public Exception GetCloseStackTraceIfAny() => Closes > 0 ? Trace : null;
    }
}
