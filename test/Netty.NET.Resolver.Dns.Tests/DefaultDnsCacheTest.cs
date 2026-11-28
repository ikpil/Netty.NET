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
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: InetAddress.getByAddress(bytes) is new IPAddress(bytes); loop.schedule(Callable, 1, SECONDS).get() is
// loop.ScheduleAsync(Func, TimeSpan) awaited (the anonymous Callable is a lambda; a recorded error is rethrown
// with ExceptionDispatchInfo). UnknownHostException is Common's UnknownHostException and Throwable is Exception
// (DefaultDnsCache.CopyThrowable). addSuppressed/getSuppressed are Common's
// ThrowableUtil.AddSuppressed/GetSuppressed (identity-keyed). assertInstanceOf is Assert.IsAssignableFrom;
// getStackTrace().length is the frame count of System.Diagnostics.StackTrace(exception);
// printStackTrace(PrintWriter) is Exception.ToString() written to a StreamWriter over a MemoryStream, read before
// the writer is closed, as the source reads the ByteArrayOutputStream before closing its PrintWriter (and, as in
// the source, the expected exception is printed to both writers).
public class DefaultDnsCacheTest
{

    [Fact]
    public async Task TestExpire()
    {
        IPAddress addr1 = new IPAddress(new byte[] { 10, 0, 0, 1 });
        IPAddress addr2 = new IPAddress(new byte[] { 10, 0, 0, 2 });
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCache cache = new DefaultDnsCache();
            cache.Cache("netty.io", null, addr1, 1, loop);
            cache.Cache("netty.io", null, addr2, 10000, loop);

            Exception error = await loop.ScheduleAsync(() =>
            {
                try
                {
                    Assert.Null(cache.Get("netty.io", null));
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
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCache cache = new DefaultDnsCache();
            Assert.NotNull(cache.Cache("netty.io", null, NetUtil.LOCALHOST, days, loop));
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void TestExpireWithToBigMinTTL()
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCache cache = new DefaultDnsCache(int.MaxValue, int.MaxValue, int.MaxValue);
            Assert.NotNull(cache.Cache("netty.io", null, NetUtil.LOCALHOST, 100, loop));
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void TestAddMultipleAddressesForSameHostname()
    {
        IPAddress addr1 = new IPAddress(new byte[] { 10, 0, 0, 1 });
        IPAddress addr2 = new IPAddress(new byte[] { 10, 0, 0, 2 });
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCache cache = new DefaultDnsCache();
            cache.Cache("netty.io", null, addr1, 1, loop);
            cache.Cache("netty.io", null, addr2, 10000, loop);

            IReadOnlyList<IDnsCacheEntry> entries = cache.Get("netty.io", null);
            Assert.Equal(2, entries.Count);
            AssertEntry(entries[0], addr1);
            AssertEntry(entries[1], addr2);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void TestAddSameAddressForSameHostname()
    {
        IPAddress addr1 = new IPAddress(new byte[] { 10, 0, 0, 1 });
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCache cache = new DefaultDnsCache();
            cache.Cache("netty.io", null, addr1, 1, loop);
            cache.Cache("netty.io", null, addr1, 10000, loop);

            IReadOnlyList<IDnsCacheEntry> entries = cache.Get("netty.io", null);
            Assert.Single(entries);
            AssertEntry(entries[0], addr1);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private static void AssertEntry(IDnsCacheEntry entry, IPAddress address)
    {
        Assert.Equal(address, entry.Address());
        Assert.Null(entry.Cause());
    }

    [Fact]
    public void TestCacheFailed()
    {
        IPAddress addr1 = new IPAddress(new byte[] { 10, 0, 0, 1 });
        IPAddress addr2 = new IPAddress(new byte[] { 10, 0, 0, 2 });
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCache cache = new DefaultDnsCache(1, 100, 100);
            cache.Cache("netty.io", null, addr1, 10000, loop);
            cache.Cache("netty.io", null, addr2, 10000, loop);

            IReadOnlyList<IDnsCacheEntry> entries = cache.Get("netty.io", null);
            Assert.Equal(2, entries.Count);
            AssertEntry(entries[0], addr1);
            AssertEntry(entries[1], addr2);

            Exception exception = new Exception();
            cache.Cache("netty.io", null, exception, loop);
            entries = cache.Get("netty.io", null);
            IDnsCacheEntry entry = entries[0];
            Assert.Single(entries);
            AssertThrowable(exception, entry.Cause());
            Assert.Null(entry.Address());
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void TestDotHandling()
    {
        IPAddress addr1 = new IPAddress(new byte[] { 10, 0, 0, 1 });
        IPAddress addr2 = new IPAddress(new byte[] { 10, 0, 0, 2 });
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCache cache = new DefaultDnsCache(1, 100, 100);
            cache.Cache("netty.io", null, addr1, 10000, loop);
            cache.Cache("netty.io.", null, addr2, 10000, loop);

            IReadOnlyList<IDnsCacheEntry> entries = cache.Get("netty.io", null);
            Assert.Equal(2, entries.Count);
            AssertEntry(entries[0], addr1);
            AssertEntry(entries[1], addr2);

            IReadOnlyList<IDnsCacheEntry> entries2 = cache.Get("netty.io.", null);
            Assert.Equal(2, entries2.Count);
            AssertEntry(entries2[0], addr1);
            AssertEntry(entries2[1], addr2);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void TestCacheExceptionIsSafe()
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            IEventLoop loop = group.Next();
            DefaultDnsCache cache = new DefaultDnsCache(1, 100, 100);

            TestSuppressed(cache, new UnknownHostException("test"), loop);
            TestSuppressed(cache, new Exception("test"), loop);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private static void TestSuppressed(IDnsCache cache, Exception exception, IEventLoop loop)
    {
        string hostname = Guid.NewGuid().ToString();
        cache.Cache(hostname, null, exception, loop);
        IReadOnlyList<IDnsCacheEntry> entries = cache.Get(hostname, null);
        IDnsCacheEntry entry = entries[0];
        Assert.Single(entries);
        Assert.NotSame(exception, entry.Cause());

        AssertThrowable(exception, entry.Cause());
        ThrowableUtil.AddSuppressed(entry.Cause(), new Exception());

        Assert.Empty(ThrowableUtil.GetSuppressed(exception));
        Assert.Single(ThrowableUtil.GetSuppressed(entry.Cause()));
        Assert.Null(entry.Address());
    }

    private static void AssertThrowable(Exception expected, Exception actual)
    {
        Assert.IsAssignableFrom(expected.GetType(), actual);
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(new StackTrace(expected).FrameCount, new StackTrace(actual).FrameCount);
        MemoryStream expectedOutputStream = new MemoryStream();
        StreamWriter expectedWriter = new StreamWriter(expectedOutputStream);
        MemoryStream actualOutputStream = new MemoryStream();
        StreamWriter actualWriter = new StreamWriter(actualOutputStream);

        try
        {
            expectedWriter.WriteLine(expected.ToString());

            actualWriter.WriteLine(expected.ToString());

            Assert.Equal(expectedOutputStream.ToArray(), actualOutputStream.ToArray());
        }
        finally
        {
            expectedWriter.Dispose();
            actualWriter.Dispose();
        }
    }
}
