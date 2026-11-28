/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Resolver.Dns.Tests;

#pragma warning disable xUnit1051 // The resolver calls run without the test's cancellation token, as the source's futures do.

// CLR: @BeforeEach before() runs from the constructor and @AfterEach destroy() from Dispose() (the lifecycle
// methods are private, xUnit1013). TestDnsServer.MapRecordStoreA is the ported test helper on the ApacheDs
// stand-in. resolve/resolveAll are ResolveAsync/ResolveAllAsync (core notes, Promise/Future): fut.await(10,
// SECONDS) is the CLR-only Await helper, a Task.WhenAny with a 10-second Task.Delay that is true when the resolve
// Task completed first (as handler-proxy's ProxyHandlerTest maps Future.await(timeout)); isSuccess() is
// IsCompletedSuccessfully, getNow() the result of the completed Task and cause() DnsNameResolver.Cause.
// getHostAddress() is IPAddress.ToString(): MapRecordStoreA answers IPv4 addresses, whose dotted-decimal text is
// the same on both platforms. java.net.UnknownHostException is Common's UnknownHostException, assertInstanceOf is
// Assert.IsAssignableFrom (SearchDomainUnknownHostException extends it), and AssertJ's contains/doesNotContain on
// the message are Assert.Contains/Assert.DoesNotContain.
public class SearchDomainTest : IDisposable
{

