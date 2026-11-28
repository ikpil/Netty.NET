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
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Resolver.Dns;

/**
 * {@link AuthoritativeDnsServerCache} implementation which delegates all operations to a wrapped {@link DnsCache}.
 * This implementation is only present to preserve a upgrade story.
 */
// CLR: InetSocketAddress is EndPoint; new InetSocketAddress(addr, port) is IPEndPoint, isUnresolved() is a
// DnsEndPoint and getAddress() the IPEndPoint's Address.
internal sealed class AuthoritativeDnsServerCacheAdapter : IAuthoritativeDnsServerCache
{

    private static readonly IDnsRecord[] EMPTY = Array.Empty<IDnsRecord>();
    private readonly IDnsCache cache;

    internal AuthoritativeDnsServerCacheAdapter(IDnsCache cache)
    {
        this.cache = ObjectUtil.CheckNotNull(cache, "cache");
    }

    public IDnsServerAddressStream Get(string hostname)
    {
        IReadOnlyList<IDnsCacheEntry> entries = cache.Get(hostname, EMPTY);
        if (entries == null || entries.Count == 0)
        {
            return null;
        }
        if (entries[0].Cause() != null)
        {
            return null;
        }

        List<EndPoint> addresses = new List<EndPoint>(entries.Count);

        int i = 0;

        do
        {
            IPAddress addr = entries[i].Address();
            addresses.Add(new IPEndPoint(addr, DefaultDnsServerAddressStreamProvider.DNS_PORT));
        } while (++i < entries.Count);

        return new SequentialDnsServerAddressStream(addresses, 0);
    }

    public void Cache(string hostname, EndPoint address, long originalTtl, IEventLoop loop)
    {
        // We only cache resolved addresses.
        if (!(address is DnsEndPoint))
        {
            cache.Cache(hostname, EMPTY, ((IPEndPoint) address).Address, originalTtl, loop);
        }
    }

    public void Clear()
    {
        cache.Clear();
    }

    public bool Clear(string hostname)
    {
        return cache.Clear(hostname);
    }
}
