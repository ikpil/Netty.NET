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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using static Netty.NET.Buffer.Tests.SimpleLeakAwareCompositeByteBufTest;

namespace Netty.NET.Buffer.Tests;

public class AdvancedLeakAwareCompositeByteBufTest
{
    public static IEnumerable<object[]> OperationCases()
    {
        for (int kind = 0; kind < 3; ++kind)
            for (int depth = 0; depth < 2; ++depth)
                for (int op = 0; op < 152; ++op) yield return [kind, depth, op];
    }

    [Theory]
    [MemberData(nameof(OperationCases))]
    public void DataViewsStructuresFailuresAndReferenceOperationsPreserveOriginalRecording(int kind, int depth, int op)
    {
        CompositeByteBuf expected = Owner(kind), origin = Owner(kind), referent = origin;
        if (depth != 0) referent = new WrappedCompositeByteBuf(origin);
        var tracker = new Tracker(referent);
        CompositeByteBuf b = new AdvancedLeakAwareCompositeByteBuf(referent, tracker);
        ByteBuf expectedView = null, view = null;
        string expectedResult = "", result = "";
        try
        {
            Exception expectedFailure = Record.Exception(() => expectedResult = Run(expected, op, out expectedView));
            Exception failure = Record.Exception(() => result = Run(b, op, out view));
            Assert.Equal(expectedFailure?.GetType(), failure?.GetType()); Assert.Equal(expectedResult, result);
            Assert.Equal(Snapshot(expected), Snapshot(origin)); Assert.Equal(expected.NumComponents, b.NumComponents);
            Assert.Equal(op is 133 or 134 or 135 or 136 or 137 or 138 or 142 ? 0 : 1, tracker.Records); Assert.Equal(0, tracker.Closes);
            if (view != null && op is >= 84 and <= 92) Assert.IsType<AdvancedLeakAwareByteBuf>(view);
        }
        finally
        {
            CleanupView(expectedView, op); CleanupView(view, op);
            expected.Release(expected.ReferenceCount); b.Release(origin.ReferenceCount);
        }
        Assert.Equal(1, tracker.Closes);
    }

