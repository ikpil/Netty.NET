/*
 * Copyright 2023 The Netty Project
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
using System.Net;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: the static GROUP is created by the static field initializer and the @AfterAll shutdownEventLoopGroup() runs
// from the class fixture AfterAll, which xUnit disposes after the last test of the class
// (https://xunit.net/docs/shared-context, "Class Fixtures"; as handler's FlowControlHandlerTest);
// shutdownGracefully() is ShutdownGracefullyAsync() without waiting, as in the source. @BeforeEach setUp() is the
// constructor and @AfterEach tearDown() is Dispose(), which close() maps to. datagramChannelType(NioDatagramChannel.class)
// is DatagramChannelType(typeof(NioDatagramChannel)). The static import of Cache.MAX_SUPPORTED_TTL_SECS is the const
// Cache<object>.MAX_SUPPORTED_TTL_SECS (the generic class has one value for every type argument). AssertJ's
// containsExactly is Assert.Equal over the sequences, isSameAs is Assert.Same, isInstanceOf Assert.IsAssignableFrom and
// isEqualTo Assert.Equal. The test classes are public, as xUnit requires; the nested test doubles implement the I-prefixed
// interfaces (InetAddress is IPAddress, InetSocketAddress EndPoint, Throwable Exception, EventLoop IEventLoop, DnsRecord
// IDnsRecord) and UnsupportedOperationException is NotSupportedException.
public class DnsNameResolverBuilderTest : IClassFixture<DnsNameResolverBuilderTest.AfterAll>, IDisposable
{
    private static readonly IEventLoopGroup GROUP = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

    private DnsNameResolverBuilder builder;
    private DnsNameResolver resolver;

    public DnsNameResolverBuilderTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        builder = new DnsNameResolverBuilder(GROUP.Next()).DatagramChannelType(typeof(NioDatagramChannel));
    }

    public void Dispose()
    {
        TearDown();
    }

    private void TearDown()
    {
        if (resolver != null)
        {
            resolver.Dispose();
        }
    }

    private static void ShutdownEventLoopGroup()
    {
        _ = GROUP.ShutdownGracefullyAsync();
    }

    public sealed class AfterAll : IDisposable
    {
        public void Dispose()
        {
            ShutdownEventLoopGroup();
        }
    }

    [Fact]
    public void SearchDomainsPreservesOrderCaseAndHashCollisions()
    {
        resolver = builder.SearchDomains(new List<string> { "Aa", "BB", "Aa", "aa", null, "ignored" }).Build();

        Assert.Equal(new[] { "Aa", "BB", "aa" }, resolver.SearchDomains());
    }

    [Fact]
    public void TestDefaults()
    {
        resolver = builder.Build();

        CheckDefaultDnsCache((DefaultDnsCache) resolver.ResolveCache(), Cache<object>.MAX_SUPPORTED_TTL_SECS, 0, 0);

        CheckDefaultDnsCnameCache((DefaultDnsCnameCache) resolver.CnameCache(), Cache<object>.MAX_SUPPORTED_TTL_SECS, 0);

        CheckDefaultAuthoritativeDnsServerCache(
                (DefaultAuthoritativeDnsServerCache) resolver.AuthoritativeDnsServerCache(),
                Cache<object>.MAX_SUPPORTED_TTL_SECS, 0);
Assert.IsAssignableFrom<ThreadLocalNameServerAddressStream>(resolver.QueryDnsServerAddressStream());
    }

    [Fact]
    public void TestCustomDnsCacheDefaultTtl()
    {
        IDnsCache testDnsCache = new TestDnsCache();
        resolver = builder.ResolveCache(testDnsCache).Build();

        Assert.Same(testDnsCache, resolver.ResolveCache());

        CheckDefaultDnsCnameCache((DefaultDnsCnameCache) resolver.CnameCache(), Cache<object>.MAX_SUPPORTED_TTL_SECS, 0);

        CheckDefaultAuthoritativeDnsServerCache(
                (DefaultAuthoritativeDnsServerCache) resolver.AuthoritativeDnsServerCache(),
                Cache<object>.MAX_SUPPORTED_TTL_SECS, 0);
    }

    [Fact]
    public void TestCustomDnsCacheCustomTtl()
    {
        IDnsCache testDnsCache = new TestDnsCache();
        resolver = builder.ResolveCache(testDnsCache).Ttl(1, 2).NegativeTtl(3).Build();

        Assert.Same(testDnsCache, resolver.ResolveCache());

        CheckDefaultDnsCnameCache((DefaultDnsCnameCache) resolver.CnameCache(), 2, 1);

        CheckDefaultAuthoritativeDnsServerCache(
                (DefaultAuthoritativeDnsServerCache) resolver.AuthoritativeDnsServerCache(), 2, 1);
    }

    [Fact]
    public void TestCustomDnsCnameCacheDefaultTtl()
    {
        IDnsCnameCache testDnsCnameCache = new TestDnsCnameCache();
        resolver = builder.CnameCache(testDnsCnameCache).Build();

        CheckDefaultDnsCache((DefaultDnsCache) resolver.ResolveCache(), Cache<object>.MAX_SUPPORTED_TTL_SECS, 0, 0);

        Assert.Same(testDnsCnameCache, resolver.CnameCache());

        CheckDefaultAuthoritativeDnsServerCache(
                (DefaultAuthoritativeDnsServerCache) resolver.AuthoritativeDnsServerCache(),
                Cache<object>.MAX_SUPPORTED_TTL_SECS, 0);
    }

    [Fact]
    public void TestCustomDnsCnameCacheCustomTtl()
    {
        IDnsCnameCache testDnsCnameCache = new TestDnsCnameCache();
        resolver = builder.CnameCache(testDnsCnameCache).Ttl(1, 2).NegativeTtl(3).Build();

        CheckDefaultDnsCache((DefaultDnsCache) resolver.ResolveCache(), 2, 1, 3);

        Assert.Same(testDnsCnameCache, resolver.CnameCache());

        CheckDefaultAuthoritativeDnsServerCache(
                (DefaultAuthoritativeDnsServerCache) resolver.AuthoritativeDnsServerCache(), 2, 1);
    }

    [Fact]
    public void TestCustomAuthoritativeDnsServerCacheDefaultTtl()
    {
        IAuthoritativeDnsServerCache testAuthoritativeDnsServerCache = new TestAuthoritativeDnsServerCache();
        resolver = builder.AuthoritativeDnsServerCache(testAuthoritativeDnsServerCache).Build();

        CheckDefaultDnsCache((DefaultDnsCache) resolver.ResolveCache(), Cache<object>.MAX_SUPPORTED_TTL_SECS, 0, 0);

        CheckDefaultDnsCnameCache((DefaultDnsCnameCache) resolver.CnameCache(), Cache<object>.MAX_SUPPORTED_TTL_SECS, 0);

        Assert.Same(testAuthoritativeDnsServerCache, resolver.AuthoritativeDnsServerCache());
    }

    [Fact]
    public void TestCustomAuthoritativeDnsServerCacheCustomTtl()
    {
        IAuthoritativeDnsServerCache testAuthoritativeDnsServerCache = new TestAuthoritativeDnsServerCache();
        resolver = builder.AuthoritativeDnsServerCache(testAuthoritativeDnsServerCache)
                .Ttl(1, 2).NegativeTtl(3).Build();

        CheckDefaultDnsCache((DefaultDnsCache) resolver.ResolveCache(), 2, 1, 3);

        CheckDefaultDnsCnameCache((DefaultDnsCnameCache) resolver.CnameCache(), 2, 1);

        Assert.Same(testAuthoritativeDnsServerCache, resolver.AuthoritativeDnsServerCache());
    }

    [Fact]
    public void DisableQueryTimeoutWithZero()
    {
        resolver = builder.QueryTimeoutMillis(0).Build();
        Assert.Equal(0L, resolver.QueryTimeoutMillis());
    }

    private static void CheckDefaultDnsCache(DefaultDnsCache dnsCache,
            int expectedMaxTtl, int expectedMinTtl, int expectedNegativeTtl)
    {
        Assert.Equal(expectedMaxTtl, dnsCache.MaxTtl());
        Assert.Equal(expectedMinTtl, dnsCache.MinTtl());
        Assert.Equal(expectedNegativeTtl, dnsCache.NegativeTtl());
    }

    private static void CheckDefaultDnsCnameCache(DefaultDnsCnameCache dnsCnameCache,
            int expectedMaxTtl, int expectedMinTtl)
    {
        Assert.Equal(expectedMaxTtl, dnsCnameCache.MaxTtl());
        Assert.Equal(expectedMinTtl, dnsCnameCache.MinTtl());
    }

    private static void CheckDefaultAuthoritativeDnsServerCache(
            DefaultAuthoritativeDnsServerCache authoritativeDnsServerCache,
            int expectedMaxTtl, int expectedMinTtl)
    {
        Assert.Equal(expectedMaxTtl, authoritativeDnsServerCache.MaxTtl());
        Assert.Equal(expectedMinTtl, authoritativeDnsServerCache.MinTtl());
    }

    private sealed class TestDnsCache : IDnsCache
    {

        public void Clear()
        {
            //no-op
        }

        public bool Clear(string hostname)
        {
            return false;
        }

        public IReadOnlyList<IDnsCacheEntry> Get(string hostname, IDnsRecord[] additional)
        {
            return null;
        }

        public IDnsCacheEntry Cache(string hostname, IDnsRecord[] additional, IPAddress address,
                long originalTtl, IEventLoop loop)
        {
            return null;
        }

        public IDnsCacheEntry Cache(string hostname, IDnsRecord[] additional, Exception cause, IEventLoop loop)
        {
            return null;
        }
    }

    private sealed class TestDnsCnameCache : IDnsCnameCache
    {

        public string Get(string hostname)
        {
            return null;
        }

        public void Cache(string hostname, string cname, long originalTtl, IEventLoop loop)
        {
            //no-op
        }

        public void Clear()
        {
            //no-op
        }

        public bool Clear(string hostname)
        {
            return false;
        }
    }

    private sealed class TestAuthoritativeDnsServerCache : IAuthoritativeDnsServerCache
    {

        public IDnsServerAddressStream Get(string hostname)
        {
            return null;
        }

        public void Cache(string hostname, EndPoint address, long originalTtl, IEventLoop loop)
        {
            //no-op
        }

        public void Clear()
        {
            //no-op
        }

        public bool Clear(string hostname)
        {
            return false;
        }
    }

    [Fact]
    public void TestCustomQueryDnsServerAddressStream()
    {
        IDnsServerAddressStream queryAddressStream = new TestQueryServerAddressStream();
        resolver = builder.QueryServerAddressStream(queryAddressStream).Build();

        Assert.Same(queryAddressStream, resolver.QueryDnsServerAddressStream());

        resolver = builder.Copy().Build();
        Assert.Same(queryAddressStream, resolver.QueryDnsServerAddressStream());
    }

    private sealed class TestQueryServerAddressStream : IDnsServerAddressStream
    {

        public EndPoint Next()
        {
            throw new NotSupportedException();
        }

        public int Size()
        {
            throw new NotSupportedException();
        }

        public IDnsServerAddressStream Duplicate()
        {
            throw new NotSupportedException();
        }
    }
}
