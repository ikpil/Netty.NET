using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Native platform environment")]
public class PlatformBootstrapContractTest
{
    [Theory]
    [InlineData("os.name", "Linux")]
    [InlineData("os.name", "Mac OS X")]
    [InlineData("os.name", "imaginary")]
    [InlineData("os.arch", "i386")]
    [InlineData("os.arch", "aarch64")]
    [InlineData("os.arch", "imaginary")]
    public void ActualRuntimeIdentityIgnoresJvmIdentityOverrides(string name, string value)
    {
        WithPlatform(new() { [name] = value, ["io.netty.osClassifiers"] = "",
            ["io.netty.bitMode"] = "16", ["sun.arch.data.model"] = "128",
            ["com.ibm.vm.bitmode"] = "256", ["java.vm.name"] = "1024-bit JVM" }, platform =>
        {
            Assert.Equal(OperatingSystem.IsWindows(), Invoke(platform, "IsWindows"));
            Assert.Equal(OperatingSystem.IsMacOS(), Invoke(platform, "IsOsx"));
            string os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" :
                OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() ? "linux" :
                OperatingSystem.IsFreeBSD() ? "freebsd" : "unknown";
            Assert.Equal(os, Invoke(platform, "NormalizedOs"));
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x86_64", Architecture.X86 => "x86_32",
                Architecture.Arm or Architecture.Armv6 => "arm_32",
                Architecture.Arm64 => "aarch_64", Architecture.S390x => "s390_64",
                Architecture.LoongArch64 => "loongarch_64", Architecture.Ppc64le => "ppcle_64",
                Architecture.RiscV64 => "riscv64", _ => "unknown"
            };
            Assert.Equal(arch, Invoke(platform, "NormalizedArch"));
        });
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("fedora", "fedora")]
    [InlineData("suse,fedora", "suse,fedora")]
    [InlineData("arch,arch", "arch")]
    [InlineData("ubuntu,debian", "")]
    [InlineData("arch,", "arch")]
    public void CachedClassifiersKeepUniquePriorityAndRejectMutation(string setting, string expected)
    {
        WithPlatform(new() { ["io.netty.osClassifiers"] = setting }, platform =>
        {
            object first = Invoke(platform, "NormalizedLinuxClassifiers");
            Assert.NotNull(first);
            var items = Assert.IsAssignableFrom<IEnumerable<string>>(first);
            var snapshot = new List<string>(items);
            if (expected != null)
                Assert.Equal(expected.Length == 0 ? Array.Empty<string>() : expected.Split(','), items);
            Assert.Equal(snapshot.Count, new HashSet<string>(snapshot, StringComparer.Ordinal).Count);
            Assert.All(snapshot, item => Assert.Contains(item, new[] { "arch", "fedora", "suse" }));
            // Consumers receive a read-only snapshot, never the cached mutable builder.
            if (first is ICollection<string> collection)
            {
                Assert.True(collection.IsReadOnly);
                Assert.Throws<NotSupportedException>(() => collection.Add("arch"));
            }
            Environment.SetEnvironmentVariable("io.netty.osClassifiers", "fedora");
            Assert.Same(first, Invoke(platform, "NormalizedLinuxClassifiers"));
            Assert.Equal(snapshot, items);
        });
    }

    [Theory]
    [InlineData(",")]
    [InlineData("arch,suse,fedora")]
    public void InvalidClassifierPreferenceFailsItsCacheWithoutDisablingNativePlatform(string setting)
    {
        WithPlatform(new() { ["io.netty.osClassifiers"] = setting }, platform =>
        {
            var first = Assert.Throws<TargetInvocationException>(() => Invoke(platform, "NormalizedLinuxClassifiers"));
            Assert.IsType<ArgumentException>(first.InnerException);
            Assert.Equal(OperatingSystem.IsWindows(), Invoke(platform, "IsWindows"));
            Environment.SetEnvironmentVariable("io.netty.osClassifiers", "arch");
            var second = Assert.Throws<TargetInvocationException>(() => Invoke(platform, "NormalizedLinuxClassifiers"));
            Assert.Same(first.InnerException, second.InnerException);
        });
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("ID='arch'\nID_LIKE=\"fedora suse arch\"\n", "arch,fedora,suse")]
    [InlineData("ID=ubuntu\r\nID_LIKE='debian arch'\r\n", "arch")]
    public void OsReleaseFileParsingKeepsAllowedIdentityPriority(string text, string expected)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
            WithPlatform(new() { ["io.netty.osClassifiers"] = "" }, platform =>
            {
                MethodInfo parser = platform.GetMethod("ProcessOsReleaseFile", BindingFlags.NonPublic | BindingFlags.Static);
                var items = new LinkedHashSet<string>();
                Assert.True((bool)parser.Invoke(null, new object[] { path, items }));
                Assert.Equal(expected.Length == 0 ? Array.Empty<string>() : expected.Split(','), items);
                items.Clear();
                Assert.False((bool)parser.Invoke(null, new object[] { path + ".absent", items }));
                Assert.Empty(items);
            });
        }
        finally { File.Delete(path); }
    }

    private static object Invoke(Type type, string name) => type.GetMethod(name).Invoke(null, null);

    private static void WithPlatform(Dictionary<string, string> settings, Action<Type> assertion)
    {
        var saved = new Dictionary<string, string>();
        var context = new AssemblyLoadContext("Netty platform state", isCollectible: true);
        try
        {
            foreach (var setting in settings)
            {
                saved.Add(setting.Key, Environment.GetEnvironmentVariable(setting.Key));
                Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            }
            Assembly assembly = context.LoadFromAssemblyPath(typeof(PlatformDependent).Assembly.Location);
            assertion(assembly.GetType(typeof(PlatformDependent).FullName, true));
        }
        finally
        {
            foreach (var setting in saved) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            context.Unload();
        }
    }
}
