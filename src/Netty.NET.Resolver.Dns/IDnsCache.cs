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
using System.Net;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Resolver.Dns;

/**
 * A cache for DNS resolution entries.
 */
// CLR: List<? extends DnsCacheEntry> is IReadOnlyList<IDnsCacheEntry> (covariant, so an implementation may return a list
// of its own entry type); InetAddress is IPAddress, Throwable is Exception and EventLoop the Transport IEventLoop
// (docs/handler-porting.md, Addresses).
public interface IDnsCache
{

    /**
     * Clears all the resolved addresses cached by this resolver.
     *
     * @see #clear(String)
     */
    void Clear();

    /**
     * Clears the resolved addresses of the specified host name from the cache of this resolver.
     *
     * @return {@code true} if and only if there was an entry for the specified host name in the cache and
     *         it has been removed by this method
     */
    bool Clear(string hostname);

    /**
     * Return the cached entries for the given hostname.
     * @param hostname the hostname
     * @param additionals the additional records
     * @return the cached entries
     */
    IReadOnlyList<IDnsCacheEntry> Get(string hostname, IDnsRecord[] additionals);

    /**
     * Create a new {@link DnsCacheEntry} and cache a resolved address for a given hostname.
     * @param hostname the hostname
     * @param additionals the additional records
     * @param address the resolved address
     * @param originalTtl the TTL as returned by the DNS server
     * @param loop the {@link EventLoop} used to register the TTL timeout
     * @return The {@link DnsCacheEntry} corresponding to this cache entry.
     */
    IDnsCacheEntry Cache(string hostname, IDnsRecord[] additionals, IPAddress address, long originalTtl,
                        IEventLoop loop);

    /**
     * Cache the resolution failure for a given hostname.
     * Be aware this <strong>won't</strong> be called with timeout / cancel / transport exceptions.
      *
     * @param hostname the hostname
     * @param additionals the additional records
     * @param cause the resolution failure
     * @param loop the {@link EventLoop} used to register the TTL timeout
     * @return The {@link DnsCacheEntry} corresponding to this cache entry, or {@code null} if this cache doesn't
     * support caching failed responses.
     */
    IDnsCacheEntry Cache(string hostname, IDnsRecord[] additionals, Exception cause, IEventLoop loop);
}
