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
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class AllocatorLeakConstructionFailureTest
{
    public static IEnumerable<object[]> Cases()
    {
        for (int provider = 0; provider < 4; ++provider)
        for (int method = 0; method < 7; ++method)
        for (int mode = 0; mode < 2; ++mode)
            if (provider != 0 || method != 1) yield return [provider, method, mode];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void DetectorFailureReleasesFactoryOwnedBufferAndPreservesOriginalException(int provider, int method, int mode)
    {
        var context = new AssemblyLoadContext("allocator detector failure", true);
        try
        {
            context.LoadFromAssemblyPath(typeof(ResourceLeakDetectorFactory).Assembly.Location);
            context.LoadFromAssemblyPath(typeof(ByteBuf).Assembly.Location);
            Assembly tests = context.LoadFromAssemblyPath(typeof(AllocatorLeakConstructionFailureTest).Assembly.Location);
            tests.GetType(typeof(AllocatorLeakConstructionFailureTest).FullName)!.GetMethod(nameof(Reproduce), BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [provider, method, mode]);
        }
        catch (TargetInvocationException failure) when (failure.InnerException != null)
        { ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); }
        finally { context.Unload(); }
    }
    private sealed class FailureFactory(int mode) : ResourceLeakDetectorFactory
    {
        public readonly InvalidOperationException Failure = new("detector construction failure");
        public ByteBuf Tracked;
        public bool FailClose;
        public int Closes;
        [Obsolete]
        public override ResourceLeakDetector<T> NewResourceLeakDetector<T>(Type resource, int samplingInterval, long maxActive)
            => typeof(T) == typeof(ByteBuf) ? (ResourceLeakDetector<T>)(object)new Detector(this, mode)
                : new ResourceLeakDetector<T>(resource, samplingInterval);
    }
    private sealed class Detector(FailureFactory state, int mode) : ResourceLeakDetector<ByteBuf>(typeof(ByteBuf), 1)
    {
        public override IResourceLeakTracker<ByteBuf> Track(ByteBuf value)
        {
            state.Tracked = value;
            if (mode == 0) throw state.Failure;
            return new Tracker(state);
        }
        public override bool IsRecordEnabled() => throw state.Failure;
    }
    private sealed class Tracker(FailureFactory state) : IResourceLeakTracker<ByteBuf>
    {
        public void Record() { }
        public void Record(object hint) { }
        public bool Close(ByteBuf value)
        {
            Assert.Same(state.Tracked, value); ++state.Closes;
            if (state.FailClose) throw new InvalidOperationException("secondary close failure");
            return true;
        }
    }
    // Runs in a fresh collectible context so the process-wide detector is installed before ByteBuf initializes.
    private static void Reproduce(int provider, int method, int mode)
    {
        var factory = new FailureFactory(mode); ResourceLeakDetectorFactory.SetResourceLeakDetectorFactory(factory);
        foreach (bool failClose in new[] { false, true })
        {
            factory.FailClose = failClose; factory.Tracked = null; factory.Closes = 0;
            var native = new NativeMemoryAllocator();
            IByteBufAllocator allocator = provider switch
            {
                0 => new UnpooledByteBufAllocator(true, native),
                1 => new PooledByteBufAllocator(true, maxOrder: 1, nativeAllocator: native, smallCacheSize: 2),
                2 => new PooledByteBufAllocator(true, poolHeap: false, poolDirect: false, nativeAllocator: native),
                _ => new AdaptiveByteBufAllocator(true, false, native)
            };
            try
            {
                ByteBuf Request() => method switch
                {
                    0 => allocator.DirectBuffer(64, 128), 1 => allocator.HeapBuffer(64, 128),
                    2 => allocator.CompositeHeapBuffer(2), 3 => allocator.CompositeDirectBuffer(2),
                    4 => allocator.Buffer(64, 128), 5 => allocator.IoBuffer(64, 128), _ => allocator.CompositeBuffer(2)
                };
                Assert.Same(factory.Failure, Assert.Throws<InvalidOperationException>(() => Request()));
                Assert.NotNull(factory.Tracked); Assert.Equal(0, factory.Tracked.ReferenceCount);
                Assert.Equal(mode == 0 ? 0 : 1, factory.Closes);
                if (allocator is PooledByteBufAllocator pooled)
                { Assert.True(pooled.PinnedHeapMemory <= 0); Assert.True(pooled.PinnedDirectMemory <= 0); }
                if (allocator is UnpooledByteBufAllocator unpooled)
                { Assert.Equal(0, unpooled.Metric.UsedHeapMemory); Assert.Equal(0, unpooled.Metric.UsedDirectMemory); }
            }
            finally
            {
                if (factory.Tracked?.ReferenceCount > 0) factory.Tracked.Release();
                (allocator as IDisposable)?.Dispose(); Assert.Equal(0, native.ReservedBytes);
            }
        }
    }
}
