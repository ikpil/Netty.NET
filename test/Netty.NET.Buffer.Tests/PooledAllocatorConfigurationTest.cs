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
using System.Globalization;
using System.Reflection;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public partial class DefaultByteBufAllocatorTest
{
    private static readonly string[] PooledConfigurationKeys = {
        "io.netty.allocator.pageSize", "io.netty.allocator.maxOrder", "io.netty.allocator.directMemoryCacheAlignment",
        "io.netty.allocator.numHeapArenas", "io.netty.allocator.numDirectArenas", "io.netty.allocator.smallCacheSize",
        "io.netty.allocator.normalCacheSize", "io.netty.allocator.maxCachedBufferCapacity", "io.netty.allocator.cacheTrimInterval",
        "io.netty.allocator.cacheTrimIntervalMillis", "io.netty.allocation.cacheTrimIntervalMillis", "io.netty.allocator.useCacheForAllThreads"
    };
    private static object Field(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static T DefaultSetting<T>(Scope scope, string name) => (T)scope.Type("PooledByteBufAllocator").GetMethod(name).Invoke(null, null);
    private static object Arena(object allocator) => ((Array)Field(allocator, "_heapArenas")).GetValue(0);
    private static void FastScope(Scope scope, Action action)
        => scope.CommonTypeCall("Concurrent.FastThreadLocalThread", "RunWithFastThreadLocal", action);
    private static void MappedScope(Scope scope, Action action)
    {
        Type executorType = scope._common.GetType("Netty.NET.Common.Concurrent.ImmediateEventExecutor", true);
        object executor = executorType.GetField("INSTANCE").GetValue(null);
        Type map = scope._common.GetType("Netty.NET.Common.Internal.ThreadExecutorMap", true);
        MethodInfo apply = map.GetMethod("Apply", new[] { typeof(Action), scope._common.GetType("Netty.NET.Common.Concurrent.IEventExecutor", true) });
        ((Action)apply.Invoke(null, new[] { (object)action, executor }))();
    }

    [Theory]
    [InlineData(8192, 9, 0, 8192, 9, 4194304)]
    [InlineData(4096, 2, 0, 4096, 2, 16384)]
    [InlineData(32768, 1, 64, 32768, 1, 65536)]
    [InlineData(3000, 2, 0, 8192, 2, 32768)]
    [InlineData(8193, 2, 0, 8192, 2, 32768)]
    [InlineData(4096, 2, 8192, 8192, 2, 32768)]
    [InlineData(8192, -1, 0, 8192, 9, 4194304)]
    [InlineData(8192, 15, 0, 8192, 9, 4194304)]
    [InlineData(131072, 14, 0, 131072, 9, 67108864)]
    public void RuntimePooledGeometryPreservesOriginalFallbacks(int page, int order, int alignment, int expectedPage, int expectedOrder, int chunk)
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[0], page.ToString(CultureInfo.InvariantCulture));
        scope.Set(PooledConfigurationKeys[1], order.ToString(CultureInfo.InvariantCulture));
        scope.Set(PooledConfigurationKeys[2], alignment.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expectedPage, DefaultSetting<int>(scope, "DefaultPageSize"));
        Assert.Equal(expectedOrder, DefaultSetting<int>(scope, "DefaultMaxOrder"));
        object allocator = scope.Default; Assert.Equal(chunk, Property<int>(Property<object>(allocator, "Metric"), "ChunkSize"));
        if (chunk < 1000000) CheckFactory(allocator, "DirectBuffer", true);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(-1, 2)] [InlineData(2, -2)] [InlineData(2, 3)]
    public void RuntimeArenaCountsClampAndZeroCountsUseUnpooledFallback(int heap, int direct)
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[3], heap.ToString(CultureInfo.InvariantCulture));
        scope.Set(PooledConfigurationKeys[4], direct.ToString(CultureInfo.InvariantCulture));
        object allocator = scope.Default, metric = Property<object>(allocator, "Metric");
        Assert.Equal(Math.Max(0, heap), DefaultSetting<int>(scope, "DefaultNumHeapArena"));
        Assert.Equal(Math.Max(0, direct), DefaultSetting<int>(scope, "DefaultNumDirectArena"));
        Assert.Equal(Math.Max(0, heap), Property<int>(metric, "NumHeapArenas"));
        Assert.Equal(Math.Max(0, direct), Property<int>(metric, "NumDirectArenas"));
        CheckFactory(allocator, "HeapBuffer", false); CheckFactory(allocator, "DirectBuffer", true);
        Assert.Equal(direct > 0, Property<bool>(allocator, "IsDirectBufferPooled"));
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public void RuntimeCacheEligibilityAndFastScopeRemoval(bool fast, bool mapping, bool all)
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[11], all.ToString());
        scope.Set(PooledConfigurationKeys[5], "3"); scope.Set(PooledConfigurationKeys[6], "3");
        object allocator = scope.Default, arena = Arena(allocator);
        Assert.Equal(3, DefaultSetting<int>(scope, "DefaultSmallCacheSize"));
        Assert.Equal(3, DefaultSetting<int>(scope, "DefaultNormalCacheSize"));
        Action allocate = () =>
        {
            object b = Call(allocator, "HeapBuffer", 16, 16); Call(b, "Release", 1);
            Assert.Equal(fast || mapping || all ? 0L : 1L, Property<long>(arena, "NumSmallDeallocations"));
        };
        Action run = () => { if (mapping) MappedScope(scope, allocate); else allocate(); };
        if (fast) FastScope(scope, run); else run();
        if (fast)
        {
            Assert.Equal(1L, Property<long>(arena, "NumSmallDeallocations"));
            Assert.Equal(0, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
        }
        else Assert.Equal(1, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
    }

    [Theory]
    [InlineData(null, null, 0)] [InlineData("123", null, 123)] [InlineData(null, "456", 456)]
    [InlineData("123", "456", 456)] [InlineData("123", "-1", 0)] [InlineData("123", "bad", 0)]
    public void RuntimeTrimPeriodUsesCurrentPropertyOverDeprecatedAlias(string oldValue, string current, long milliseconds)
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[10], oldValue); scope.Set(PooledConfigurationKeys[9], current);
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), (TimeSpan)Field(scope.Default, "_cacheTrimPeriod"));
    }

    [Fact]
    public void RuntimeConfigurationFreezesAndExplicitConstructorKeepsItsOptions()
    {
        using var scope = new Scope("pooled", null); scope.Set(PooledConfigurationKeys[0], "4096"); scope.Set(PooledConfigurationKeys[1], "2");
        Assert.Equal(4096, DefaultSetting<int>(scope, "DefaultPageSize"));
        scope.Set(PooledConfigurationKeys[0], "16384"); scope.Set(PooledConfigurationKeys[5], "1");
        object allocator = scope.Default;
        Assert.Equal(16384, Property<int>(Property<object>(allocator, "Metric"), "ChunkSize"));
        Assert.Equal(256, DefaultSetting<int>(scope, "DefaultSmallCacheSize"));
        object explicitAllocator = scope.Track(Activator.CreateInstance(scope.Type("PooledByteBufAllocator"),
            false, true, true, 8192, 9, 0, null, 0, 0, 32768, 8192, TimeSpan.Zero));
        Assert.Equal(4194304, Property<int>(Property<object>(explicitAllocator, "Metric"), "ChunkSize"));
        Assert.Equal(0, Property<int>(Property<object>(explicitAllocator, "Metric"), "SmallCacheSize"));
    }

    [Theory]
    [InlineData(-1)] [InlineData(3)]
    public void InvalidRuntimeAlignmentIsRejectedAfterDefaultPageValidation(int alignment)
    {
        using var scope = new Scope("pooled", null); scope.Set(PooledConfigurationKeys[2], alignment.ToString(CultureInfo.InvariantCulture));
        var e = Assert.Throws<TargetInvocationException>(() => { _ = scope.Default; });
        Assert.IsType<ArgumentOutOfRangeException>(e.GetBaseException());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidTrimThresholdFailsOnlyWhenRuntimeCacheIsEligible(bool eligible)
    {
        using var scope = new Scope("pooled", null); scope.Set(PooledConfigurationKeys[8], "0");
        object allocator = scope.Default;
        if (eligible) FastScope(scope, () => Assert.Throws<TargetInvocationException>(() => Call(allocator, "HeapBuffer", 16, 16)));
        else CheckFactory(allocator, "HeapBuffer", false);
        Assert.Equal(eligible ? 0 : 1, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidMaximumCachedCapacityFailsOnlyForEligibleRuntimeBinding(bool eligible)
    {
        using var scope = new Scope("pooled", null); scope.Set(PooledConfigurationKeys[7], "-1");
        object allocator = scope.Default;
        if (eligible) FastScope(scope, () => Assert.Throws<TargetInvocationException>(() => Call(allocator, "HeapBuffer", 16, 16)));
        else CheckFactory(allocator, "HeapBuffer", false);
    }

    [Fact]
    public void NonpositiveCacheSizesDisableCachesEvenWithZeroTrimThreshold()
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[5], "-1"); scope.Set(PooledConfigurationKeys[6], "0"); scope.Set(PooledConfigurationKeys[8], "0");
        Assert.Equal(-1, DefaultSetting<int>(scope, "DefaultSmallCacheSize"));
        FastScope(scope, () => CheckFactory(scope.Default, "HeapBuffer", false));
    }

    [Fact]
    public void RuntimeSchedulingRejectionRollsBackBindingWithoutReservingStorage()
    {
        using var scope = new Scope("pooled", null); scope.Set(PooledConfigurationKeys[9], "10");
        object allocator = scope.Default;
        MappedScope(scope, () => Assert.Throws<TargetInvocationException>(() => Call(allocator, "HeapBuffer", 16, 16)));
        Assert.Equal(0, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
        Assert.Equal(0, scope.NativeBytes);
    }

    [Fact]
    public void RuntimeFastCleanupDoesNotDetachALaterBindingOrInvalidateLiveBuffer()
    {
        using var scope = new Scope("pooled", null); object allocator = scope.Default, buffer = null;
        FastScope(scope, () =>
        {
            object old = Call(allocator, "HeapBuffer", 16, 16); Call(old, "Release", 1);
            Assert.Equal(true, Call(allocator, "ReleaseCurrentThreadBinding"));
            buffer = Call(allocator, "HeapBuffer", 16, 16); Call(buffer, "WriteInt", 123456789);
        });
        Assert.Equal(0, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
        Assert.Equal(123456789, Call(buffer, "GetInt", 0)); Call(buffer, "Release", 1);
        FastScope(scope, () => CheckFactory(allocator, "HeapBuffer", false));
        Assert.Equal(0, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
    }
}
