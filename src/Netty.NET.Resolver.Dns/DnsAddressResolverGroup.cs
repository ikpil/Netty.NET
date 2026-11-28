/*
 * Copyright 2014 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Resolver.Dns;

/**
 * A {@link AddressResolverGroup} of {@link DnsNameResolver}s.
 */
// CLR: AddressResolverGroup<InetSocketAddress> is Resolver's AddressResolverGroup<IPEndPoint> (as
// DefaultAddressResolverGroup), AddressResolver/NameResolver are IAddressResolver/INameResolver,
// EventExecutor/EventLoop IEventExecutor/IEventLoop, ChannelFactory<? extends DatagramChannel>
// Func<IDatagramChannel> and Class<? extends DatagramChannel> a Type (DnsNameResolverBuilder.DatagramChannelType).
// ConcurrentHashMap of Promise is ConcurrentDictionary of TaskCompletionSource (see InflightNameResolver). The
// final newResolver(EventExecutor) is a sealed override; "throws Exception" has no counterpart. The @Deprecated
// newResolver(EventLoop, ChannelFactory, DnsServerAddressStreamProvider), whose @deprecated text names
// newNameResolver(...) as the method to override, is not ported : its body runs in NewResolver(IEventExecutor),
// and its comment is recorded in the manifest. IllegalStateException is InvalidOperationException.
public class DnsAddressResolverGroup : AddressResolverGroup<IPEndPoint>
{

    private readonly DnsNameResolverBuilder dnsResolverBuilder;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<IPAddress>> resolvesInProgress =
            new ConcurrentDictionary<string, TaskCompletionSource<IPAddress>>();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<IPAddress>>>
            resolveAllsInProgress = new ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<IPAddress>>>();

    public DnsAddressResolverGroup(DnsNameResolverBuilder dnsResolverBuilder)
    {
        this.dnsResolverBuilder = WithSharedCaches(dnsResolverBuilder.Copy());
    }

    public DnsAddressResolverGroup(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type channelType,
            IDnsServerAddressStreamProvider nameServerProvider)
    {
        this.dnsResolverBuilder = WithSharedCaches(new DnsNameResolverBuilder());
        dnsResolverBuilder.DatagramChannelType(channelType).NameServerProvider(nameServerProvider);
    }

    public DnsAddressResolverGroup(
            Func<IDatagramChannel> channelFactory,
            IDnsServerAddressStreamProvider nameServerProvider)
    {
        this.dnsResolverBuilder = WithSharedCaches(new DnsNameResolverBuilder());
        dnsResolverBuilder.DatagramChannelFactory(channelFactory).NameServerProvider(nameServerProvider);
    }

    private static DnsNameResolverBuilder WithSharedCaches(DnsNameResolverBuilder dnsResolverBuilder)
    {
        /// To avoid each member of the group having its own cache we either use the configured cache
        // or create a new one to share among the entire group.
        return dnsResolverBuilder.ResolveCache(dnsResolverBuilder.GetOrNewCache())
                .CnameCache(dnsResolverBuilder.GetOrNewCnameCache())
                .AuthoritativeDnsServerCache(dnsResolverBuilder.GetOrNewAuthoritativeDnsServerCache());
    }

    protected sealed override IAddressResolver<IPEndPoint> NewResolver(IEventExecutor executor)
    {
        if (!(executor is IEventLoop))
        {
            throw new InvalidOperationException(
                    "unsupported executor type: " + StringUtil.SimpleClassName(executor) +
                    " (expected: " + StringUtil.SimpleClassName(typeof(IEventLoop)));
        }

        // we don't really need to pass channelFactory and nameServerProvider separately,
        // but still keep this to ensure backward compatibility with (potentially) override methods
        IEventLoop loop = dnsResolverBuilder.eventLoop;
        IEventLoop eventLoop = loop == null ? (IEventLoop) executor : loop;
        Func<IDatagramChannel> channelFactory = dnsResolverBuilder.DatagramChannelFactory();
        IDnsServerAddressStreamProvider nameServerProvider = dnsResolverBuilder.NameServerProvider();

        // CLR: the body of the @Deprecated newResolver(EventLoop, ChannelFactory, DnsServerAddressStreamProvider).
        INameResolver<IPAddress> resolver = new InflightNameResolver<IPAddress>(
                eventLoop,
                NewNameResolver(eventLoop, channelFactory, nameServerProvider),
                resolvesInProgress,
                resolveAllsInProgress);

        return NewAddressResolver(eventLoop, resolver);
    }

    /**
     * Creates a new {@link NameResolver}. Override this method to create an alternative {@link NameResolver}
     * implementation or override the default configuration.
     */
    protected virtual INameResolver<IPAddress> NewNameResolver(IEventLoop eventLoop,
                                                        Func<IDatagramChannel> channelFactory,
                                                        IDnsServerAddressStreamProvider nameServerProvider)
    {
        DnsNameResolverBuilder builder = dnsResolverBuilder.Copy();

        // once again, channelFactory and nameServerProvider are most probably set in builder already,
        // but I do reassign them again to avoid corner cases with override methods
        return builder.EventLoop(eventLoop)
                .DatagramChannelFactory(channelFactory)
                .NameServerProvider(nameServerProvider)
                .Build();
    }

    /**
     * Creates a new {@link AddressResolver}. Override this method to create an alternative {@link AddressResolver}
     * implementation or override the default configuration.
     */
    protected virtual IAddressResolver<IPEndPoint> NewAddressResolver(IEventLoop eventLoop,
                                                                    INameResolver<IPAddress> resolver)
    {
        return new InetSocketAddressResolver(eventLoop, resolver);
    }
}
