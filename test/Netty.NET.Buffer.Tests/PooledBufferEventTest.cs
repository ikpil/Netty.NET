/*
 * Copyright 2026 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

[CollectionDefinition(nameof(PooledBufferEventCollection), DisableParallelization = true)]
public class PooledBufferEventCollection { }

[Collection(nameof(PooledBufferEventCollection))]
public class PooledBufferEventTest
{
    private static PooledByteBufAllocator NewAllocator(NativeMemoryAllocator native, int cache = 0, bool pooled = true)
        => new(poolHeap: pooled, poolDirect: pooled, maxOrder: 1, nativeAllocator: native, smallCacheSize: cache, normalCacheSize: cache);
    private static ByteBuf Allocate(PooledByteBufAllocator allocator, bool direct, int size, int max = 40000)
        => direct ? allocator.DirectBuffer(size, max) : allocator.HeapBuffer(size, max);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public unsafe void OriginalPooledAllocationAndFinalReleaseCarryCompleteMetadata(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = new Listener(); ByteBuf buffer = Allocate(allocator, direct, 128, int.MaxValue);
        try
        {
            Record allocation = Assert.Single(listener.Events); Check(allocation, 1, 128, 128, int.MaxValue, direct);
            Assert.True(allocation.Flag("chunkPooled")); Assert.False(allocation.Flag("chunkThreadLocal"));
            using (var pin = buffer.AsReadOnlyMemory(0, 128).Pin())
                Assert.True(direct ? allocation.Number("address") == unchecked((long)(nint)pin.Pointer) : allocation.Number("address") == 0);
            buffer.Retain(); Assert.False(buffer.Release()); Assert.Single(listener.Events);
            buffer.SetIndex(7, 99).MarkReaderIndex().MarkWriterIndex();
        }
        finally { buffer.Release(); }
        Record release = listener.Events[1]; Check(release, 2, 128, 128, int.MaxValue, direct);
        Assert.True(release.Number("address") == listener.Events[0].Number("address"));
        Assert.Equal(2, listener.Events.Length); allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalThreadLocalFlagDistinguishesCacheHitsFromCacheEligibleMisses(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native, cache: 4);
        using var listener = new Listener();
        // Prime the cache.
        Allocate(allocator, direct, 128).Release();
        // Allocate out of the cache.
        Allocate(allocator, direct, 128).Release();
        Assert.Equal(new[] { 1, 2, 1, 2 }, listener.Events.Select(e => e.Id));
        Assert.False(listener.Events[0].Flag("chunkThreadLocal"));
        Assert.True(listener.Events[2].Flag("chunkThreadLocal")); Assert.True(listener.Events[2].Flag("chunkPooled"));
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ResizeReportsOldAndNewSnapshotsOnlyWhenStorageIsReallocated(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = new Listener(); ByteBuf buffer = Allocate(allocator, direct, 17);
        try
        {
            buffer.SetIndex(2, 15).MarkReaderIndex().MarkWriterIndex();
            buffer.Capacity = 24; buffer.Capacity = 24; Assert.Single(listener.Events);
            buffer.Capacity = 16;
            Assert.Equal(new[] { 1, 3, 1 }, listener.Events.Select(e => e.Id));
            Check(listener.Events[1], 3, 24, 32, 40000, direct); Assert.Equal(16, listener.Events[1].Number("newCapacity"));
            Check(listener.Events[2], 1, 16, 16, 40000, direct);
            Assert.True(listener.Events[0].Number("address") == listener.Events[1].Number("address"));
            Assert.Equal(2, buffer.ReaderIndex); Assert.Equal(15, buffer.WriterIndex);
            buffer.ResetReaderIndex().ResetWriterIndex();
        }
        finally { buffer.Release(); }
        Check(listener.Events[3], 2, 16, 16, 40000, direct);
        Assert.True(listener.Events[2].Number("address") == listener.Events[3].Number("address"));
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FastCapacityIsCappedByTheBufferMaximumAndIndependentOfActiveCursors(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = new Listener(); ByteBuf buffer = Allocate(allocator, direct, 17, 23);
        buffer.SetIndex(7, 13); buffer.Capacity = 22; buffer.Release();
        Check(listener.Events[0], 1, 17, 23, 23, direct);
        Check(listener.Events[1], 2, 22, 23, 23, direct);
        Assert.Equal(2, listener.Events.Length); allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, 0, true)] [InlineData(true, 0, true)]
    [InlineData(false, 17000, false)] [InlineData(true, 17000, false)]
    public void ArenaZeroAndHugeAllocationsDescribeTheActualStoragePolicy(bool direct, int size, bool pooled)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = new Listener(); ByteBuf buffer = Allocate(allocator, direct, size, 17000);
        int fast = Math.Min(((PooledByteBuf)AllocatorLeakDetectionTest.UnwrapLeak(buffer)).Allocation.ReservedLength, 17000);
        buffer.Release();
        Check(listener.Events[0], 1, size, fast, 17000, direct);
        Assert.Equal(pooled, listener.Events[0].Flag("chunkPooled")); Assert.False(listener.Events[0].Flag("chunkThreadLocal"));
        Check(listener.Events[1], 2, size, fast, 17000, direct);
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalNoArenaFallbackReportsAllocationWithoutInventingUnpooledFreeOrResizeEvents(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native, pooled: false);
        using var listener = new Listener(); ByteBuf buffer = Allocate(allocator, direct, 17);
        buffer.Capacity = 64; buffer.Release();
        Record allocation = Assert.Single(listener.Events); Check(allocation, 1, 17, 17, 40000, direct);
        Assert.False(allocation.Flag("chunkPooled")); Assert.False(allocation.Flag("chunkThreadLocal"));
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(4)]
    public void IndependentKeywordsSelectOnlyTheRequestedLifecycleEvent(long keyword)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = new Listener((EventKeywords)keyword); ByteBuf buffer = Allocate(allocator, true, 16);
        buffer.Capacity = 128; buffer.Release();
        int id = keyword == 1 ? 1 : keyword == 2 ? 2 : 3;
        Assert.Equal(keyword == 1 ? 2 : 1, listener.Events.Length);
        Assert.All(listener.Events, e => Assert.Equal(id, e.Id));
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void FailedNativeResizeReportsAttemptWithoutPublishingAllocationOrChangingData()
    {
        var native = new NativeMemoryAllocator(16384); using var allocator = NewAllocator(native);
        using var listener = new Listener(); ByteBuf buffer = Allocate(allocator, true, 16);
        try
        {
            buffer.WriteInt(0x12345678); buffer.SetIndex(1, 4);
            Assert.Throws<OutOfMemoryException>(() => buffer.Capacity = 20000);
            Assert.Equal(new[] { 1, 3 }, listener.Events.Select(e => e.Id));
            Check(listener.Events[1], 3, 16, 16, 40000, true); Assert.Equal(20000, listener.Events[1].Number("newCapacity"));
            Assert.Equal(16, buffer.Capacity); Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex);
            Assert.Equal(0x12345678, buffer.GetInt(0)); Assert.Equal(16384, native.ReservedBytes);
        }
        finally { buffer.Release(); }
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void InvalidAndDisposedOperationsDoNotEmitSuccessfulAllocationsOrResizeAttempts()
    {
        var native = new NativeMemoryAllocator(0); using var allocator = NewAllocator(native);
        using var listener = new Listener();
        Assert.Throws<OutOfMemoryException>(() => Allocate(allocator, true, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => Allocate(allocator, false, -1));
        ByteBuf buffer = Allocate(allocator, false, 16, 16);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Capacity = 17);
        buffer.Release(); allocator.Dispose();
        Assert.Throws<ObjectDisposedException>(() => Allocate(allocator, false, 16));
        Assert.Equal(new[] { 1, 2 }, listener.Events.Select(e => e.Id)); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void DisabledAndReenabledTracingDoesNotRetainBufferOrNativeStorage()
    {
        Assert.False(BufferAllocatorEventSource.Log.IsEnabled());
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        Allocate(allocator, true, 16).Release();
        using (var listener = new Listener())
        {
            Allocate(allocator, true, 16).Release(); Assert.Equal(2, listener.Events.Length);
            listener.DisableEvents(BufferAllocatorEventSource.Log);
            Allocate(allocator, true, 16).Release(); Assert.Equal(2, listener.Events.Length);
            listener.EnableEvents(BufferAllocatorEventSource.Log, EventLevel.Informational, (EventKeywords)7);
            Allocate(allocator, true, 16).Release(); Assert.Equal(4, listener.Events.Length);
        }
        Assert.False(BufferAllocatorEventSource.Log.IsEnabled()); allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void ListenerCallbacksObserveSnapshotsOutsideAllocatorAndResizeLocks()
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = new Listener();
        object sync = typeof(PooledByteBufAllocator).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(allocator);
        ByteBuf buffer = null; bool held = false;
        listener.Callback = _ =>
        {
            held |= Monitor.IsEntered(sync);
            if (buffer != null)
            {
                ByteBuf root = AllocatorLeakDetectionTest.UnwrapLeak(buffer);
                object resize = typeof(PooledByteBuf).GetField("_resizeLock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(root);
                held |= Monitor.IsEntered(resize);
            }
        };
        buffer = Allocate(allocator, true, 16); buffer.Capacity = 128; buffer.Release();
        Assert.False(held); Assert.Equal(new[] { 1, 3, 1, 2 }, listener.Events.Select(e => e.Id));
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public unsafe void NativeOffsetAndRetainedViewsKeepOneRootLifecycle()
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = new Listener();
        ByteBuf first = Allocate(allocator, true, 128), second = Allocate(allocator, true, 128);
        ByteBuf view = second.RetainedSlice(3, 8).Duplicate();
        try
        {
            using var pin = second.AsReadOnlyMemory(0, 128).Pin();
            Assert.True(listener.Events[1].Number("address") == unchecked((long)(nint)pin.Pointer));
            Assert.True(listener.Events[0].Number("address") != listener.Events[1].Number("address"));
            Assert.False(second.Release()); Assert.Equal(2, listener.Events.Length);
            Assert.True(view.Release()); Assert.Equal(3, listener.Events.Length);
            Assert.True(listener.Events[1].Number("address") == listener.Events[2].Number("address"));
        }
        finally { first.Release(); }
        Assert.Equal(new[] { 1, 1, 2, 2 }, listener.Events.Select(e => e.Id));
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void FreeEventDoesNotRetainStorageAfterTheLastOperationLeaseEnds()
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = new Listener(); ByteBuf buffer = Allocate(allocator, true, 16);
        var lease = AllocatorLeakDetectionTest.UnwrapLeak(buffer).AcquireReadLease();
        try
        {
            buffer.Release(); allocator.Dispose();
            Assert.Equal(new[] { 1, 2 }, listener.Events.Select(e => e.Id)); Assert.True(native.ReservedBytes > 0);
        }
        finally { lease.Dispose(); }
        Assert.Equal(0, native.ReservedBytes); Assert.Equal(2, listener.Events.Length);
    }

    [Fact]
    public void ProvidersInSeparateLoadContextsDoNotCollideWithTheDefaultProvider()
    {
        using var mainListener = new Listener();
        var contexts = new[] { new AssemblyLoadContext("buffer tracing one", true), new AssemblyLoadContext("buffer tracing two", true) };
        try
        {
            var sources = new List<EventSource> { BufferAllocatorEventSource.Log };
            foreach (var context in contexts)
            {
                var assembly = context.LoadFromAssemblyPath(typeof(ByteBuf).Assembly.Location);
                var source = (EventSource)assembly.GetType(typeof(BufferAllocatorEventSource).FullName).GetField("Log").GetValue(null);
                Assert.Null(source.ConstructionException); sources.Add(source);
                using var listener = new Listener(source: source);
                Type type = assembly.GetType(typeof(PooledByteBufAllocator).FullName);
                var allocator = (IDisposable)Activator.CreateInstance(type,
                    new object[] { false, true, true, 8192, 1, 0, null, 0, 0, 32768, 8192, default(TimeSpan) });
                using (allocator)
                {
                    object buffer = type.GetMethod("HeapBuffer", new[] { typeof(int), typeof(int) }).Invoke(allocator, new object[] { 17, 23 });
                    buffer.GetType().GetMethod("Release").Invoke(buffer, new object[] { 1 });
                }
                Assert.Equal(new[] { 1, 2 }, listener.Events.Select(e => e.Id));
            }
            Assert.Equal(3, sources.Select(s => s.Name).Distinct().Count());
            Assert.Equal(3, sources.Select(s => s.Guid).Distinct().Count()); Assert.Empty(mainListener.Events);
            var native = new NativeMemoryAllocator(); using var ordinary = NewAllocator(native);
            Allocate(ordinary, false, 17).Release(); Assert.Equal(new[] { 1, 2 }, mainListener.Events.Select(e => e.Id));
        }
        finally { foreach (var context in contexts) context.Unload(); }
    }

    [Fact]
    public void ThrowingListenerCannotPreventReturnOfNativeStorage()
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = new Listener(); listener.Callback = _ => throw new InvalidOperationException("observer failure");
        ByteBuf buffer = Allocate(allocator, true, 16); buffer.Capacity = 128; buffer.Release();
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
        Assert.Equal(4, listener.Events.Count(e => e.Id > 0));
    }

    private static void Check(Record record, int id, int size, int fast, int max, bool direct)
    {
        Assert.True(record.Id == id, record.Id == 0 ? string.Join("; ", record.Fields.Values) : "Unexpected event kind.");
        Assert.Equal(typeof(PooledByteBufAllocator).FullName, record.Fields["allocatorType"]);
        Assert.Equal(size, record.Number("size")); Assert.Equal(fast, record.Number("maxFastCapacity"));
        Assert.Equal(max, record.Number("maxCapacity")); Assert.Equal(direct, record.Flag("direct"));
        Assert.True(direct ? record.Number("address") != 0 : record.Number("address") == 0);
    }

    internal sealed record Record(int Id, string Name, Dictionary<string, object> Fields)
    {
        internal long Number(string field) => Convert.ToInt64(Fields[field]);
        internal bool Flag(string field) => Convert.ToBoolean(Fields[field]);
    }

    internal sealed class Listener : EventListener
    {
        private readonly ConcurrentQueue<Record> _events = new();
        private readonly EventSource _source;
        internal Action<Record> Callback;
        internal Record[] Events => _events.ToArray();
        internal Listener(EventKeywords keywords = (EventKeywords)7, EventSource source = null)
        {
            _source = source ?? BufferAllocatorEventSource.Log;
            EnableEvents(_source, EventLevel.Informational, keywords);
        }
        protected override void OnEventWritten(EventWrittenEventArgs args)
        {
            if (!ReferenceEquals(args.EventSource, _source)) return;
            var fields = args.PayloadNames.Zip(args.Payload).ToDictionary(x => x.First, x => x.Second);
            var record = new Record(args.EventId, args.EventName, fields); _events.Enqueue(record); Callback?.Invoke(record);
        }
    }
}