    private static string Run(CompositeByteBuf cb, int op, out ByteBuf view)
    {
        if (op < 123) return Execute(cb, op, out view);
        view = null; string result = "ok";
        switch (op)
        {
            case 123: { cb.AddComponent(1,Unpooled.WrappedBuffer(new byte[]{90,91}),true); result="ok"; break; }
            case 124: { cb.AddComponent(Unpooled.WrappedBuffer(new byte[]{90,91}),true); result="ok"; break; }
            case 125: { cb.AddComponents(new ByteBuf[]{Unpooled.WrappedBuffer(new byte[]{90}),Unpooled.WrappedBuffer(new byte[]{91})},true); result="ok"; break; }
            case 126: { cb.AddComponents((System.Collections.Generic.IEnumerable<ByteBuf>)new ByteBuf[]{Unpooled.WrappedBuffer(new byte[]{90}),Unpooled.WrappedBuffer(new byte[]{91})},true); result="ok"; break; }
            case 127: { cb.RemoveComponent(0); result="ok"; break; }
            case 128: { cb.RemoveComponents(0,2); result="ok"; break; }
            case 129: { cb.Consolidate(); result="ok"; break; }
            case 130: { cb.Consolidate(0,2); result="ok"; break; }
            case 131: { cb.ReaderIndex=8; cb.DiscardReadComponents(); result="ok"; break; }
            case 132: { var s=new StringBuilder(); foreach(ByteBuf part in cb)s.Append(part.Capacity).Append(":").Append(ByteBufUtil.HexDump(part)).Append(";");result=s.ToString(); break; }
            case 133: { ByteBuf part=cb.Component(0);result=part.Capacity+","+part.ReaderIndex+","+part.WriterIndex+","+ByteBufUtil.HexDump(part); break; }
            case 134: { ByteBuf part=cb.ComponentSlice(0);result=part.Capacity+","+part.ReaderIndex+","+part.WriterIndex+","+ByteBufUtil.HexDump(part); break; }
            case 135: { ByteBuf part=cb.InternalComponent(0);result=part.Capacity+","+part.ReaderIndex+","+part.WriterIndex+","+ByteBufUtil.HexDump(part); break; }
            case 136: { ByteBuf part=cb.ComponentAtOffset(9);result=part.Capacity+","+part.ReaderIndex+","+part.WriterIndex+","+ByteBufUtil.HexDump(part); break; }
            case 137: { ByteBuf part=cb.InternalComponentAtOffset(9);result=part.Capacity+","+part.ReaderIndex+","+part.WriterIndex+","+ByteBufUtil.HexDump(part); break; }
            case 138: { result=cb.ToComponentIndex(9)+","+cb.ToByteIndex(cb.ToComponentIndex(9)); break; }
            case 139: { var s=new StringBuilder();foreach(ByteBuf part in cb.Decompose(3,22))s.Append(ByteBufUtil.HexDump(part)).Append(";");result=s.ToString(); break; }
            case 140: { var source=new CompositeByteBuf();source.AddComponents(new ByteBuf[]{Unpooled.WrappedBuffer(new byte[]{90,91}),Unpooled.WrappedBuffer(new byte[]{92,93})},true);var st=new Tracker(source);cb.AddFlattenedComponents(new SimpleLeakAwareCompositeByteBuf(source,st),true);result=st.Closes+","+source.ReferenceCount; break; }
            case 141: { ByteBuf incoming=Unpooled.WrappedBuffer(new byte[]{90});try{cb.AddComponent(100,incoming);}catch(Exception){result="error,"+incoming.ReferenceCount;} break; }
            case 142: { result=cb.IsDirect?"true":"false"; break; }
            case 143: { cb.AddComponents(Unpooled.WrappedBuffer(new byte[]{90}),Unpooled.WrappedBuffer(new byte[]{91})); result="ok"; break; }
            case 144: { cb.AddComponents(1,new ByteBuf[]{Unpooled.WrappedBuffer(new byte[]{90}),Unpooled.WrappedBuffer(new byte[]{91})}); result="ok"; break; }
            case 145: { cb.AddComponents(1,(System.Collections.Generic.IEnumerable<ByteBuf>)new ByteBuf[]{Unpooled.WrappedBuffer(new byte[]{90}),Unpooled.WrappedBuffer(new byte[]{91})}); result="ok"; break; }
            case 146: { cb.AddComponent(Unpooled.WrappedBuffer(new byte[]{90})); result="ok"; break; }
            case 147: { cb.AddComponent((ByteBuf)null); result="ok"; break; }
            case 148: { cb.RemoveComponents(-1,2); result="ok"; break; }
            case 149: { cb.Consolidate(-1,2); result="ok"; break; }
            case 150: { cb.Decompose(-1,2); result="ok"; break; }
            case 151: { result=cb.IsReadOnly?"true":"false"; break; }
        }
        return result;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalNestedCompositeForEachByteRegressionClosesBothTrackedOwners(bool direct)
    {
        var outer = new CompositeByteBuf(); var inner = new CompositeByteBuf();
        var outerTracker = new Tracker(outer); var innerTracker = new Tracker(inner);
        var b = new AdvancedLeakAwareCompositeByteBuf(outer, outerTracker);
        var c = new AdvancedLeakAwareCompositeByteBuf(inner, innerTracker);
        ByteBuf component = direct ? c.Allocator.DirectBuffer(1) : c.Allocator.HeapBuffer(1);
        c.AddComponent(component.WriteByte(0), true); b.AddComponent(c, true);
        Assert.Equal(-1, b.ForEachByte(static _ => true)); Assert.True(b.Release());
        Assert.Equal(1, outerTracker.Closes); Assert.Equal(1, innerTracker.Closes); Assert.Equal(0, component.ReferenceCount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RetainedAdvancedViewKeepsExactCompositeTrackerUntilItsFinalRelease(int kind)
    {
        CompositeByteBuf origin = Owner(kind); var referent = new WrappedCompositeByteBuf(origin);
        var tracker = new Tracker(referent); var b = new AdvancedLeakAwareCompositeByteBuf(referent, tracker);
        ByteBuf view = b.RetainedSlice(2, 20);
        Assert.IsType<AdvancedLeakAwareByteBuf>(view); Assert.Equal(1, tracker.Records);
        Assert.False(b.Release()); Assert.Equal(0, tracker.Closes); Assert.Equal(2, tracker.Records);
        Assert.Equal(2, view.GetByte(0)); Assert.Equal(3, tracker.Records);
        Assert.True(view.Release()); Assert.Equal(4, tracker.Records); Assert.Equal(1, tracker.Closes);
    }

    [Fact]
    public void NestedAdvancedTrackersRecordEachOwnerOnceAndCloseTheirExactReferents()
    {
        CompositeByteBuf origin = Owner(0); var innerTracker = new Tracker(origin);
        var inner = new AdvancedLeakAwareCompositeByteBuf(origin, innerTracker);
        var outerTracker = new Tracker(inner); var outer = new AdvancedLeakAwareCompositeByteBuf(inner, outerTracker);
        try
        {
            Assert.Equal(origin.GetInt(3), outer.GetInt(3));
            Assert.Equal(1, innerTracker.Records); Assert.Equal(1, outerTracker.Records);
            Assert.Same(outer, outer.AddComponent(Unpooled.WrappedBuffer(new byte[] { 90 }), true));
            Assert.Equal(2, innerTracker.Records); Assert.Equal(2, outerTracker.Records);
        }
        finally { outer.Release(); }
        Assert.Equal(3, innerTracker.Records); Assert.Equal(3, outerTracker.Records);
        Assert.Equal(1, innerTracker.Closes); Assert.Equal(1, outerTracker.Closes);
    }

    [Fact]
    public void ReadOnlyQueryRecordsButIndexMetadataComparisonAndComponentLookupDoNot()
    {
        CompositeByteBuf origin = Owner(0); var tracker = new Tracker(origin);
        var b = new AdvancedLeakAwareCompositeByteBuf(origin, tracker);
        try
        {
            _ = b.Capacity; _ = b.Allocator; _ = b.IsDirect; _ = b.IsContiguous; _ = b.NumComponents;
            _ = b.MaxNumComponents; _ = b.ReadableBytes; _ = b.ReferenceCount;
            Assert.Equal(origin.GetHashCode(), b.GetHashCode()); Assert.Equal(0, b.CompareTo(origin)); Assert.True(b.Equals(origin));
            _ = b.Component(0); _ = b.InternalComponent(0); _ = b.ToComponentIndex(9); _ = b.ToByteIndex(1);
            b.SetIndex(1, 6).MarkReaderIndex().MarkWriterIndex().Clear().ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(0, tracker.Records); Assert.False(b.IsReadOnly); Assert.Equal(1, tracker.Records);
            object hint = new object(); Assert.Same(b, b.Touch(hint)); Assert.Same(hint, tracker.Hint);
            Assert.Equal(2, tracker.Records); Assert.Same(b, b.Touch()); Assert.Same(hint, tracker.Hint); Assert.Equal(3, tracker.Records);
        }
        finally { b.Release(); }
        Assert.Equal(4, tracker.Records); Assert.Equal(1, tracker.Closes);
    }

    [Fact]
    public void FailedReleaseRecordsBeforeFailureWithoutInventingCompositeCloseDiagnostics()
    {
        var origin = new CompositeByteBuf(); var tracker = new Tracker(origin);
        var b = new AdvancedLeakAwareCompositeByteBuf(origin, tracker);
        Assert.True(b.Release()); Assert.Throws<IllegalReferenceCountException>(() => b.Release());
        Assert.Equal(2, tracker.Records); Assert.Equal(1, tracker.Closes);
    }

    [Fact]
    public void GenericAndNonGenericEnumerationRecordAtAcquisitionAndKeepBorrowedInvalidation()
    {
        CompositeByteBuf origin = Owner(0); var tracker = new Tracker(origin);
        var b = new AdvancedLeakAwareCompositeByteBuf(origin, tracker);
        try
        {
            using var generic = ((IEnumerable<ByteBuf>)b).GetEnumerator(); Assert.True(generic.MoveNext());
            Assert.Same(origin.ComponentSlice(0), generic.Current); Assert.Equal(1, tracker.Records);
            var iterator = ((System.Collections.IEnumerable)b).GetEnumerator(); Assert.True(iterator.MoveNext());
            Assert.Equal(2, tracker.Records); Assert.Equal(1, origin.ReferenceCount);
            origin.AddComponent(Unpooled.Buffer(0));
            Assert.Throws<InvalidOperationException>(() => generic.MoveNext());
            Assert.Throws<InvalidOperationException>(() => iterator.MoveNext()); Assert.Equal(2, tracker.Records);
            (iterator as IDisposable)?.Dispose();
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeBorrowPinsCanOutliveLogicalLeakClose(bool internalLease)
    {
        var native = new NativeMemoryAllocator(); var allocator = new UnpooledByteBufAllocator(true, native);
        var origin = new CompositeByteBuf(allocator, true).AddComponent(allocator.DirectBuffer(8, 8).WriteLong(42), true);
        var tracker = new Tracker(origin); var b = new AdvancedLeakAwareCompositeByteBuf(origin, tracker);
        var pin = internalLease ? default : b.AsMemory(0, 8).Pin();
        var lease = internalLease ? b.AcquireReadLease() : default;
        try
        {
            Assert.True(b.Release()); Assert.Equal(1, tracker.Closes); Assert.Equal(internalLease ? 1 : 2, tracker.Records);
            Assert.Equal(8, native.ReservedBytes);
        }
        finally { lease.Dispose(); pin.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void ClrMemoryAndLittleEndianFloatingPointAccessesRecordOnce(int operation)
    {
        CompositeByteBuf origin = Owner(0); var tracker = new Tracker(origin);
        ByteBuf b = new AdvancedLeakAwareCompositeByteBuf(origin, tracker);
        try
        {
            switch (operation)
            {
                case 0: Assert.Equal(25, b.AsMemory(25, 8).Span[0]); break;
                case 1: Assert.Equal(25, b.AsSpan(25, 8)[0]); break;
                case 2: Assert.Equal(25, b.AsReadOnlyMemory(25, 8).Span[0]); break;
                case 3: Assert.Equal(25, b.AsReadOnlySpan(25, 8)[0]); break;
                case 4: Assert.Equal(8, b.AsReadOnlySequence(3, 8).Length); break;
                case 5: b.SetFloatLE(3, 1.25f); Assert.Equal(1.25f, origin.GetFloatLE(3)); break;
                case 6: b.WriteDoubleLE(2.5); Assert.Equal(2.5, origin.GetDoubleLE(32)); break;
                case 7: origin.SetFloatLE(2, 1.25f); Assert.Equal(1.25f, b.ReadFloatLE()); break;
            }
            Assert.Equal(1, tracker.Records);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SynchronousStreamTransfersRecordOnceAndRetainOuterFluentIdentity(bool direct)
    {
        ByteBuf origin = Owner(direct ? 1 : 0); var tracker = new Tracker(origin);
        ByteBuf b = new AdvancedLeakAwareCompositeByteBuf((CompositeByteBuf)origin, tracker);
        using var stream = new MemoryStream();
        try
        {
            Assert.Same(b, b.GetBytes(3, stream, 8)); Assert.Equal(1, tracker.Records);
            Assert.Equal("030405060708090a", Convert.ToHexStringLower(stream.ToArray()));
            Assert.Same(b, b.ReadBytes(stream, 8)); Assert.Equal(2, tracker.Records);
            stream.Position = 0;
            Assert.Equal(8, b.SetBytes(3, stream, 8)); Assert.Equal(3, tracker.Records);
            Assert.Equal(8, b.WriteBytes(stream, 8)); Assert.Equal(4, tracker.Records);
            Assert.Equal(10, origin.ReaderIndex); Assert.Equal(40, origin.WriterIndex);
            Assert.Throws<ArgumentNullException>(() => b.GetBytes(3, (Stream)null, 8));
            Assert.Equal(5, tracker.Records);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task AsyncStreamTransfersAndCancellationRecordOnce(bool cancel)
    {
        ByteBuf origin = Owner(1); var tracker = new Tracker(origin); ByteBuf b = new AdvancedLeakAwareCompositeByteBuf((CompositeByteBuf)origin, tracker);
        using var stream = new MemoryStream(new byte[64]);
        try
        {
            CancellationToken token = cancel ? new CancellationToken(true) : TestContext.Current.CancellationToken;
            async Task Run(ValueTask pending)
            {
                if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
                else await pending;
            }
            await Run(b.GetBytesAsync(3, stream, 8, token)); Assert.Equal(1, tracker.Records);
            await Run(b.ReadBytesAsync(stream, 8, token)); Assert.Equal(2, tracker.Records);
            if (cancel) Assert.Equal(2, b.ReaderIndex); else Assert.Equal(10, b.ReaderIndex);
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await b.SetBytesAsync(3, stream, 8, token));
            else { stream.Position = 0; Assert.Equal(8, await b.SetBytesAsync(3, stream, 8, token)); }
            Assert.Equal(3, tracker.Records);
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await b.WriteBytesAsync(stream, 8, token));
            else { stream.Position = 0; Assert.Equal(8, await b.WriteBytesAsync(stream, 8, token)); }
            Assert.Equal(4, tracker.Records); Assert.Equal(cancel ? 32 : 40, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PositionedFilesRecordEachSyncAndAsyncTransferOnce(bool asyncIo)
    {
        string path = Path.GetTempFileName(); ByteBuf origin = Owner(1); var tracker = new Tracker(origin);
        ByteBuf b = new AdvancedLeakAwareCompositeByteBuf((CompositeByteBuf)origin, tracker);
        try
        {
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read,
                asyncIo ? FileOptions.Asynchronous : FileOptions.None);
            var token = TestContext.Current.CancellationToken;
            if (asyncIo)
            {
                Assert.Equal(8, await b.GetBytesAsync(3, file, 0, 8, token)); Assert.Equal(1, tracker.Records);
                Assert.Equal(8, await b.ReadBytesAsync(file, 8, 8, token)); Assert.Equal(2, tracker.Records);
                Assert.Equal(8, await b.SetBytesAsync(3, file, 0, 8, token)); Assert.Equal(3, tracker.Records);
                Assert.Equal(8, await b.WriteBytesAsync(file, 8, 8, token)); Assert.Equal(4, tracker.Records);
            }
            else
            {
                Assert.Equal(8, b.GetBytes(3, file, 0, 8)); Assert.Equal(1, tracker.Records);
                Assert.Equal(8, b.ReadBytes(file, 8, 8)); Assert.Equal(2, tracker.Records);
                Assert.Equal(8, b.SetBytes(3, file, 0, 8)); Assert.Equal(3, tracker.Records);
                Assert.Equal(8, b.WriteBytes(file, 8, 8)); Assert.Equal(4, tracker.Records);
            }
            Assert.Equal(10, origin.ReaderIndex); Assert.Equal(40, origin.WriterIndex);
        }
        finally { b.Release(); File.Delete(path); }
    }

    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Records, Closes;
        internal object Hint;
        public void Record() => Records++;
        public void Record(object hint) { Hint = hint; Records++; }
        public bool Close(ByteBuf b) { Assert.Same(expected, b); Assert.Equal(0, b.ReferenceCount); return ++Closes == 1; }
    }
}
