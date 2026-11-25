/*
 * Copyright 2026 The Netty Project
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
using System.Collections.Immutable;
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Resolver.Dns.Tests.ApacheDs;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using static Netty.NET.Resolver.Dns.Tests.DnsNameResolverTest;
using static Netty.NET.Resolver.Dns.Tests.TestDnsServer;

namespace Netty.NET.Resolver.Dns.Tests;

#pragma warning disable xUnit1051 // The resolver calls run without the test's cancellation token, as the source's futures do.

// CLR: @Isolated runs the class alone, which matters because it sets the process-wide
// DnsNameResolver.resolveLocalhostWithoutDns; a collection with DisableParallelization is "guaranteed not to run in
// parallel against any other test" (https://xunit.net/docs/running-tests-in-parallel), as handler's LoggingHandlerTest
// maps @Isolated.
[CollectionDefinition(nameof(IsolatedDnsNameResolverTestCollection), DisableParallelization = true)]
public sealed class IsolatedDnsNameResolverTestCollection;

// CLR: the static fields are initialized in their textual order; @BeforeAll captureSetting() and @AfterAll
// restoreSetting() run from the class fixture BeforeAndAfterAll (https://xunit.net/docs/shared-context, "Class
// Fixtures"), as in DnsNameResolverTest; shutdownGracefully() is ShutdownGracefullyAsync() without waiting.
// @ParameterizedTest @EnumSource(DnsNameResolverChannelStrategy.class) is [Theory] over DnsNameResolverTest's
// ChannelStrategies (every value of the C# enum). The static imports of DnsNameResolverTest.DOMAINS_ALL/newResolver and
// TestDnsServer.newARecord are using static directives (the members are internal, the package access of the source).
// The anonymous RecordStore and HostsFileEntriesResolver classes are the nested NameRecordStore and
// NullHostsFileEntriesResolver; RecordStore and the ApacheDs types are the stand-in (owner decision 2026-11-25), and
// Collections.singleton/emptySet are ImmutableHashSet.Create/ImmutableHashSet.Empty. resolve(h).syncUninterruptibly()
// .getNow() is an awaited ResolveAsync and close() Dispose() (core notes); getHostAddress() of the IPv4 answer is
// IPAddress.ToString(); dnsQueryLifecycleObserverFactory() is the internal DnsQueryLifecycleObserverFactory(); AssertJ's
// assumeThat(PlatformDependent.isWindows()).isFalse() is Assert.SkipWhen (xUnit v3 dynamic skip).
[Collection(nameof(IsolatedDnsNameResolverTestCollection))]
public class IsolatedDnsNameResolverTest : IClassFixture<IsolatedDnsNameResolverTest.BeforeAndAfterAll>
{
    private static readonly TestDnsServer dnsServer = new TestDnsServer(DOMAINS_ALL);
    private static readonly IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
    private static bool existingLocalhostSetting;

    private static void CaptureSetting()
    {
        existingLocalhostSetting = DnsNameResolver.resolveLocalhostWithoutDns;
        dnsServer.Start();
    }

    private static void RestoreSetting()
    {
        try
        {
            dnsServer.Stop();
            _ = group.ShutdownGracefullyAsync();
        }
        finally
        {
            DnsNameResolver.resolveLocalhostWithoutDns = existingLocalhostSetting;
        }
    }

    // CLR: @BeforeAll captureSetting() and @AfterAll restoreSetting() (see the class header).
    public sealed class BeforeAndAfterAll : IDisposable
    {
        public BeforeAndAfterAll()
        {
            CaptureSetting();
        }

        public void Dispose()
        {
            RestoreSetting();
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies), MemberType = typeof(DnsNameResolverTest))]
    public async Task TestResolveDotLocalhostViaDnsWhenDisabled(DnsNameResolverChannelStrategy strategy)
    {
        string hostname = "myservice.localhost";
        string expectedIp = "10.0.0.1";
        TestDnsServer customDnsServer = new TestDnsServer(new NameRecordStore(hostname, expectedIp));
        customDnsServer.Start();
        DnsNameResolver.resolveLocalhostWithoutDns = false;
        try
        {
            DnsNameResolver resolver = NewResolver(strategy, false, null, customDnsServer, group.Next())
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                    .HostsFileEntriesResolver(new NullHostsFileEntriesResolver())
                    .Build();
            try
            {
                IPAddress address = await resolver.ResolveAsync(hostname);
                Assert.Equal(expectedIp, address.ToString());

                TestRecursiveCacheDnsQueryLifecycleObserverFactory lifecycleObserverFactory =
                        (TestRecursiveCacheDnsQueryLifecycleObserverFactory)
                                resolver.DnsQueryLifecycleObserverFactory();
                Assert.False(lifecycleObserverFactory.observers.IsEmpty);
            }
            finally
            {
                resolver.Dispose();
            }
        }
        finally
        {
            customDnsServer.Stop();
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies), MemberType = typeof(DnsNameResolverTest))]
    public async Task TestResolveLocalhostViaDnsWhenDisabledOnNonWindows(DnsNameResolverChannelStrategy strategy)
    {
        Assert.SkipWhen(PlatformDependent.IsWindows(), "assumeThat(PlatformDependent.isWindows()).isFalse()");

        string expectedIp = "10.0.0.2";
        TestDnsServer customDnsServer = new TestDnsServer(new NameRecordStore("localhost", expectedIp));
        customDnsServer.Start();
        DnsNameResolver.resolveLocalhostWithoutDns = false;
        try
        {
            DnsNameResolver resolver = NewResolver(strategy, false, null, customDnsServer, group.Next())
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                    .HostsFileEntriesResolver(new NullHostsFileEntriesResolver())
                    .Build();
            try
            {
                IPAddress address = await resolver.ResolveAsync("localhost");
                Assert.Equal(expectedIp, address.ToString());

                TestRecursiveCacheDnsQueryLifecycleObserverFactory lifecycleObserverFactory =
                        (TestRecursiveCacheDnsQueryLifecycleObserverFactory)
                                resolver.DnsQueryLifecycleObserverFactory();
                Assert.False(lifecycleObserverFactory.observers.IsEmpty);
            }
            finally
            {
                resolver.Dispose();
            }
        }
        finally
        {
            customDnsServer.Stop();
        }
    }

    // CLR: the anonymous RecordStore of both tests: an A record for the name, with or without the trailing dot
    // (equalsIgnoreCase is OrdinalIgnoreCase), else an empty set.
    private sealed class NameRecordStore : IRecordStore
    {
        private readonly string hostname;
        private readonly string expectedIp;

        internal NameRecordStore(string hostname, string expectedIp)
        {
            this.hostname = hostname;
            this.expectedIp = expectedIp;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            if (question.GetRecordType() == RecordType.A)
            {
                string domainName = question.GetDomainName();
                if (domainName.Equals(hostname, StringComparison.OrdinalIgnoreCase) ||
                    domainName.Equals(hostname + '.', StringComparison.OrdinalIgnoreCase))
                {
                    return ImmutableHashSet.Create(NewARecord(hostname, expectedIp));
                }
            }
            return ImmutableHashSet<IResourceRecord>.Empty;
        }
    }

    // CLR: the anonymous HostsFileEntriesResolver of both tests.
    private sealed class NullHostsFileEntriesResolver : IHostsFileEntriesResolver
    {
        public IPAddress Address(string inetHost, ResolvedAddressTypes resolvedAddressTypes)
        {
            return null;
        }
    }
}
