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
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: loop.schedule(Callable, 1, SECONDS).get() is loop.ScheduleAsync(Func, TimeSpan) awaited (the anonymous Callable is
// a lambda; a recorded error is rethrown with ExceptionDispatchInfo); TimeUnit.DAYS.toSeconds(days) is days * 86400.
// shutdownGracefully() is ShutdownGracefullyAsync() without waiting, as in the source.
public class DefaultDnsCnameCacheTest
{

    [Fact]
    public async Task TestExpire()
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCnameCache cache = new DefaultDnsCnameCache();
            cache.Cache("netty.io", "mapping.netty.io", 1, loop);

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
            DefaultDnsCnameCache cache = new DefaultDnsCnameCache();
            cache.Cache("netty.io", "mapping.netty.io", days * 86400L, loop);
            Assert.Equal("mapping.netty.io", cache.Get("netty.io"));
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void TestMultipleCnamesForSameHostname()
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCnameCache cache = new DefaultDnsCnameCache();
            cache.Cache("netty.io", "mapping1.netty.io", 10, loop);
            cache.Cache("netty.io", "mapping2.netty.io", 10000, loop);

            Assert.Equal("mapping2.netty.io", cache.Get("netty.io"));
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void TestAddSameCnameForSameHostname()
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCnameCache cache = new DefaultDnsCnameCache();
            cache.Cache("netty.io", "mapping.netty.io", 10, loop);
            cache.Cache("netty.io", "mapping.netty.io", 10000, loop);

            Assert.Equal("mapping.netty.io", cache.Get("netty.io"));
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void TestClear()
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCnameCache cache = new DefaultDnsCnameCache();
            cache.Cache("x.netty.io", "mapping.netty.io", 100000, loop);
            cache.Cache("y.netty.io", "mapping.netty.io", 100000, loop);

            Assert.Equal("mapping.netty.io", cache.Get("x.netty.io"));
            Assert.Equal("mapping.netty.io", cache.Get("y.netty.io"));

            Assert.True(cache.Clear("x.netty.io"));
            Assert.Null(cache.Get("x.netty.io"));
            Assert.Equal("mapping.netty.io", cache.Get("y.netty.io"));
            cache.Clear();
            Assert.Null(cache.Get("y.netty.io"));
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
