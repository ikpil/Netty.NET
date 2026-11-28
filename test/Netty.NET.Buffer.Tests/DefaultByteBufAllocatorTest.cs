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
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using System.Threading;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Configuration is process-wide. Fresh collectible assembly contexts exercise static
// initialization without resetting production fields; this collection excludes parallel fixtures.
[Collection(nameof(AllocatorLeakDetectionCollection))]
public class DefaultByteBufAllocatorTest
{
    private sealed class Scope : IDisposable
    {
        private readonly Dictionary<string, string> _previous = new();
        private readonly HashSet<object> _allocators = new(ReferenceEqualityComparer.Instance);
        private readonly AssemblyLoadContext _context = new("allocator configuration", true);
        internal readonly Assembly _buffer, _common;
        internal Scope(string kind, string noPreferDirect)
        {
            Set("io.netty.allocator.type", kind);
            Set("io.netty.noPreferDirect", noPreferDirect);
            Set("io.netty.availableProcessors", "2");
            Set("io.netty.leakDetection.level", "disabled");
            Set("io.netty.allocator.lowMemory", null);

            foreach (string name in PooledConfigurationKeys)
                Set(name, null);

            _common = _context.LoadFromAssemblyPath(typeof(NettyRuntime).Assembly.Location);
            _buffer = _context.LoadFromAssemblyPath(typeof(ByteBuf).Assembly.Location);
        }

        internal void Set(string key, string value)
        {
            if (!_previous.ContainsKey(key))
            {
                _previous.Add(key, Environment.GetEnvironmentVariable(key));
            }

            Environment.SetEnvironmentVariable(key, value);
        }

        internal Type Type(string name)
        {
            return _buffer.GetType("Netty.NET.Buffer." + name, true);
        }
        internal object Default => Track(Type("IByteBufAllocator").GetProperty("Default").GetValue(null));
        internal object UtilityDefault => Type("ByteBufUtil").GetProperty("DefaultAllocator", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        internal object Singleton(string name)
        {
            return Track(Type(name).GetProperty("Default").GetValue(null));
        }

        internal object Adaptive(bool? prefer = null, bool? low = null)
        {
            return Track(Activator.CreateInstance(Type("AdaptiveByteBufAllocator"), prefer, null, low, null));
        }

        internal object Track(object allocator)
        {
            lock (_allocators)
                _allocators.Add(allocator);

            return allocator;
        }

        internal object CommonTypeCall(string type, string method, params object[] args)
        {
            return _common.GetType("Netty.NET.Common." + type, true).GetMethod(method).Invoke(null, args);
        }
        internal long NativeBytes
        {
            get
            {
                Type type = _common.GetType("Netty.NET.Common.NativeMemoryAllocator", true);
                object native = type.GetProperty("Shared").GetValue(null);
                return (long)type.GetProperty("ReservedBytes").GetValue(native);
            }
        }
        internal void CloseAllocators()
        {
            foreach (var allocator in _allocators)
                (allocator as IDisposable)?.Dispose();
        }

        public void Dispose()
        {
            try
            {
                CloseAllocators();
                Assert.Equal(0, NativeBytes);
            }
            finally
            {
                _context.Unload();

                foreach (var pair in _previous)
                {
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                }
            }
        }
    }

    private static object Call(object target, string method, params object[] args)
    {
        return target.GetType().GetMethods().Single(m => m.Name == method && m.GetParameters().Length == args.Length
            && m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(x => x))
            .Invoke(target, args);
    }

    private static T Property<T>(object target, string name)
    {
        return (T)target.GetType().GetProperty(name).GetValue(target);
    }

    private static void CheckFactory(object allocator, string factory, bool direct)
    {
        object b = Call(allocator, factory, 9, 64);

        try
        {
            Assert.Equal(direct, Property<bool>(b, "IsDirect"));
            Assert.Equal(9, Property<int>(b, "Capacity"));
            Assert.Same(allocator, Call(b, "Alloc"));
            Call(b, "WriteInt", 123456789);
            object copy = Call(b, "Copy");

            try
            {
                Assert.Same(allocator, Call(copy, "Alloc"));
                Assert.Equal(123456789, Call(copy, "GetInt", 0));
            }
            finally
            {
                Call(copy, "Release", 1);
            }
        }
        finally
        {
            Call(b, "Release", 1);
        }
    }

