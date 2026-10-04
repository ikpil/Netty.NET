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
            Assert.Equal(OperatingSystem.IsAndroid(), InvokeBoolean(platform, nameof(PlatformDependent.IsAndroid))));
    }

    [Fact]
    public void GraalNativeImagePropertiesDoNotEnableReflectiveAccess()
    {
        foreach (string setting in new string[] { null, "false", "true" }) WithFreshPlatform(new()
        {
            ["org.graalvm.nativeimage.imagecode"] = "runtime",
            ["io.netty.tryReflectionSetAccessible"] = setting
        }, platform =>
        {
            // CLR nonpublic member lookup has no mutable JVM accessible flag.
            MethodInfo probe = platform.GetMethod("IsAndroid0", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(probe);
            Assert.Equal(OperatingSystem.IsAndroid(), (bool)probe.Invoke(null, null));
        });
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
            AssertNativeMemoryAndHash(platform);
        });
    }

    [Fact]
    public void ExplicitNettyPreferenceRemainsIndependentOfUnsafeAvailability()
    {
        foreach (var settings in new Dictionary<string, string>[]
        {
            new() { ["io.netty.noUnsafe"] = "true" },
            new() { ["io.netty.noUnsafe"] = "false", ["io.netty.tryUnsafe"] = "false" },
            new() { ["io.netty.noUnsafe"] = "false", ["io.netty.tryUnsafe"] = null,
                ["org.jboss.netty.tryUnsafe"] = "false" }
        })
            WithFreshPlatform(settings, platform =>
            {
                // The former preference diagnostic is retired. JVM preferences must
                // leave actual CLR allocation and arithmetic available.
                AssertNativeMemoryAndHash(platform);
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

            Assembly assembly = context.LoadFromAssemblyPath(typeof(PlatformDependent).Assembly.Location);
            assertion(assembly.GetType(typeof(PlatformDependent).FullName, throwOnError: true));
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

    private static void AssertNativeMemoryAndHash(Type platform)
    {
        Type allocatorType = platform.Assembly.GetType(typeof(NativeMemoryAllocator).FullName, true);
        object allocator = Activator.CreateInstance(allocatorType, new object[] { 16L });
        MethodInfo allocate = allocatorType.GetMethod("Allocate");
        byte[] bytes = new byte[30];
        for (int index = 0; index < bytes.Length; index++)
            bytes[index] = unchecked((byte)(index * 73 + 8 * 17 + 128));
        using (var owner = (IMemoryOwner<byte>)allocate.Invoke(allocator, new object[] { 8, false }))
        {
            bytes.AsSpan(11, 8).CopyTo(owner.Memory.Span);
            Assert.True(owner.Memory.Span.SequenceEqual(bytes.AsSpan(11, 8)));
            Assert.Equal(8L, allocatorType.GetProperty("ReservedBytes").GetValue(allocator));
            byte[] snapshot = owner.Memory.ToArray();
            MethodInfo hash = platform.GetMethod(nameof(PlatformDependent.HashCodeAscii),
                new[] { typeof(byte[]), typeof(int), typeof(int) });
            // Pinned Java eight-byte result from AsciiStringHashContractTest's oracle.
            Assert.Equal(BitConverter.IsLittleEndian ? 277172517 : 205010825,
                (int)hash.Invoke(null, new object[] { snapshot, 0, snapshot.Length }));
        }
        Assert.Equal(0L, allocatorType.GetProperty("ReservedBytes").GetValue(allocator));
    }
}