    private DnsNameResolverBuilder NewResolver()
    {
        return new DnsNameResolverBuilder(group.Next())
            .DatagramChannelType(typeof(NioDatagramChannel))
            .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer.LocalAddress()))
            .MaxQueriesPerResolve(1)
            .OptResourceEnabled(false)
            .Ndots(1);
    }

    private TestDnsServer dnsServer;
    private IEventLoopGroup group;
    private DnsNameResolver resolver;

    public SearchDomainTest()
    {
        Before();
    }

    public void Dispose()
    {
        Destroy();
    }

    private void Before()
    {
        group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
    }

    private void Destroy()
    {
        if (dnsServer != null)
        {
            dnsServer.Stop();
            dnsServer = null;
        }

        if (resolver != null)
        {
            resolver.Dispose();
        }

        _ = group.ShutdownGracefullyAsync();
    }

    [Fact]
    public async Task TestResolve()
    {
        ISet<string> domains = new HashSet<string>();
        domains.Add("host1.foo.com");
        domains.Add("host1");
        domains.Add("host3");
        domains.Add("host4.sub.foo.com");
        domains.Add("host5.sub.foo.com");
        domains.Add("host5.sub");

        TestDnsServer.MapRecordStoreA store = new TestDnsServer.MapRecordStoreA(domains);
        dnsServer = new TestDnsServer(store);
        dnsServer.Start();

        resolver = NewResolver().SearchDomains(new List<string> { "foo.com" }).Ndots(2).Build();

        string a = "host1.foo.com";
        string resolved = await AssertResolve(resolver, a);
        Assert.Equal(store.GetAddress("host1.foo.com"), resolved);

        // host1 resolves host1.foo.com with foo.com search domain
        resolved = await AssertResolve(resolver, "host1");
        Assert.Equal(store.GetAddress("host1.foo.com"), resolved);

        // "host1." absolute query
        resolved = await AssertResolve(resolver, "host1.");
        Assert.Equal(store.GetAddress("host1"), resolved);

        // "host2" not resolved
        await AssertNotResolve(resolver, "host2");

        // "host3" does not contain a dot nor it's absolute but it should still be resolved after search list have
        // been checked
        resolved = await AssertResolve(resolver, "host3");
        Assert.Equal(store.GetAddress("host3"), resolved);

        // "host3." does not contain a dot but is absolute
        resolved = await AssertResolve(resolver, "host3.");
        Assert.Equal(store.GetAddress("host3"), resolved);

        // "host4.sub" contains a dot but not resolved then resolved to "host4.sub.foo.com" with "foo.com" search domain
        resolved = await AssertResolve(resolver, "host4.sub");
        Assert.Equal(store.GetAddress("host4.sub.foo.com"), resolved);

        // "host5.sub" would have been directly resolved but since it has less than ndots the "foo.com" search domain
        // is used.
        resolved = await AssertResolve(resolver, "host5.sub");
        Assert.Equal(store.GetAddress("host5.sub.foo.com"), resolved);
    }

    [Fact]
    public async Task TestResolveAll()
    {
        ISet<string> domains = new HashSet<string>();
        domains.Add("host1.foo.com");
        domains.Add("host1");
        domains.Add("host3");
        domains.Add("host4.sub.foo.com");
        domains.Add("host5.sub.foo.com");
        domains.Add("host5.sub");

        TestDnsServer.MapRecordStoreA store = new TestDnsServer.MapRecordStoreA(domains, 2);
        dnsServer = new TestDnsServer(store);
        dnsServer.Start();

        resolver = NewResolver().SearchDomains(new List<string> { "foo.com" }).Ndots(2).Build();

        string a = "host1.foo.com";
        IList<string> resolved = await AssertResolveAll(resolver, a);
        Assert.Equal(store.GetAddresses("host1.foo.com"), resolved);

        // host1 resolves host1.foo.com with foo.com search domain
        resolved = await AssertResolveAll(resolver, "host1");
        Assert.Equal(store.GetAddresses("host1.foo.com"), resolved);

        // "host1." absolute query
        resolved = await AssertResolveAll(resolver, "host1.");
        Assert.Equal(store.GetAddresses("host1"), resolved);

        // "host2" not resolved
        await AssertNotResolveAll(resolver, "host2");

        // "host3" does not contain a dot nor it's absolute but it should still be resolved after search list have
        // been checked
        resolved = await AssertResolveAll(resolver, "host3");
        Assert.Equal(store.GetAddresses("host3"), resolved);

        // "host3." does not contain a dot but is absolute
        resolved = await AssertResolveAll(resolver, "host3.");
        Assert.Equal(store.GetAddresses("host3"), resolved);

        // "host4.sub" contains a dot but not resolved then resolved to "host4.sub.foo.com" with "foo.com" search domain
        resolved = await AssertResolveAll(resolver, "host4.sub");
        Assert.Equal(store.GetAddresses("host4.sub.foo.com"), resolved);

        // "host5.sub" would have been directly resolved but since it has less than ndots the "foo.com" search domain
        // is used.
        resolved = await AssertResolveAll(resolver, "host5.sub");
        Assert.Equal(store.GetAddresses("host5.sub.foo.com"), resolved);
    }

    [Fact]
    public async Task TestMultipleSearchDomain()
    {
        ISet<string> domains = new HashSet<string>();
        domains.Add("host1.foo.com");
        domains.Add("host2.bar.com");
        domains.Add("host3.bar.com");
        domains.Add("host3.foo.com");

        TestDnsServer.MapRecordStoreA store = new TestDnsServer.MapRecordStoreA(domains);
        dnsServer = new TestDnsServer(store);
        dnsServer.Start();

        resolver = NewResolver().SearchDomains(new[] { "foo.com", "bar.com" }).Build();

        // "host1" resolves via the "foo.com" search path
        string resolved = await AssertResolve(resolver, "host1");
        Assert.Equal(store.GetAddress("host1.foo.com"), resolved);

        // "host2" resolves via the "bar.com" search path
        resolved = await AssertResolve(resolver, "host2");
        Assert.Equal(store.GetAddress("host2.bar.com"), resolved);

        // "host3" resolves via the "foo.com" search path as it is the first one
        resolved = await AssertResolve(resolver, "host3");
        Assert.Equal(store.GetAddress("host3.foo.com"), resolved);

        // "host4" does not resolve
        await AssertNotResolve(resolver, "host4");
    }

    [Fact]
    public async Task TestSearchDomainWithNdots2()
    {
        ISet<string> domains = new HashSet<string>();
        domains.Add("host1.sub.foo.com");
        domains.Add("host2.sub.foo.com");
        domains.Add("host2.sub");

        TestDnsServer.MapRecordStoreA store = new TestDnsServer.MapRecordStoreA(domains);
        dnsServer = new TestDnsServer(store);
        dnsServer.Start();

        resolver = NewResolver().SearchDomains(ImmutableHashSet.Create("foo.com")).Ndots(2).Build();

        string resolved = await AssertResolve(resolver, "host1.sub");
        Assert.Equal(store.GetAddress("host1.sub.foo.com"), resolved);

        // "host2.sub" is resolved with the foo.com search domain as ndots = 2
        resolved = await AssertResolve(resolver, "host2.sub");
        Assert.Equal(store.GetAddress("host2.sub.foo.com"), resolved);
    }

    [Fact]
    public async Task TestSearchDomainWithNdots0()
    {
        ISet<string> domains = new HashSet<string>();
        domains.Add("host1");
        domains.Add("host1.foo.com");
        domains.Add("host2.foo.com");

        TestDnsServer.MapRecordStoreA store = new TestDnsServer.MapRecordStoreA(domains);
        dnsServer = new TestDnsServer(store);
        dnsServer.Start();

        resolver = NewResolver().SearchDomains(ImmutableHashSet.Create("foo.com")).Ndots(0).Build();

        // "host1" resolves directly as ndots = 0
        string resolved = await AssertResolve(resolver, "host1");
        Assert.Equal(store.GetAddress("host1"), resolved);

        // "host1.foo.com" resolves to host1.foo
        resolved = await AssertResolve(resolver, "host1.foo.com");
        Assert.Equal(store.GetAddress("host1.foo.com"), resolved);

        // "host2" shouldn't resolve because it is not in the known domain names, and "host2" has 0 dots which is not
        // less ndots (which is also 0).
        await AssertNotResolve(resolver, "host2");
    }

    private static async Task AssertNotResolve(DnsNameResolver resolver, string inetHost)
    {
        Task<IPAddress> fut = resolver.ResolveAsync(inetHost);
        Assert.True(await Await(fut, 10000));
        Assert.False(fut.IsCompletedSuccessfully);
    }

    private static async Task AssertNotResolveAll(DnsNameResolver resolver, string inetHost)
    {
        Task<IReadOnlyList<IPAddress>> fut = resolver.ResolveAllAsync(inetHost);
        Assert.True(await Await(fut, 10000));
        Assert.False(fut.IsCompletedSuccessfully);
    }

    private static async Task<string> AssertResolve(DnsNameResolver resolver, string inetHost)
    {
        Task<IPAddress> fut = resolver.ResolveAsync(inetHost);
        Assert.True(await Await(fut, 10000));

        return (await fut).ToString();
    }

    private static async Task<IList<string>> AssertResolveAll(DnsNameResolver resolver,
                                                              string inetHost)
    {
        Task<IReadOnlyList<IPAddress>> fut = resolver.ResolveAllAsync(inetHost);
        Assert.True(await Await(fut, 10000));
        IList<string> list = new List<string>();

        foreach (IPAddress addr in await fut)
        {
            list.Add(addr.ToString());
        }

        return list;
    }

    // CLR: Future.await(timeout, unit): true when the Task completed within the timeout (see the class header).
    private static async Task<bool> Await(Task future, int timeoutMillis)
    {
        return await Task.WhenAny(future, Task.Delay(timeoutMillis, TestContext.Current.CancellationToken)) == future;
    }

    [Fact]
    public async Task TestExceptionMsgContainsSearchDomain()
    {
        TestDnsServer.MapRecordStoreA store = new TestDnsServer.MapRecordStoreA(ImmutableHashSet<string>.Empty);
        dnsServer = new TestDnsServer(store);
        dnsServer.Start();

        resolver = NewResolver().SearchDomains(new List<string> { "foo.com" }).Ndots(1).Build();

        Task<IPAddress> fut = resolver.ResolveAsync("unknown.hostname");
        Assert.True(await Await(fut, 10000));
        Assert.False(fut.IsCompletedSuccessfully);
        Exception cause = DnsNameResolver.Cause(fut);
        Assert.IsAssignableFrom<UnknownHostException>(cause);
        Assert.Contains("foo.com", cause.Message);
    }

    [Fact]
    public async Task TestExceptionMsgDoesNotContainSearchDomainIfNdotsIsNotReached()
    {
        TestDnsServer.MapRecordStoreA store = new TestDnsServer.MapRecordStoreA(ImmutableHashSet<string>.Empty);
        dnsServer = new TestDnsServer(store);
        dnsServer.Start();

        resolver = NewResolver().SearchDomains(new List<string> { "foo.com" }).Ndots(2).Build();

        Task<IPAddress> fut = resolver.ResolveAsync("unknown.hostname");
        Assert.True(await Await(fut, 10000));
        Assert.False(fut.IsCompletedSuccessfully);
        Exception cause = DnsNameResolver.Cause(fut);
        Assert.IsAssignableFrom<UnknownHostException>(cause);
        Assert.DoesNotContain("foo.com", cause.Message);
    }
}