    public static IEnumerable<object[]> SelectionCases()
    {
        foreach (string kind in new string[] { null, "adaptive", "unpooled", "pooled", "unknown", "Adaptive", " pooled ", "" })
        foreach (string noPrefer in new string[] { null, "true", "false" })
            yield return new object[] { kind, noPrefer, kind == null || kind == "adaptive" ? "AdaptiveByteBufAllocator"
                : kind == "unpooled" ? "UnpooledByteBufAllocator" : "PooledByteBufAllocator" };
    }

    [Theory]
    [MemberData(nameof(SelectionCases))]
    public void ExactSelectionPreferenceAndPublicFactories(string kind, string noPreferDirect, string expected)
    {
        using var scope = new Scope(kind, noPreferDirect);
        object allocator = scope.Default;
        Assert.Equal(expected, allocator.GetType().Name);
        Assert.Same(allocator, scope.Default);
        Assert.Same(allocator, scope.UtilityDefault);

        if (expected != "AdaptiveByteBufAllocator")
            Assert.Same(allocator, scope.Singleton(expected));

        bool direct = noPreferDirect != "true";
        CheckFactory(allocator, "Buffer", direct);
        CheckFactory(allocator, "HeapBuffer", false);
        CheckFactory(allocator, "DirectBuffer", true);
        CheckFactory(allocator, "IoBuffer", true);
        Assert.Equal(expected != "UnpooledByteBufAllocator", Property<bool>(allocator, "IsDirectBufferPooled"));
        scope.CloseAllocators();
        Assert.Equal(0, scope.NativeBytes);
    }

