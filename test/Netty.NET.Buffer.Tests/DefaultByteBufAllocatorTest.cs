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
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Configuration is process-wide. Fresh collectible assembly contexts exercise static
// initialization without resetting production fields; this collection excludes parallel fixtures.
[Collection(nameof(AllocatorLeakDetectionCollection))]
public partial class DefaultByteBufAllocatorTest
{
    private sealed class Scope : IDisposable
    {
        private readonly Dictionary<string, string> _previous = new();
        private readonly HashSet<object> _allocators = new(ReferenceEqualityComparer.Instance);
        private readonly AssemblyLoadContext _context = new("allocator configuration", true);
        internal readonly Assembly _buffer, _common;
        internal Scope(string kind, string noPreferDirect)
        {
            Set("io.netty.allocator.type", kind); Set("io.netty.noPreferDirect", noPreferDirect);
            Set("io.netty.availableProcessors", "2"); Set("io.netty.leakDetection.level", "disabled");
            Set("io.netty.allocator.lowMemory", null);
            foreach (string name in PooledConfigurationKeys) Set(name, null);
            _common = _context.LoadFromAssemblyPath(typeof(NettyRuntime).Assembly.Location);
            _buffer = _context.LoadFromAssemblyPath(typeof(ByteBuf).Assembly.Location);
        }
        internal void Set(string key, string value)
        {
            if (!_previous.ContainsKey(key)) _previous.Add(key, Environment.GetEnvironmentVariable(key));
            Environment.SetEnvironmentVariable(key, value);
        }
        internal Type Type(string name) => _buffer.GetType("Netty.NET.Buffer." + name, true);
        internal object Default => Track(Type("IByteBufAllocator").GetProperty("Default").GetValue(null));
        internal object UtilityDefault => Type("ByteBufUtil").GetProperty("DefaultAllocator", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        internal object Singleton(string name) => Track(Type(name).GetProperty("Default").GetValue(null));
        internal object Adaptive(bool? prefer = null, bool? low = null) => Track(Activator.CreateInstance(Type("AdaptiveByteBufAllocator"), prefer, low, null, null));
        internal object Track(object allocator) { lock (_allocators) _allocators.Add(allocator); return allocator; }
        internal object CommonTypeCall(string type, string method, params object[] args)
            => _common.GetType("Netty.NET.Common." + type, true).GetMethod(method).Invoke(null, args);
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
        { foreach (var allocator in _allocators) (allocator as IDisposable)?.Dispose(); }
        public void Dispose()
        {
            try { CloseAllocators(); Assert.Equal(0, NativeBytes); }
            finally
            {
                _context.Unload();
                foreach (var pair in _previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    private static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethods().Single(m => m.Name == method && m.GetParameters().Length == args.Length
            && m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(x => x))
            .Invoke(target, args);
    private static T Property<T>(object target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);
    private static void CheckFactory(object allocator, string factory, bool direct)
    {
        object b = Call(allocator, factory, 9, 64);
        try
        {
            Assert.Equal(direct, Property<bool>(b, "IsDirect")); Assert.Equal(9, Property<int>(b, "Capacity"));
            Assert.Same(allocator, Property<object>(b, "Allocator")); Call(b, "WriteInt", 123456789);
            object copy = Call(b, "Copy");
            try { Assert.Same(allocator, Property<object>(copy, "Allocator")); Assert.Equal(123456789, Call(copy, "GetInt", 0)); }
            finally { Call(copy, "Release", 1); }
        }
        finally { Call(b, "Release", 1); }
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
        using var scope = new Scope(kind, noPreferDirect); object allocator = scope.Default;
        Assert.Equal(expected, allocator.GetType().Name); Assert.Same(allocator, scope.Default);
        Assert.Same(allocator, scope.UtilityDefault);
        if (expected != "AdaptiveByteBufAllocator") Assert.Same(allocator, scope.Singleton(expected));
        bool direct = noPreferDirect != "true";
        CheckFactory(allocator, "Buffer", direct); CheckFactory(allocator, "HeapBuffer", false);
        CheckFactory(allocator, "DirectBuffer", true); CheckFactory(allocator, "IoBuffer", true);
        Assert.Equal(expected != "UnpooledByteBufAllocator", Property<bool>(allocator, "IsDirectBufferPooled"));
        scope.CloseAllocators(); Assert.Equal(0, scope.NativeBytes);
    }

    [Theory]
    [InlineData("unpooled")] [InlineData("pooled")] [InlineData("adaptive")] [InlineData("utility")]
    public void SingletonAndUtilityFirstInitializationAvoidsCycles(string first)
    {
        using var scope = new Scope("adaptive", "true");
        if (first == "utility") Assert.Equal(0x04030201, scope.Type("ByteBufUtil").GetMethod("SwapInt").Invoke(null, new object[] { 0x01020304 }));
        else if (first == "adaptive") scope.Adaptive();
        else scope.Singleton(first == "unpooled" ? "UnpooledByteBufAllocator" : "PooledByteBufAllocator");
        Assert.Equal("AdaptiveByteBufAllocator", scope.Default.GetType().Name); Assert.Same(scope.Default, scope.UtilityDefault);
        CheckFactory(scope.Default, "Buffer", false);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelectionAndPreferenceFreezeAfterInitialization(bool noPreferDirect)
    {
        using var scope = new Scope("unpooled", noPreferDirect.ToString()); object first = scope.Default;
        scope.Set("io.netty.allocator.type", "adaptive"); scope.Set("io.netty.noPreferDirect", (!noPreferDirect).ToString());
        Assert.Same(first, scope.Default); Assert.Equal("UnpooledByteBufAllocator", scope.Default.GetType().Name);
        CheckFactory(scope.Singleton("PooledByteBufAllocator"), "Buffer", !noPreferDirect);
        CheckFactory(scope.Adaptive(), "Buffer", !noPreferDirect);
        CheckFactory(scope.Adaptive(true), "Buffer", true); CheckFactory(scope.Adaptive(false), "Buffer", false);
    }

    [Fact]
    public void UtilityCallsDoNotInitializeGlobalAllocatorOrProcessorPolicy()
    {
        using var scope = new Scope("pooled", null);
        Assert.Equal(0x04030201, scope.Type("ByteBufUtil").GetMethod("SwapInt").Invoke(null, new object[] { 0x01020304 }));
        scope.Set("io.netty.allocator.type", "adaptive");
        scope.CommonTypeCall("NettyRuntime", "SetAvailableProcessors", 3);
        object allocator = scope.Default; Assert.Equal("AdaptiveByteBufAllocator", allocator.GetType().Name);
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
            var groups = (Array)pool.GetType().GetField("_small", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pool);
            object group = groups.GetValue(0);
            Assert.Equal(processors * 2, group.GetType().GetField("_maxStripes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(group));
            object cache = group.GetType().GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(group);
            Assert.Equal(processors * 2, cache.GetType().GetField("_retentionFloor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cache));
        }
    }

    [Theory]
    [InlineData(null, null, false)] [InlineData("true", null, true)] [InlineData("false", null, false)]
    [InlineData("true", false, false)] [InlineData("false", true, true)]
    public void ExplicitLowMemorySettingControlsGlobalAndCanBeOverridden(string setting, bool? low, bool expected)
    {
        using var scope = new Scope("adaptive", null); scope.Set("io.netty.allocator.lowMemory", setting);
        Check(scope.Default, setting == "true"); Check(scope.Adaptive(low: low), expected);
        static void Check(object allocator, bool lowMemory)
        {
            object b = Call(allocator, "HeapBuffer", 32768, 32768);
            try { Assert.Equal(lowMemory ? 32768L : 262144L, Property<long>(allocator, "UsedHeapMemory")); }
            finally { Call(b, "Release", 1); }
            Assert.Equal(lowMemory ? 0L : 262144L, Property<long>(allocator, "UsedHeapMemory"));
        }
    }

    [Theory]
    [InlineData("adaptive")] [InlineData("unpooled")] [InlineData("pooled")] [InlineData("unknown")]
    public void ConcurrentFirstAccessPublishesOneUsableInstance(string kind)
    {
        using var scope = new Scope(kind, null); var allocators = new object[32];
        Parallel.For(0, allocators.Length, i => allocators[i] = scope.Default);
        Assert.All(allocators, a => Assert.Same(allocators[0], a)); CheckFactory(allocators[0], "Buffer", true);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnpooledHelpersKeepHeapSemanticsWithAnyGlobalPolicy(bool noPreferDirect)
    {
        using var scope = new Scope("adaptive", noPreferDirect.ToString());
        object b = scope.Type("Unpooled").GetMethod("Buffer").Invoke(null, new object[] { 9, 64 });
        try { Assert.False(Property<bool>(b, "IsDirect")); Assert.Same(scope.Singleton("UnpooledByteBufAllocator"), Property<object>(b, "Allocator")); }
        finally { Call(b, "Release", 1); }
        Assert.Equal("AdaptiveByteBufAllocator", scope.Default.GetType().Name);
    }
}
