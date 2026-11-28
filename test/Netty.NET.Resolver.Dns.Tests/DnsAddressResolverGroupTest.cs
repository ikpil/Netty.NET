/*
 * Copyright 2020 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Resolver.Dns.Tests.ApacheDs;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Resolver.Dns.Tests.TestDnsServer;

namespace Netty.NET.Resolver.Dns.Tests;

#pragma warning disable xUnit1051 // The resolver calls run without the test's cancellation token, as the source's futures do.

// CLR: the apacheds imports are the ApacheDs stand-in (owner decision 2026-11-25, see TestDnsServer). AddressResolver is
// IAddressResolver<IPEndPoint> and resolve(SocketAddress) is ResolveAsync(EndPoint); InetSocketAddress.createUnresolved
// is DnsEndPoint and getAddress() IPEndPoint.Address (docs/handler-porting.md, Addresses). loop.newPromise() is a
// TaskCompletionSource created with RunContinuationsAsynchronously, setSuccess/setFailure SetResult/SetException and
// promise.sync()/get() an awaited Task (core notes, Promise/Future). The FutureListener is a continuation on Common's
// EventExecutorTaskScheduler over the configured loop: a Task has no executor of its own (SimpleNameResolver: "Tasks do
// not impose executor affinity on their consumers"), so the continuation is put on the event loop that notifies the
// listeners of the resolver's future in Java (DefaultPromise.notifyListeners on the resolver's executor, which
// DnsAddressResolverGroup.newResolver picks as the configured loop); future.cause() is DnsNameResolver.Cause and
// future.get() the Task's result. The anonymous SocketAddress subclass is the nested TestSocketAddress (an EndPoint the
// resolver does not support; serialVersionUID has no counterpart), and the anonymous RecordStore is the nested
// HostnameRecordStore, which captures the source's final locals. Collections.singleton is ImmutableHashSet.Create,
// equalsIgnoreCase is OrdinalIgnoreCase, assertInstanceOf is Assert.IsAssignableFrom and assertSame Assert.Same.
// close() is Dispose() and shutdownGracefully() is ShutdownGracefullyAsync() without waiting, as in the source.
public class DnsAddressResolverGroupTest
{
    [Fact]
    public async Task TestUseConfiguredEventLoop()
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        IEventLoop loop = group.Next();

        IEventLoopGroup defaultEventLoopGroup = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
        DnsNameResolverBuilder builder = new DnsNameResolverBuilder()
                .EventLoop(loop).DatagramChannelType(typeof(NioDatagramChannel));
        DnsAddressResolverGroup resolverGroup = new DnsAddressResolverGroup(builder);

        try
        {
            TaskCompletionSource<object> promise =
                    new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            IAddressResolver<IPEndPoint> resolver = resolverGroup.GetResolver(defaultEventLoopGroup.Next());
            Task<IPEndPoint> future = resolver.ResolveAsync(new TestSocketAddress());
            _ = future.ContinueWith(f =>
            {
                try
                {
                    Assert.IsAssignableFrom<UnsupportedAddressTypeException>(DnsNameResolver.Cause(f));
                    Assert.True(loop.InEventLoop());
                    promise.SetResult(null);
                }
                catch (Exception cause)
                {
                    promise.SetException(cause);
                }
            }, CancellationToken.None, TaskContinuationOptions.None, new EventExecutorTaskScheduler(() => loop));
            await AwaitUninterruptibly(future);
            await promise.Task;
        }
        finally
        {
            resolverGroup.Dispose();
            _ = group.ShutdownGracefullyAsync();
            _ = defaultEventLoopGroup.ShutdownGracefullyAsync();
        }
    }

    private sealed class TestSocketAddress : EndPoint
    {
    }

    [Fact]
    public async Task TestSharedDNSCacheAcrossEventLoops()
    {
        string hostname = "netty.io";
        string expectedIp = "1.2.3.4";
        TestDnsServer dnsServer = new TestDnsServer(new HostnameRecordStore(hostname, expectedIp));
        dnsServer.Start();
        MultiThreadIoEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        IEventLoop loop = group.Next();
        DnsNameResolverBuilder builder = new DnsNameResolverBuilder()
                .EventLoop(loop).DatagramChannelType(typeof(NioDatagramChannel))
                .OptResourceEnabled(false)
                .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer.LocalAddress()));
        DnsAddressResolverGroup resolverGroup = new DnsAddressResolverGroup(builder);
        IEventLoopGroup defaultEventLoopGroup = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
        IEventLoop eventLoop1 = defaultEventLoopGroup.Next();
        IEventLoop eventLoop2 = defaultEventLoopGroup.Next();

        try
        {
            TaskCompletionSource<IPEndPoint> promise1 =
                    new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            InetSocketAddressResolver resolver1 = (InetSocketAddressResolver)resolverGroup.GetResolver(eventLoop1);
            IPAddress address1 =
                    await Resolve(resolver1, new DnsEndPoint(hostname, 80), promise1);
            TaskCompletionSource<IPEndPoint> promise2 =
                    new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            InetSocketAddressResolver resolver2 = (InetSocketAddressResolver)resolverGroup.GetResolver(eventLoop2);
            IPAddress address2 =
                    await Resolve(resolver2, new DnsEndPoint(hostname, 80), promise2);
            Assert.Same(address1, address2);
        }
        finally
        {
            resolverGroup.Dispose();
            _ = group.ShutdownGracefullyAsync();
            _ = defaultEventLoopGroup.ShutdownGracefullyAsync();
            dnsServer.Stop();
        }
    }

    private sealed class HostnameRecordStore : IRecordStore
    {
        private readonly string hostname;
        private readonly string expectedIp;

        internal HostnameRecordStore(string hostname, string expectedIp)
        {
            this.hostname = hostname;
            this.expectedIp = expectedIp;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            if (question.GetRecordType() == RecordType.A &&
                question.GetDomainName().Equals(hostname, StringComparison.OrdinalIgnoreCase))
            {
                return ImmutableHashSet.Create(NewARecord(hostname, expectedIp));
            }

            return null;
        }
    }

    private async Task<IPAddress> Resolve(InetSocketAddressResolver resolver, EndPoint socketAddress,
                                          TaskCompletionSource<IPEndPoint> promise)
    {
        Task<IPEndPoint> future = resolver.ResolveAsync(socketAddress);
        _ = future.ContinueWith(f =>
        {
            try
            {
                promise.SetResult(f.GetAwaiter().GetResult());
            }
            catch (Exception cause)
            {
                promise.SetException(cause);
            }
        }, TaskScheduler.Default);
        await AwaitUninterruptibly(future);
        await promise.Task;
        IPEndPoint inetSocketAddress = await promise.Task;
        return inetSocketAddress.Address;
    }

    // CLR: Future.await(): waits for the Task without observing its result or failure.
    private static async Task AwaitUninterruptibly(Task future)
    {
        try
        {
            await future;
        }
        catch (Exception)
        {
            // The outcome is read by the listener.
        }
    }
}
