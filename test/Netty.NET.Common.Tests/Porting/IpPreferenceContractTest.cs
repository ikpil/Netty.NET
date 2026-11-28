using System;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;

namespace Netty.NET.Common.Tests.Porting;

[Collection("System properties")]
public class IpPreferenceContractTest
{
    private const string IPv4Key = "java.net.preferIPv4Stack";
    private const string IPv6Key = "java.net.preferIPv6Addresses";

    [Fact]
    public void PreferencesAreReadOnlyNativePropertiesWithoutGetterAliases()
    {
        Type type = typeof(NetUtil);

        foreach (string name in new[] { "IsIpV4StackPreferred", "IsIpV6AddressesPreferred" })
        {
            PropertyInfo property = type.GetProperty(name);
            Assert.NotNull(property);
            Assert.Equal(typeof(bool), property.PropertyType);
            Assert.True(property.GetMethod.IsStatic);
            Assert.True(property.GetMethod.IsPublic);
            Assert.Null(property.SetMethod);
        }

        Assert.Null(type.GetProperty("PreferIPv4Stack"));
        Assert.Null(type.GetProperty("PreferIPv6Addresses"));
    }

    [Theory]
    [InlineData(null, null, false, false)]
    [InlineData("false", "false", false, false)]
    [InlineData("true", "true", true, true)]
    [InlineData("yes", "yes", true, false)]
    [InlineData("1", "1", true, false)]
    [InlineData("", "", false, false)]
    [InlineData("no", "no", false, false)]
    [InlineData("0", "0", false, false)]
    [InlineData("system", "system", false, false)]
    [InlineData(" \tTrUe\r\n", " \tTrUe\r\n", true, true)]
    [InlineData("\u00a0true\u2003", "\u00a0true\u2003", true, true)]
    [InlineData("\u001ftrue\u001f", "\u001ftrue\u001f", false, false)]
    [InlineData("invalid", "invalid", false, false)]
    [InlineData("false", "true", false, true)]
    public void CapturedSettingsPreserveIndependentPolicies(string ipv4, string ipv6, bool expected4, bool expected6)
    {
        WithFreshNetUtil(ipv4, ipv6, type =>
        {
            Assert.Equal(expected4, Read(type, "IsIpV4StackPreferred"));
            Assert.Equal(expected6, Read(type, "IsIpV6AddressesPreferred"));
        });
    }

    [Theory]
    [InlineData("IsIpV4StackPreferred")]
    [InlineData("IsIpV6AddressesPreferred")]
    [InlineData("LOCALHOST4")]
    public void FirstAccessCapturesBothSettingsWithoutChangingRuntimeCapability(string firstMember)
    {
        bool nativeIPv6 = Socket.OSSupportsIPv6;
        WithFreshNetUtil("true", "false", type =>
        {
            if (firstMember == "LOCALHOST4")
                Assert.NotNull(type.GetProperty(nameof(NetUtil.LOCALHOST)).GetValue(null));
            else
                Assert.NotNull(type.GetProperty(firstMember).GetValue(null));

            Environment.SetEnvironmentVariable(IPv4Key, "false");
            Environment.SetEnvironmentVariable(IPv6Key, "true");
            Assert.True(Read(type, "IsIpV4StackPreferred"));
            Assert.False(Read(type, "IsIpV6AddressesPreferred"));
            Assert.Equal(nativeIPv6, Socket.OSSupportsIPv6);
        });
    }

    // Isolate actual static initialization; the exclusive collection protects process settings.
    private static void WithFreshNetUtil(string ipv4, string ipv6, Action<Type> assertion)
    {
        string saved4 = Environment.GetEnvironmentVariable(IPv4Key);
        string saved6 = Environment.GetEnvironmentVariable(IPv6Key);
        var context = new AssemblyLoadContext("Netty IP preference probe", isCollectible: true);

        try
        {
            Environment.SetEnvironmentVariable(IPv4Key, ipv4);
            Environment.SetEnvironmentVariable(IPv6Key, ipv6);
            Assembly assembly = context.LoadFromAssemblyPath(typeof(NetUtil).Assembly.Location);
            assertion(assembly.GetType(typeof(NetUtil).FullName, throwOnError: true));
        }
        finally
        {
            Environment.SetEnvironmentVariable(IPv4Key, saved4);
            Environment.SetEnvironmentVariable(IPv6Key, saved6);
            context.Unload();
        }
    }

    private static bool Read(Type type, string name)
    {
        return (bool)type.GetProperty(name).GetValue(null);
    }
}