    [Theory]
    [InlineData("unpooled")] [InlineData("pooled")] [InlineData("adaptive")] [InlineData("utility")]
    public void SingletonAndUtilityFirstInitializationAvoidsCycles(string first)
    {
        using var scope = new Scope("adaptive", "true");

        if (first == "utility")
            Assert.Equal(0x04030201, scope.Type("ByteBufUtil").GetMethod("SwapInt").Invoke(null, new object[] { 0x01020304 }));
        else if (first == "adaptive")
            scope.Adaptive();
        else
            scope.Singleton(first == "unpooled" ? "UnpooledByteBufAllocator" : "PooledByteBufAllocator");

        Assert.Equal("AdaptiveByteBufAllocator", scope.Default.GetType().Name);
        Assert.Same(scope.Default, scope.UtilityDefault);
        CheckFactory(scope.Default, "Buffer", false);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelectionAndPreferenceFreezeAfterInitialization(bool noPreferDirect)
    {
        using var scope = new Scope("unpooled", noPreferDirect.ToString());
        object first = scope.Default;
        scope.Set("io.netty.allocator.type", "adaptive");
        scope.Set("io.netty.noPreferDirect", (!noPreferDirect).ToString());
        Assert.Same(first, scope.Default);
        Assert.Equal("UnpooledByteBufAllocator", scope.Default.GetType().Name);
        CheckFactory(scope.Singleton("PooledByteBufAllocator"), "Buffer", !noPreferDirect);
        CheckFactory(scope.Adaptive(), "Buffer", !noPreferDirect);
        CheckFactory(scope.Adaptive(true), "Buffer", true);
        CheckFactory(scope.Adaptive(false), "Buffer", false);
    }

    [Fact]
    public void UtilityCallsDoNotInitializeGlobalAllocatorOrProcessorPolicy()
    {
        using var scope = new Scope("pooled", null);
        Assert.Equal(0x04030201, scope.Type("ByteBufUtil").GetMethod("SwapInt").Invoke(null, new object[] { 0x01020304 }));
        scope.Set("io.netty.allocator.type", "adaptive");
        scope.CommonTypeCall("NettyRuntime", "SetAvailableProcessors", 3);
        object allocator = scope.Default;
        Assert.Equal("AdaptiveByteBufAllocator", allocator.GetType().Name);
        Assert.Equal(3, scope.CommonTypeCall("NettyRuntime", "AvailableProcessors"));
    }

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(8)]
    public void AdaptiveSharedStripesAndReuseUseConfiguredProcessorCount(int processors)
    {
        using var scope = new Scope("adaptive", null);
        scope.Set("io.netty.availableProcessors", processors.ToString(CultureInfo.InvariantCulture));
        scope.Set("io.netty.allocator.chunkReuseQueueCapacity", null);
        object allocator = scope.Default;

        foreach (string name in new[] { "HeapPool", "DirectPool" })
        {
            object pool = allocator.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(allocator);
            var groups = (Array)pool.GetType().GetField("_sizeClassedMagazineGroups", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pool);
            object group = groups.GetValue(0);
            // Magazine groups read the pool's per-instance MAX_STRIPES replacement.
            Assert.Equal(processors * 2, pool.GetType().GetField("_maxStripes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pool));
            object cache = group.GetType().GetField("_chunkCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(group);
            Assert.Equal(processors * 2, cache.GetType().GetField("_chunkReuseQueue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cache));
        }
    }

    [Theory]
    [InlineData(null, null, false)] [InlineData("true", null, true)] [InlineData("false", null, false)]
    [InlineData("true", false, false)] [InlineData("false", true, true)]
    public void ExplicitLowMemorySettingControlsGlobalAndCanBeOverridden(string setting, bool? low, bool expected)
    {
        using var scope = new Scope("adaptive", null);
        scope.Set("io.netty.allocator.lowMemory", setting);
        Check(scope.Default, setting == "true");
        Check(scope.Adaptive(low: low), expected);

        static void Check(object allocator, bool lowMemory)
        {
            object b = Call(allocator, "HeapBuffer", 32768, 32768);

            try
            {
                Assert.Equal(lowMemory ? 32768L : 262144L, Property<long>(allocator, "UsedHeapMemory"));
            }
            finally
            {
                Call(b, "Release", 1);
            }

            Assert.Equal(lowMemory ? 0L : 262144L, Property<long>(allocator, "UsedHeapMemory"));
        }
    }

    [Theory]
    [InlineData("adaptive")] [InlineData("unpooled")] [InlineData("pooled")] [InlineData("unknown")]
    public void ConcurrentFirstAccessPublishesOneUsableInstance(string kind)
    {
        using var scope = new Scope(kind, null);
        var allocators = new object[32];
        Parallel.For(0, allocators.Length, i => allocators[i] = scope.Default);
        Assert.All(allocators, a => Assert.Same(allocators[0], a));
        CheckFactory(allocators[0], "Buffer", true);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnpooledHelpersKeepHeapSemanticsWithAnyGlobalPolicy(bool noPreferDirect)
    {
        using var scope = new Scope("adaptive", noPreferDirect.ToString());
        object b = scope.Type("Unpooled").GetMethod("Buffer").Invoke(null, new object[] { 9, 64 });

        try
        {
            Assert.False(Property<bool>(b, "IsDirect"));
            Assert.Same(scope.Singleton("UnpooledByteBufAllocator"), Call(b, "Alloc"));
        }
        finally
        {
            Call(b, "Release", 1);
        }

        Assert.Equal("AdaptiveByteBufAllocator", scope.Default.GetType().Name);
    }

    private static readonly string[] PooledConfigurationKeys = {
        "io.netty.allocator.pageSize", "io.netty.allocator.maxOrder", "io.netty.allocator.directMemoryCacheAlignment",
        "io.netty.allocator.numHeapArenas", "io.netty.allocator.numDirectArenas", "io.netty.allocator.smallCacheSize",
        "io.netty.allocator.normalCacheSize", "io.netty.allocator.maxCachedBufferCapacity", "io.netty.allocator.cacheTrimInterval",
        "io.netty.allocator.cacheTrimIntervalMillis", "io.netty.allocation.cacheTrimIntervalMillis", "io.netty.allocator.useCacheForAllThreads"
    };
    private static object Field(object target, string name)
    {
        return target.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }

    private static T DefaultSetting<T>(Scope scope, string name)
    {
        return (T)scope.Type("PooledByteBufAllocator").GetMethod(name).Invoke(null, null);
    }

    private static object Arena(object allocator)
    {
        return ((Array)Field(allocator, "_heapArenas")).GetValue(0);
    }

    private static void FastScope(Scope scope, Action action)
    {
        scope.CommonTypeCall("Concurrent.FastThreadLocalThread", "RunWithFastThreadLocal", action);
    }

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
        object allocator = scope.Default;
        Assert.Equal(chunk, Property<int>(Property<object>(allocator, "Metric"), "ChunkSize"));

        if (chunk < 1000000)
            CheckFactory(allocator, "DirectBuffer", true);
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
        CheckFactory(allocator, "HeapBuffer", false);
        CheckFactory(allocator, "DirectBuffer", true);
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
        scope.Set(PooledConfigurationKeys[5], "3");
        scope.Set(PooledConfigurationKeys[6], "3");
        object allocator = scope.Default, arena = Arena(allocator);
        Assert.Equal(3, DefaultSetting<int>(scope, "DefaultSmallCacheSize"));
        Assert.Equal(3, DefaultSetting<int>(scope, "DefaultNormalCacheSize"));
        Action allocate = () =>
        {
            object b = Call(allocator, "HeapBuffer", 16, 16);
            Call(b, "Release", 1);
            Assert.Equal(fast || mapping || all ? 0L : 1L, Property<long>(arena, "NumSmallDeallocations"));
        };
        Action run = () => {
            if (mapping)
            {
                MappedScope(scope, allocate);
            }
            else
            {
                allocate();
            }
        };

        if (fast)
            FastScope(scope, run);
        else
            run();

        if (fast)
        {
            Assert.Equal(1L, Property<long>(arena, "NumSmallDeallocations"));
            Assert.Equal(0, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
        }
        else
            Assert.Equal(1, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
    }

    [Theory]
    [InlineData(null, null, 0)] [InlineData("123", null, 123)] [InlineData(null, "456", 456)]
    [InlineData("123", "456", 456)] [InlineData("123", "-1", 0)] [InlineData("123", "bad", 0)]
    public void RuntimeTrimPeriodUsesCurrentPropertyOverDeprecatedAlias(string oldValue, string current, long milliseconds)
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[10], oldValue);
        scope.Set(PooledConfigurationKeys[9], current);
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), (TimeSpan)Field(scope.Default, "_cacheTrimPeriod"));
    }

    [Fact]
    public void RuntimeConfigurationFreezesAndExplicitConstructorKeepsItsOptions()
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[0], "4096");
        scope.Set(PooledConfigurationKeys[1], "2");
        Assert.Equal(4096, DefaultSetting<int>(scope, "DefaultPageSize"));
        scope.Set(PooledConfigurationKeys[0], "16384");
        scope.Set(PooledConfigurationKeys[5], "1");
        object allocator = scope.Default;
        Assert.Equal(16384, Property<int>(Property<object>(allocator, "Metric"), "ChunkSize"));
        Assert.Equal(256, DefaultSetting<int>(scope, "DefaultSmallCacheSize"));
        object explicitAllocator = scope.Track(Activator.CreateInstance(scope.Type("PooledByteBufAllocator"),
            false, true, true, 8192, 9, 0, 0, null, 0, null, 32768, 8192, TimeSpan.Zero));
        Assert.Equal(4194304, Property<int>(Property<object>(explicitAllocator, "Metric"), "ChunkSize"));
        Assert.Equal(0, Property<int>(Property<object>(explicitAllocator, "Metric"), "SmallCacheSize"));
    }

    [Theory]
    [InlineData(-1)] [InlineData(3)]
    public void InvalidRuntimeAlignmentIsRejectedAfterDefaultPageValidation(int alignment)
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[2], alignment.ToString(CultureInfo.InvariantCulture));
        var e = Assert.Throws<TargetInvocationException>(() =>
        {
            _ = scope.Default;
        });
        Assert.IsType<ArgumentOutOfRangeException>(e.GetBaseException());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidTrimThresholdFailsOnlyWhenRuntimeCacheIsEligible(bool eligible)
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[8], "0");
        object allocator = scope.Default;

