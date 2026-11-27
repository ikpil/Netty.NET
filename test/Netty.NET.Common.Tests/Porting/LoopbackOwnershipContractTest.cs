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
    public void LoopbackAddressesAreReadOnlyPropertiesWithTheSourceNames()
    {
        Type type = typeof(NetUtil);
        // Owner decision 2026-11-14: the source names LOCALHOST4/LOCALHOST6/LOCALHOST are kept; each read is a copy.
        foreach (string name in new[] { "LOCALHOST4", "LOCALHOST6", "LOCALHOST" })
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
        }
        foreach (string name in new[] { "LoopbackAddress", "IPv4Loopback", "IPv6Loopback" })
        {
            Assert.Null(type.GetProperty(name));
        }
    }

    [Fact]
    public void CallerMutationCannotChangeLaterReads()
    {
        const string member = "LOCALHOST";
        WithFreshNetUtil(type =>
        {
            IPAddress first = Read(type, member);
            byte[] expected = first.GetAddressBytes();
            long scope = first.AddressFamily == AddressFamily.InterNetworkV6 ? first.ScopeId : 0;
            Mutate(first, scope + 7);
            IPAddress later = Read(type, member);
            Assert.Equal(expected, later.GetAddressBytes());
            if (later.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Assert.Equal(scope, later.ScopeId);
            }
            Assert.NotSame(first, later);
        });
    }

    [Fact]
    public void IndependentEndpointsDoNotShareMutableAddresses()
    {
        const string member = "LOCALHOST";
        WithFreshNetUtil(type =>
        {
            var first = new IPEndPoint(Read(type, member), 443);
            var second = new IPEndPoint(Read(type, member), 443);
            byte[] expected = second.Address.GetAddressBytes();
            long scope = second.Address.AddressFamily == AddressFamily.InterNetworkV6 ? second.Address.ScopeId : 0;
            Mutate(first.Address, scope + 42);
            Assert.Equal(expected, second.Address.GetAddressBytes());
            if (second.Address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Assert.Equal(scope, second.Address.ScopeId);
            }
            Assert.NotSame(first.Address, second.Address);
            Assert.Equal(second.Address, Read(type, member));
        });
    }

    [Fact]
    public void ConcurrentConsumersOwnTheirScopeMutations()
    {
        WithFreshNetUtil(type =>
        {
            IPAddress expected = Read(type, "LOCALHOST");
            byte[] bytes = expected.GetAddressBytes();
            long scope = expected.AddressFamily == AddressFamily.InterNetworkV6 ? expected.ScopeId : 0;
            Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 4 }, index =>
            {
                IPAddress address = Read(type, "LOCALHOST");
                Mutate(address, scope + index + 1);
                IPAddress later = Read(type, "LOCALHOST");
                Assert.Equal(bytes, later.GetAddressBytes());
                if (later.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    Assert.Equal(scope, later.ScopeId);
                }
                Assert.NotSame(address, later);
            });
        });
    }

    [Theory]
    [InlineData("LOCALHOST4")]
    [InlineData("LOCALHOST6")]
    public void SourceLoopbackConstantsReturnIndependentCopies(string member)
    {
        // LOCALHOST4/LOCALHOST6 hold the loopback values; a caller mutating its copy cannot change later reads.
        WithFreshNetUtil(type =>
        {
            IPAddress expected = member == "LOCALHOST4" ? IPAddress.Loopback : IPAddress.IPv6Loopback;
            IPAddress address = Read(type, member);
            Assert.Equal(expected, address);
            Mutate(address, 42);
            IPAddress later = Read(type, member);
            Assert.Equal(expected, later);
            Assert.NotSame(address, later);
        });
    }

    [Theory]
    [InlineData("LOCALHOST4", "127.0.0.1", AddressFamily.InterNetwork)]
    [InlineData("LOCALHOST6", "::1", AddressFamily.InterNetworkV6)]
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

    private static IPAddress Read(Type type, string member)
    {
        return (IPAddress)type.GetProperty(member).GetValue(null);
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
