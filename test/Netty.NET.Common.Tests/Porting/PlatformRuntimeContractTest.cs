using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

[CollectionDefinition("Native platform environment", DisableParallelization = true)]
public sealed class NativePlatformEnvironmentCollection;

[Collection("Native platform environment")]
public class PlatformRuntimeContractTest
{
    [Fact]
    public void AndroidDetectionUsesTheOperatingSystemRatherThanTheJvmName()
    {
        WithFreshPlatform(new() { ["java.vm.name"] = "Dalvik" }, platform =>
            Assert.Equal(OperatingSystem.IsAndroid(), InvokeBoolean(platform, "isAndroid")));
    }

    [Fact]
    public void GraalNativeImagePropertiesDoNotEnableReflectiveAccess()
    {
        WithFreshPlatform(new()
        {
            ["org.graalvm.nativeimage.imagecode"] = "runtime",
            ["io.netty.tryReflectionSetAccessible"] = null
        }, platform => Assert.False(InvokeBoolean(platform, "isExplicitTryReflectionSetAccessible")));
    }

    [Fact]
    public void JdkUnsafeAccessPoliciesDoNotSelectClrCapabilities()
    {
        WithFreshPlatform(new()
        {
            ["sun.misc.unsafe.memory.access"] = "deny",
            ["io.netty.noUnsafe"] = "false",
            ["io.netty.tryUnsafe"] = "true",
            ["org.jboss.netty.tryUnsafe"] = "true"
        }, platform =>
        {
            Assert.False(InvokeBoolean(platform, "isExplicitNoUnsafe"));
            Assert.False(InvokeBoolean(platform, "hasUnsafe"));
        });
    }

    [Fact]
    public void ExplicitNettyPreferenceRemainsIndependentOfUnsafeAvailability()
    {
        WithFreshPlatform(new() { ["io.netty.noUnsafe"] = "true" }, platform =>
        {
            Assert.True(InvokeBoolean(platform, "isExplicitNoUnsafe"));
            Assert.False(InvokeBoolean(platform, "hasUnsafe"));
        });
    }

    [Fact]
    public void SharedNativeAllocatorAppliesTheExplicitNettyLimitToAllItsOwners()
    {
        WithFreshPlatform(new() { ["io.netty.maxDirectMemory"] = "8" }, platform =>
        {
            Type allocatorType = platform.Assembly.GetType(typeof(NativeMemoryAllocator).FullName, true);
            object allocator = allocatorType.GetProperty("Shared").GetValue(null);
            Assert.Same(allocator, allocatorType.GetProperty("Shared").GetValue(null));
            Assert.Equal(8L, allocatorType.GetProperty("MaximumBytes").GetValue(allocator));
            MethodInfo allocate = allocatorType.GetMethod("Allocate");
            using (var owner = (IMemoryOwner<byte>)allocate.Invoke(allocator, new object[] { 8, true }))
            {
                Assert.Equal(8L, allocatorType.GetProperty("ReservedBytes").GetValue(allocator));
                var failure = Assert.Throws<TargetInvocationException>(() =>
                    allocate.Invoke(allocator, new object[] { 1, false }));
                Assert.IsType<OutOfMemoryException>(failure.InnerException);
                owner.Memory.Span[0] = 0xa5;
            }
            Assert.Equal(0L, allocatorType.GetProperty("ReservedBytes").GetValue(allocator));
        });
    }

    // Independent static initialization makes each probe exercise its settings.
    // The exclusive collection prevents environment mutations affecting other tests.
    private static void WithFreshPlatform(Dictionary<string, string> settings, Action<Type> assertion)
    {
        var saved = new Dictionary<string, string>();
        var context = new AssemblyLoadContext("Netty platform probe", isCollectible: true);
        try
        {
            foreach (var setting in settings)
            {
                saved.Add(setting.Key, Environment.GetEnvironmentVariable(setting.Key));
                Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            }

            Assembly assembly = context.LoadFromAssemblyPath(typeof(PlatformDependent0).Assembly.Location);
            assertion(assembly.GetType(typeof(PlatformDependent0).FullName, throwOnError: true));
        }
        finally
        {
            foreach (var setting in saved)
                Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            context.Unload();
        }
    }

    private static bool InvokeBoolean(Type platform, string name) =>
        (bool)platform.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
}
