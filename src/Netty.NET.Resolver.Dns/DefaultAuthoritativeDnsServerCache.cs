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
using System.Globalization;
using System.Linq;
using System.Net;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Resolver.Dns;

/**
 * Default implementation of {@link AuthoritativeDnsServerCache}, backed by a {@link ConcurrentMap}.
 */
// CLR: InetSocketAddress is EndPoint (IPEndPoint resolved, DnsEndPoint unresolved; docs/handler-porting.md Addresses)
// and Comparator<InetSocketAddress> is IComparer<EndPoint>. getHostString() "Returns the hostname, or the String form of
// the address if it doesn't have a hostname"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetSocketAddress.html): it is the Host of a
// DnsEndPoint and the address text of an IPEndPoint, because IPAddress carries no host name (see GetHostString). A
// resolved name server therefore replaces an unresolved entry only when the DnsEndPoint host is that address text,
// where the JDK also matches the host name of an InetAddress created with InetAddress.getByAddress(host, bytes).
// equalsIgnoreCase is an ordinal case-insensitive comparison. Numbers in toString() and messages use the invariant
// culture.
public class DefaultAuthoritativeDnsServerCache : IAuthoritativeDnsServerCache
{

    private readonly int minTtl;
    private readonly int maxTtl;
    private readonly IComparer<EndPoint> comparator;
    // CLR: the anonymous Cache<InetSocketAddress> subclass is the named nested NameServerCache; it reads the enclosing
    // instance's comparator when it sorts, so the constructor creates it with this instance.
    private readonly Cache<EndPoint> resolveCache;

    private sealed class NameServerCache : Cache<EndPoint>
    {
        private readonly DefaultAuthoritativeDnsServerCache parent;

        internal NameServerCache(DefaultAuthoritativeDnsServerCache parent)
        {
            this.parent = parent;
        }

        protected override bool ShouldReplaceAll(EndPoint entry)
        {
            return false;
        }

        protected override bool Equals(EndPoint entry, EndPoint otherEntry)
        {
            return string.Equals(GetHostString(entry), GetHostString(otherEntry), StringComparison.OrdinalIgnoreCase);
        }

        // CLR: Collections.sort(entries, comparator) "is guaranteed to be stable"
        // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Collections.html), while
        // List<T>.Sort "performs an unstable sort"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.list-1.sort?view=net-10.0); the
        // entries are sorted with Enumerable.OrderBy, which "performs a stable sort"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.orderby?view=net-10.0), and written
        // back into the list.
        protected override void SortEntries(string hostname, List<EndPoint> entries)
        {
            IComparer<EndPoint> comparator = parent.comparator;
            if (comparator != null)
            {
                List<EndPoint> sorted = entries.OrderBy(e => e, comparator).ToList();
                entries.Clear();
                entries.AddRange(sorted);
            }
        }
    }

    /**
     * Create a cache that respects the TTL returned by the DNS server.
     */
    public DefaultAuthoritativeDnsServerCache() : this(0, Cache<EndPoint>.MAX_SUPPORTED_TTL_SECS, null)
    {
    }

    /**
     * Create a cache.
     *
     * @param minTtl the minimum TTL
     * @param maxTtl the maximum TTL
     * @param comparator the {@link Comparator} to order the {@link InetSocketAddress} for a hostname or {@code null}
     *                   if insertion order should be used.
     */
    public DefaultAuthoritativeDnsServerCache(int minTtl, int maxTtl, IComparer<EndPoint> comparator)
    {
        resolveCache = new NameServerCache(this);
        this.minTtl = Math.Min(Cache<EndPoint>.MAX_SUPPORTED_TTL_SECS, ObjectUtil.CheckPositiveOrZero(minTtl, "minTtl"));
        this.maxTtl = Math.Min(Cache<EndPoint>.MAX_SUPPORTED_TTL_SECS, ObjectUtil.CheckPositive(maxTtl, "maxTtl"));
        if (minTtl > maxTtl)
        {
            throw new ArgumentException(
                    "minTtl: " + minTtl.ToString(CultureInfo.InvariantCulture) + ", maxTtl: " +
                    maxTtl.ToString(CultureInfo.InvariantCulture) + " (expected: 0 <= minTtl <= maxTtl)");
        }

        this.comparator = comparator;
    }

    public IDnsServerAddressStream Get(string hostname)
    {
        ObjectUtil.CheckNotNull(hostname, "hostname");

        IReadOnlyList<EndPoint> addresses = resolveCache.Get(hostname);
        if (addresses == null || addresses.Count == 0)
        {
            return null;
        }

        return new SequentialDnsServerAddressStream(addresses, 0);
    }

    public void Cache(string hostname, EndPoint address, long originalTtl, IEventLoop loop)
    {
        ObjectUtil.CheckNotNull(hostname, "hostname");
        ObjectUtil.CheckNotNull(address, "address");
        ObjectUtil.CheckNotNull(loop, "loop");

        if (GetHostString(address) == null)
        {
            // We only cache addresses that have also a host string as we will need it later when trying to replace
            // unresolved entries in the cache.
            return;
        }

        resolveCache.Cache_(hostname, address, Math.Max(minTtl, (int) Math.Min(maxTtl, originalTtl)), loop);
    }

    public void Clear()
    {
        resolveCache.Clear();
    }

    public bool Clear(string hostname)
    {
        return resolveCache.Clear(ObjectUtil.CheckNotNull(hostname, "hostname"));
    }

    public override string ToString()
    {
        return "DefaultAuthoritativeDnsServerCache(minTtl=" + minTtl.ToString(CultureInfo.InvariantCulture) +
                ", maxTtl=" + maxTtl.ToString(CultureInfo.InvariantCulture) + ", cached nameservers=" +
                resolveCache.Size().ToString(CultureInfo.InvariantCulture) + ')';
    }

    // Package visibility for testing purposes
    internal int MinTtl()
    {
        return minTtl;
    }

    // Package visibility for testing purposes
    internal int MaxTtl()
    {
        return maxTtl;
    }

    // CLR: InetSocketAddress.getHostString() (see the class comment): the host of an unresolved DnsEndPoint, the address
    // text of an IPEndPoint, and null for any other EndPoint.
    private static string GetHostString(EndPoint address)
    {
        switch (address)
        {
            case DnsEndPoint unresolved:
                return unresolved.Host;
            case IPEndPoint resolved:
                return resolved.Address.ToString();
            default:
                return null;
        }
    }
}
