using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Tests.Porting;

[Collection("System properties")]
public class NetworkDefaultsContractTest
{
    [Fact]
    public void DefaultsAreGetOnlyPropertiesWithTheSourceNames()
    {
        foreach ((string name, Type type) in new[]
        {
            ("LOOPBACK_IF", typeof(NetworkInterface)),
            ("NETWORK_INTERFACES", typeof(IReadOnlyList<NetworkInterface>)),
            ("SOMAXCONN", typeof(int))
        })
        {
            PropertyInfo property = typeof(NetUtil).GetProperty(name);
            Assert.NotNull(property);
            Assert.Equal(type, property.PropertyType);
            Assert.True(property.GetMethod.IsPublic);
            Assert.True(property.GetMethod.IsStatic);
            Assert.Null(property.SetMethod);
        }

        foreach (string name in new[] { "LOOPBACK_IF", "NETWORK_INTERFACES", "SOMAXCONN" })
        {
            Assert.Null(typeof(NetUtil).GetField(name));
        }

        foreach (string name in new[] { "LoopbackInterface", "NetworkInterfaces", "DefaultListenBacklog" })
        {
            Assert.Null(typeof(NetUtil).GetProperty(name));
        }
    }

    [Fact]
    public void PublishedInterfacesKeepOneReadOnlySnapshot()
    {
        WithFreshNetUtil(type =>
        {
            var interfaces = (IReadOnlyList<NetworkInterface>)Read(type, "NETWORK_INTERFACES");
            var mutable = Assert.IsAssignableFrom<IList<NetworkInterface>>(interfaces);
            Assert.True(mutable.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => mutable.Clear());
            Assert.Same(interfaces, Read(type, "NETWORK_INTERFACES"));

            for (int i = 0; i < interfaces.Count; i++)
                Assert.Same(interfaces[i], ((IReadOnlyList<NetworkInterface>)Read(type, "NETWORK_INTERFACES"))[i]);
        });
    }

    [Fact]
    public void ConcurrentReadersObserveCachedDefaults()
    {
        WithFreshNetUtil(type =>
        {
            object interfaces = Read(type, "NETWORK_INTERFACES");
            object loopback = Read(type, "LOOPBACK_IF");
            int backlog = (int)Read(type, "SOMAXCONN");
            Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 4 }, _ =>
            {
                Assert.Same(interfaces, Read(type, "NETWORK_INTERFACES"));
                Assert.Same(loopback, Read(type, "LOOPBACK_IF"));
                Assert.Equal(backlog, (int)Read(type, "SOMAXCONN"));
            });
        });
    }

    [Theory]
    [InlineData("LOOPBACK_IF")]
    [InlineData("NETWORK_INTERFACES")]
    [InlineData("SOMAXCONN")]
    public void FirstDefaultAccessCapturesBothPreferences(string member)
    {
        const string ipv4Key = "java.net.preferIPv4Stack";
        const string ipv6Key = "java.net.preferIPv6Addresses";
        string saved4 = Environment.GetEnvironmentVariable(ipv4Key);
        string saved6 = Environment.GetEnvironmentVariable(ipv6Key);

        try
        {
            Environment.SetEnvironmentVariable(ipv4Key, "true");
            Environment.SetEnvironmentVariable(ipv6Key, "false");
            WithFreshNetUtil(type =>
            {
                Read(type, member);
                Environment.SetEnvironmentVariable(ipv4Key, "false");
                Environment.SetEnvironmentVariable(ipv6Key, "true");
                Assert.True((bool)Read(type, "IsIpV4StackPreferred"));
                Assert.False((bool)Read(type, "IsIpV6AddressesPreferred"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(ipv4Key, saved4);
            Environment.SetEnvironmentVariable(ipv6Key, saved6);
        }
    }

    [Fact]
    public void ReentrantLoggerFactoryReadsDefaultsWithoutPoisoningInitialization()
    {
        var context = new AssemblyLoadContext("Netty network default callback probe", isCollectible: true);

        try
        {
            // Both assemblies must use this context's still-uninitialized NetUtil and logger factory.
            context.LoadFromAssemblyPath(typeof(NetUtil).Assembly.Location);
            Assembly tests = context.LoadFromAssemblyPath(typeof(NetworkDefaultsContractTest).Assembly.Location);
            MethodInfo probe = tests.GetType(typeof(NetworkDefaultsContractTest).FullName, throwOnError: true)
                .GetMethod(nameof(ObserveReentrantInitialization));
            string[] observations = (string[])probe.Invoke(null, null);
            Assert.Equal(new[] { "callback|null|null|null|0", "initialized|True|True|True", "later|True" }, observations);
        }
        finally
        {
            context.Unload();
        }
    }

    // Invoked inside the isolated context; only BCL values cross its boundary.
    public static string[] ObserveReentrantInitialization()
    {
        var observations = new List<string>();
        IInternalLoggerFactory original = InternalLoggerFactory.GetDefaultFactory();

        try
        {
            InternalLoggerFactory.SetDefaultFactory(new ReadingFactory(original, () =>
            {
                Type type = typeof(NetUtil);
                observations.Add($"callback|{Read(type, "LOCALHOST") ?? "null"}|{Read(type, "LOOPBACK_IF") ?? "null"}|{Read(type, "NETWORK_INTERFACES") ?? "null"}|{Read(type, "SOMAXCONN")}");
            }));
            IPAddress address = NetUtil.LOCALHOST;
            observations.Add($"initialized|{address != null}|{Read(typeof(NetUtil), "NETWORK_INTERFACES") != null}|{observations.Count == 1}");
            observations.Add($"later|{address.Equals(NetUtil.LOCALHOST)}");

            return observations.ToArray();
        }
        finally
        {
            InternalLoggerFactory.SetDefaultFactory(original);
        }
    }

    private sealed class ReadingFactory(IInternalLoggerFactory original, Action readDefaults) : IInternalLoggerFactory
    {
        public IInternalLogger NewInstance(string name)
        {
            if (name == typeof(NetUtil).FullName)
            {
                readDefaults();
            }

            return original.NewInstance(name);
        }
    }

    private static object Read(Type type, string name)
    {
        return type.GetProperty(name).GetValue(null);
    }

    private static void WithFreshNetUtil(Action<Type> assertion)
    {
        var context = new AssemblyLoadContext("Netty network default probe", isCollectible: true);

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
