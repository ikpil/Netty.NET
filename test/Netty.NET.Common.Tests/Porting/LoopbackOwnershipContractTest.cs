using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace Netty.NET.Common.Tests.Porting;

public class LoopbackOwnershipContractTest
{
    [Fact]
    public void LoopbackAddressesAreNativeReadOnlyPropertiesWithoutFieldAliases()
    {
        Type type = typeof(NetUtil);
        foreach (string name in new[] { "LoopbackAddress" })
        {
            PropertyInfo property = type.GetProperty(name);
            Assert.NotNull(property);
            Assert.Equal(typeof(IPAddress), property.PropertyType);
            Assert.True(property.GetMethod.IsStatic);
            Assert.True(property.GetMethod.IsPublic);
            Assert.Null(property.SetMethod);
        }
        foreach (string name in new[] { "LOCALHOST4", "LOCALHOST6", "LOCALHOST" })
        {
            Assert.Null(type.GetField(name));
            Assert.Null(type.GetProperty(name));
        }
        Assert.Null(type.GetProperty("IPv4Loopback"));
        Assert.Null(type.GetProperty("IPv6Loopback"));
    }

    [Fact]
    public void CallerMutationCannotChangeLaterReads()
    {
        const string member = "LoopbackAddress";
        WithFreshNetUtil(type =>
        {
            IPAddress first = Read(type, member);
            byte[] expected = first.GetAddressBytes();
            long scope = first.AddressFamily == AddressFamily.InterNetworkV6 ? first.ScopeId : 0;
            Mutate(first, scope + 7);
            IPAddress later = Read(type, member);
            Assert.Equal(expected, later.GetAddressBytes());
            if (later.AddressFamily == AddressFamily.InterNetworkV6) Assert.Equal(scope, later.ScopeId);
            Assert.NotSame(first, later);
        });
    }

    [Fact]
    public void IndependentEndpointsDoNotShareMutableAddresses()
    {
        const string member = "LoopbackAddress";
        WithFreshNetUtil(type =>
        {
            var first = new IPEndPoint(Read(type, member), 443);
            var second = new IPEndPoint(Read(type, member), 443);
            byte[] expected = second.Address.GetAddressBytes();
            long scope = second.Address.AddressFamily == AddressFamily.InterNetworkV6 ? second.Address.ScopeId : 0;
            Mutate(first.Address, scope + 42);
            Assert.Equal(expected, second.Address.GetAddressBytes());
            if (second.Address.AddressFamily == AddressFamily.InterNetworkV6) Assert.Equal(scope, second.Address.ScopeId);
            Assert.NotSame(first.Address, second.Address);
            Assert.Equal(second.Address, Read(type, member));
        });
    }

    [Fact]
    public void ConcurrentConsumersOwnTheirScopeMutations()
    {
        WithFreshNetUtil(type =>
        {
            IPAddress expected = Read(type, "LoopbackAddress");
            byte[] bytes = expected.GetAddressBytes();
            long scope = expected.AddressFamily == AddressFamily.InterNetworkV6 ? expected.ScopeId : 0;
            Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 4 }, index =>
            {
                IPAddress address = Read(type, "LoopbackAddress");
                Mutate(address, scope + index + 1);
                IPAddress later = Read(type, "LoopbackAddress");
                Assert.Equal(bytes, later.GetAddressBytes());
                if (later.AddressFamily == AddressFamily.InterNetworkV6) Assert.Equal(scope, later.ScopeId);
                Assert.NotSame(address, later);
            });
        });
    }

    [Theory]
    [InlineData("IPv4Loopback")]
    [InlineData("IPv6Loopback")]
    public void CanonicalFrameworkValuesRejectMutation(string member)
    {
        WithFreshNetUtil(type =>
        {
            IPAddress address = Read(type, member);
            Exception error = Record.Exception(() => Mutate(address, 42));
            Assert.IsType<SocketException>(error is TargetInvocationException wrapped ? wrapped.InnerException : error);
            Assert.Equal(member == "IPv4Loopback" ? IPAddress.Loopback : IPAddress.IPv6Loopback, address);
        });
    }

    [Theory]
    [InlineData("IPv4Loopback", "127.0.0.1", AddressFamily.InterNetwork)]
    [InlineData("IPv6Loopback", "::1", AddressFamily.InterNetworkV6)]
    public void CanonicalValuesAndConsumerFormattingStayUnchanged(string member, string host, AddressFamily family)
    {
        WithFreshNetUtil(type =>
        {
            IPAddress address = Read(type, member);
            Assert.Equal(family, address.AddressFamily);
            Assert.Equal(IPAddress.Parse(host), address);
            byte[] detachedBytes = address.GetAddressBytes();
            Array.Fill(detachedBytes, (byte)0xCC);
            Assert.Equal(IPAddress.Parse(host), Read(type, member));
            var formatter = type.GetMethod("ToSocketAddressString", new[] { typeof(EndPoint) });
            Assert.Equal(family == AddressFamily.InterNetwork ? "127.0.0.1:443" : "[::1]:443",
                formatter.Invoke(null, new object[] { new IPEndPoint(address, 443) }));
        });
    }

    // Read the old API only in the regression baseline; production has no alias.
    private static IPAddress Read(Type type, string member)
    {
        PropertyInfo property = type.GetProperty(member);
        if (property != null) return (IPAddress)property.GetValue(null);
        string field = member switch { "IPv4Loopback" => "LOCALHOST4", "IPv6Loopback" => "LOCALHOST6", _ => "LOCALHOST" };
        FieldInfo oldField = type.GetField(field);
        if (oldField != null) return (IPAddress)oldField.GetValue(null);
        return member == "IPv4Loopback" ? IPAddress.Loopback : IPAddress.IPv6Loopback;
    }

    private static void Mutate(IPAddress address, long scope)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            address.ScopeId = scope;
        else
            // Exercise the still-public native IPv4 setter without an obsolete-call warning.
            typeof(IPAddress).GetProperty("Address").SetValue(address, 0L);
    }

    // Mutations in the baseline must not poison the shared test process's NetUtil.
    private static void WithFreshNetUtil(Action<Type> assertion)
    {
        var context = new AssemblyLoadContext("Netty loopback ownership probe", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(typeof(NetUtil).Assembly.Location);
            assertion(assembly.GetType(typeof(NetUtil).FullName, throwOnError: true));
        }
        finally
        {
            context.Unload();
        }
    }
}