        if (eligible)
            FastScope(scope, () => Assert.Throws<TargetInvocationException>(() => Call(allocator, "HeapBuffer", 16, 16)));
        else
            CheckFactory(allocator, "HeapBuffer", false);

        Assert.Equal(eligible ? 0 : 1, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidMaximumCachedCapacityFailsOnlyForEligibleRuntimeBinding(bool eligible)
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[7], "-1");
        object allocator = scope.Default;

        if (eligible)
            FastScope(scope, () => Assert.Throws<TargetInvocationException>(() => Call(allocator, "HeapBuffer", 16, 16)));
        else
            CheckFactory(allocator, "HeapBuffer", false);
    }

    [Fact]
    public void NonpositiveCacheSizesDisableCachesEvenWithZeroTrimThreshold()
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[5], "-1");
        scope.Set(PooledConfigurationKeys[6], "0");
        scope.Set(PooledConfigurationKeys[8], "0");
        Assert.Equal(-1, DefaultSetting<int>(scope, "DefaultSmallCacheSize"));
        FastScope(scope, () => CheckFactory(scope.Default, "HeapBuffer", false));
    }

    [Fact]
    public void RuntimeSchedulingRejectionRollsBackBindingWithoutReservingStorage()
    {
        using var scope = new Scope("pooled", null);
        scope.Set(PooledConfigurationKeys[9], "10");
        object allocator = scope.Default;
        MappedScope(scope, () => Assert.Throws<TargetInvocationException>(() => Call(allocator, "HeapBuffer", 16, 16)));
        Assert.Equal(0, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
        Assert.Equal(0, scope.NativeBytes);
    }

    [Fact]
    public void RuntimeFastCleanupDoesNotDetachALaterBindingOrInvalidateLiveBuffer()
    {
        using var scope = new Scope("pooled", null);
        object allocator = scope.Default, buffer = null;
        FastScope(scope, () =>
        {
            object old = Call(allocator, "HeapBuffer", 16, 16);
            Call(old, "Release", 1);
            Assert.Equal(true, Call(allocator, "ReleaseCurrentThreadBinding"));
            buffer = Call(allocator, "HeapBuffer", 16, 16);
            Call(buffer, "WriteInt", 123456789);
        });
        Assert.Equal(0, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
        Assert.Equal(123456789, Call(buffer, "GetInt", 0));
        Call(buffer, "Release", 1);
        FastScope(scope, () => CheckFactory(allocator, "HeapBuffer", false));
        Assert.Equal(0, Property<int>(Property<object>(allocator, "Metric"), "NumThreadLocalCaches"));
    }

    [Theory]
    [InlineData(null, false)] [InlineData("0", false)] [InlineData("-1", false)]
    [InlineData("invalid", false)] [InlineData("1", true)] [InlineData("256", true)] [InlineData("1024", true)]
    public void ThreadLocalDirectSettingsSnapshotWithoutInitializingGlobalAllocator(string size, bool enabled)
    {
        using var scope = new Scope("invalid", "true");
        scope.Set("io.netty.threadLocalDirectBufferSize", size);
        // If the transport utility initialized the global allocator, this poison
        // pooled geometry would reject its unknown-selector fallback.
        scope.Set("io.netty.allocator.directMemoryCacheAlignment", "3");
        object b = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
        Assert.Equal(enabled, b != null);

        if (b != null)
        {
            Assert.True(Property<bool>(b, "IsDirect"));
            Assert.Equal(256, Property<int>(b, "Capacity"));
            Assert.Equal(1, Property<int>(b, "RefCnt"));
            Call(b, "Release", 1);
            Assert.Equal(0, scope.NativeBytes); // Ordinary threads do not retain idle storage.
        }

        scope.Set("io.netty.threadLocalDirectBufferSize", enabled ? "0" : "1024");
        object again = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
        Assert.Equal(enabled, again != null);

        if (again != null)
            Call(again, "Release", 1);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ThreadLocalDirectGuaranteedFastScopeCachesAndDrainsOnSuccessOrFailure(bool fail)
    {
        using var scope = new Scope(null, null);
        scope.Set("io.netty.threadLocalDirectBufferSize", "1024");
        Action action = () =>
        {
            object first = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
            Call(first, "SetByte", 0, 72);
            Call(first, "Release", 1);
            Assert.Equal(256, scope.NativeBytes);
            object next = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
            Assert.NotSame(first, next);
            Assert.Equal((byte)72, Call(next, "GetByte", 0));
            Call(next, "Release", 1);
            if (fail)
                throw new InvalidOperationException("scope failure");
        };
        Exception error = Record.Exception(() => scope.CommonTypeCall("Concurrent.FastThreadLocalThread", "RunWithFastThreadLocal", action));
        Assert.Equal(fail, error != null);
        Assert.Equal(0, scope.NativeBytes);
        scope.CommonTypeCall("Concurrent.FastThreadLocalThread", "RunWithFastThreadLocal", (Action)(() =>
        {
            object b = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
            Assert.Equal((byte)0, Call(b, "GetByte", 0));
            Call(b, "Release", 1);
        }));
        Assert.Equal(0, scope.NativeBytes);
    }

    [Fact]
    public void ThreadLocalDirectActualFastThreadTerminationDrainsIdleStorageButKeepsLiveResultValid()
    {
        using var scope = new Scope(null, null);
        scope.Set("io.netty.threadLocalDirectBufferSize", "1024");
        object live = null;
        Exception failure = null;
        Action work = () =>
        {
            try
            {
                object idle = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
                live = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
                Call(idle, "Release", 1);
                Call(live, "WriteByte", 71);
                Assert.Equal(512, scope.NativeBytes);
            }
            catch (Exception e)
            {
                failure = e;
            }
        };
        Type type = scope._common.GetType("Netty.NET.Common.Concurrent.FastThreadLocalThread", true);
        object fast = Activator.CreateInstance(type, work, null, 0, null);
        Thread thread = (Thread)type.GetProperty("Thread").GetValue(fast);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
        Assert.Equal(256, scope.NativeBytes);
        Assert.Equal((byte)71, Call(live, "ReadByte"));
        Call(live, "Release", 1);
        Assert.Equal(0, scope.NativeBytes);
    }
}
