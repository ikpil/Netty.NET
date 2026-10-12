/*
 * Copyright 2014 The Netty Project
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
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// The listener and level belong to the shared detector. Restore both after every case.
[Collection(nameof(AllocatorLeakDetectionCollection))]
public class ByteBufLeakListenerTest : IDisposable
{
    private static readonly FieldInfo ListenerField = typeof(ResourceLeakDetector<ByteBuf>)
        .GetField("leakListener", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static ResourceLeakDetector<ByteBuf>.LeakListener Current
        => (ResourceLeakDetector<ByteBuf>.LeakListener)ListenerField.GetValue(ByteBuf.LeakDetector);
    private readonly ResourceLeakDetector<ByteBuf>.LeakListener _previousListener = Current;
    private readonly ResourceLeakDetectorLevel _previousLevel = ResourceLeakDetector.GetLevel();

    public void Dispose()
    {
        ByteBufUtil.SetLeakListener(_previousListener);
        ResourceLeakDetector.SetLevel(_previousLevel);
    }

    [Fact]
    public void ListenerReplacedAfterCollectionReceivesTheReportOnTheDrainingThread()
    {
        ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.PARANOID);
        string hint = Guid.NewGuid().ToString();
        var first = new Listener(hint); var second = new Listener(hint);
        ByteBufUtil.SetLeakListener(first);
        MakeLeak(hint, out var weak);
        CollectUntil(() => !weak.IsAlive);
        ByteBufUtil.SetLeakListener(second);
        CollectUntil(() => { Drain(); return second.Calls == 1; });
        Assert.Equal(0, first.Calls); Assert.Equal(1, second.Calls);
        Assert.Equal("ByteBuf", second.ResourceType); Assert.Contains(hint, second.Records);
        Assert.Equal(Environment.CurrentManagedThreadId, second.ThreadId);
        Drain(); Drain(); Assert.Equal(1, second.Calls);
    }

    [Fact]
    public void NullRemovesTheListenerBeforeAnAlreadyCollectedLeakIsReported()
    {
        ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.PARANOID);
        string hint = Guid.NewGuid().ToString(); var listener = new Listener(hint);
        ByteBufUtil.SetLeakListener(listener); ByteBufUtil.SetLeakListener(listener);
        var tracker = MakeLeak(hint, out var weak);
        CollectUntil(() => !weak.IsAlive);
        ByteBufUtil.SetLeakListener(null); ByteBufUtil.SetLeakListener(null);
        // The report clears the abandoned tracker's records even without a listener.
        CollectUntil(() => { Drain(); return tracker.ToString().Length == 0; });
        Assert.Equal(0, listener.Calls);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void ListenerDoesNotTurnNormalAccessOrFinalReleaseIntoLeakNotifications(int kind)
    {
        ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.PARANOID);
        var listener = new Listener(); ByteBufUtil.SetLeakListener(listener);
        var native = new NativeMemoryAllocator();
        IByteBufAllocator allocator = kind < 3 ? new UnpooledByteBufAllocator(true, native)
            : new PooledByteBufAllocator(true, maxOrder: 1, nativeAllocator: native);
        ByteBuf root = (kind % 3) switch
        {
            0 => allocator.HeapBuffer(8, 8), 1 => allocator.DirectBuffer(8, 8),
            _ => allocator.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[8]), true)
        };
        try
        {
            root.SetIndex(0, 0).WriteLong(42).Touch("normally-released");
            ByteBuf view = root.RetainedSlice();
            Assert.False(root.Release()); Assert.Equal(42, view.ReadLong()); Assert.True(view.Release());
            // Tracking again drains eligible reports; a normally closed tracker cannot notify.
            ByteBuf next = allocator.DirectBuffer(1, 1);
            try { Assert.Equal(0, listener.Calls); }
            finally { next.Release(); }
        }
        finally
        {
            if (root.ReferenceCount > 0) root.Release(root.ReferenceCount);
            (allocator as IDisposable)?.Dispose();
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IResourceLeakTracker<ByteBuf> MakeLeak(string hint, out WeakReference weak)
    {
        // An automatically tracked, empty heap composite avoids intentionally leaking native memory.
        ByteBuf b = new UnpooledByteBufAllocator().CompositeHeapBuffer().Touch(hint);
        weak = new WeakReference(b.Unwrap());
        return AllocatorLeakDetectionTest.Tracker(b);
    }

    private static void Drain()
    {
        ByteBuf b = new UnpooledHeapByteBuf(UnpooledByteBufAllocator.Default, 1, 1);
        try { Assert.True(ByteBuf.LeakDetector.TrackForcibly(b).Close(b)); }
        finally { b.Release(); }
    }

    private static void CollectUntil(Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            if (done()) return;
            Thread.Sleep(10);
        } while (timer.Elapsed < TimeSpan.FromSeconds(5));
        Assert.True(done());
    }

    private sealed class Listener(string hint = null) : ResourceLeakDetector<ByteBuf>.LeakListener
    {
        internal int Calls, ThreadId;
        internal string ResourceType, Records;
        public void OnLeak(string resourceType, string records)
        {
            // Only count this case's unique leak when unrelated collected buffers are drained.
            if (hint != null && !records.Contains(hint)) return;
            Calls++; ThreadId = Environment.CurrentManagedThreadId;
            ResourceType = resourceType; Records = records;
        }
    }
}
