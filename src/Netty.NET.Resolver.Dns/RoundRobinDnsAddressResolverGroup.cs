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
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Resolver.Dns;

/**
 * A {@link AddressResolverGroup} of {@link DnsNameResolver}s that supports random selection of destination addresses if
 * multiple are provided by the nameserver. This is ideal for use in applications that use a pool of connections, for
 * which connecting to a single resolved address would be inefficient.
 */
// CLR: the constructors and types follow DnsAddressResolverGroup (Class<? extends DatagramChannel> is a Type and
// ChannelFactory<? extends DatagramChannel> Func<IDatagramChannel>); the final override is a sealed override and
// asAddressResolver() is Resolver's InetNameResolver.AsAddressResolver(). "throws Exception" has no counterpart.
public class RoundRobinDnsAddressResolverGroup : DnsAddressResolverGroup
{

    public RoundRobinDnsAddressResolverGroup(DnsNameResolverBuilder dnsResolverBuilder) : base(dnsResolverBuilder)
    {
    }

    public RoundRobinDnsAddressResolverGroup(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type channelType,
            IDnsServerAddressStreamProvider nameServerProvider) : base(channelType, nameServerProvider)
    {
    }

    public RoundRobinDnsAddressResolverGroup(
            Func<IDatagramChannel> channelFactory,
            IDnsServerAddressStreamProvider nameServerProvider) : base(channelFactory, nameServerProvider)
    {
    }

    /**
     * We need to override this method, not
     * {@link #newNameResolver(EventLoop, ChannelFactory, DnsServerAddressStreamProvider)},
     * because we need to eliminate possible caching of {@link io.netty.resolver.NameResolver#resolve}
     * by {@link InflightNameResolver} created in
     * {@link #newResolver(EventLoop, ChannelFactory, DnsServerAddressStreamProvider)}.
     */
    protected sealed override IAddressResolver<IPEndPoint> NewAddressResolver(IEventLoop eventLoop,
                                                                          INameResolver<IPAddress> resolver)
    {
        return new RoundRobinInetAddressResolver(eventLoop, resolver).AsAddressResolver();
    }
}
