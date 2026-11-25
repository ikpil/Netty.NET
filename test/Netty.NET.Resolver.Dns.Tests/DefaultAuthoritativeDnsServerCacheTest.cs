/*
 * Copyright 2018 The Netty Project
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
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: InetSocketAddress is EndPoint: new InetSocketAddress(InetAddress.getByAddress(host, bytes), port) is an IPEndPoint
// of new IPAddress(bytes), which carries no host name, and createUnresolved a DnsEndPoint (docs/handler-porting.md
// Addresses). loop.schedule(Callable, 1, SECONDS).get() is loop.ScheduleAsync(Func, TimeSpan) awaited; the anonymous
// Callable is a lambda and the anonymous Comparator the nested UnresolvedLastComparator. A recorded error is rethrown
// with ExceptionDispatchInfo. shutdownGracefully() is ShutdownGracefullyAsync() without waiting, as in the source.
public class DefaultAuthoritativeDnsServerCacheTest
{

    [Fact]
    public async Task TestExpire()
    {
        IPEndPoint resolved1 = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 1 }), 53);
        IPEndPoint resolved2 = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 2 }), 53);
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultAuthoritativeDnsServerCache cache = new DefaultAuthoritativeDnsServerCache();
            cache.Cache("netty.io", resolved1, 1, loop);
            cache.Cache("netty.io", resolved2, 10000, loop);

            Exception error = await loop.ScheduleAsync(() =>
            {
                try
                {
                    Assert.Null(cache.Get("netty.io"));
                    return null;
                }
                catch (Exception cause)
                {
                    return cause;
                }
            }, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            if (error != null)
            {
                ExceptionDispatchInfo.Throw(error);
            }
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void TestExpireWithDifferentTTLs()
    {
        TestExpireWithTTL0(1);
        TestExpireWithTTL0(1000);
        TestExpireWithTTL0(1000000);
    }

    private static void TestExpireWithTTL0(int days)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultAuthoritativeDnsServerCache cache = new DefaultAuthoritativeDnsServerCache();
            cache.Cache("netty.io", new IPEndPoint(NetUtil.LOCALHOST, 53), days, loop);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void TestAddMultipleDnsServerForSameHostname()
    {
        IPEndPoint resolved1 = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 1 }), 53);
        IPEndPoint resolved2 = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 2 }), 53);
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultAuthoritativeDnsServerCache cache = new DefaultAuthoritativeDnsServerCache();
            cache.Cache("netty.io", resolved1, 100, loop);
            cache.Cache("netty.io", resolved2, 10000, loop);

            IDnsServerAddressStream entries = cache.Get("netty.io");
            Assert.Equal(2, entries.Size());
            Assert.Equal(resolved1, entries.Next());
            Assert.Equal(resolved2, entries.Next());
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    // CLR-adapted: the source caches createUnresolved("ns1", 53) and later a resolved address whose InetAddress carries
    // the host name "ns1" (InetAddress.getByAddress("ns1", 10.0.0.1)), so getHostString() matches and the resolved entry
    // replaces the unresolved one in place. IPAddress carries no host name and an IPEndPoint's host string is its address
    // text, so the unresolved entry is the DnsEndPoint of host "10.0.0.1": the in-place replacement by an equal host
    // string, the sizes and the order are checked as in the source.
    [Fact]
    public void TestUnresolvedReplacedByResolved()
    {
        DnsEndPoint unresolved = new DnsEndPoint("10.0.0.1", 53);
        IPEndPoint resolved1 = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 2 }), 53);
        IPEndPoint resolved2 = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 1 }), 53);
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultAuthoritativeDnsServerCache cache = new DefaultAuthoritativeDnsServerCache();
            cache.Cache("netty.io", unresolved, 100, loop);
            cache.Cache("netty.io", resolved1, 10000, loop);

            IDnsServerAddressStream entries = cache.Get("netty.io");
            Assert.Equal(2, entries.Size());
            Assert.Equal<EndPoint>(unresolved, entries.Next());
            Assert.Equal<EndPoint>(resolved1, entries.Next());

            cache.Cache("netty.io", resolved2, 100, loop);
            IDnsServerAddressStream entries2 = cache.Get("netty.io");

            Assert.Equal(2, entries2.Size());
            Assert.Equal<EndPoint>(resolved2, entries2.Next());
            Assert.Equal<EndPoint>(resolved1, entries2.Next());
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void TestUseNoComparator()
    {
        TestUseComparator0(true);
    }

    [Fact]
    public void TestUseComparator()
    {
        TestUseComparator0(false);
    }

    private static void TestUseComparator0(bool noComparator)
    {
        DnsEndPoint unresolved = new DnsEndPoint("ns1", 53);
        IPEndPoint resolved = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 2 }), 53);
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultAuthoritativeDnsServerCache cache;

            if (noComparator)
            {
                cache = new DefaultAuthoritativeDnsServerCache(10000, 10000, null);
            }
            else
            {
                cache = new DefaultAuthoritativeDnsServerCache(10000, 10000,
                                                               new UnresolvedLastComparator());
            }
            cache.Cache("netty.io", unresolved, 100, loop);
            cache.Cache("netty.io", resolved, 10000, loop);

            IDnsServerAddressStream entries = cache.Get("netty.io");
            Assert.Equal(2, entries.Size());

            if (noComparator)
            {
                Assert.Equal<EndPoint>(unresolved, entries.Next());
                Assert.Equal<EndPoint>(resolved, entries.Next());
            }
            else
            {
                Assert.Equal<EndPoint>(resolved, entries.Next());
                Assert.Equal<EndPoint>(unresolved, entries.Next());
            }
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private sealed class UnresolvedLastComparator : IComparer<EndPoint>
    {
        public int Compare(EndPoint o1, EndPoint o2)
        {
            if (o1.Equals(o2))
            {
                return 0;
            }
            if (o1 is DnsEndPoint)
            {
                return 1;
            }
            else
            {
                return -1;
            }
        }
    }

}
