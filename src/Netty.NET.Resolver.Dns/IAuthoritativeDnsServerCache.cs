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

using System.Net;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Resolver.Dns;

/**
 * Cache which stores the nameservers that should be used to resolve a specific hostname.
 */
// CLR: InetSocketAddress is EndPoint: an IPEndPoint when resolved and a DnsEndPoint when unresolved; EventLoop is
// the Transport IEventLoop.
public interface IAuthoritativeDnsServerCache
{

    /**
     * Returns the cached nameservers that should be used to resolve the given hostname. The returned
     * {@link DnsServerAddressStream} may contain unresolved {@link InetSocketAddress}es that will be resolved
     * when needed while resolving other domain names.
     *
     * @param hostname the hostname
     * @return the cached entries or an {@code null} if none.
     */
    IDnsServerAddressStream Get(string hostname);

    /**
     * Caches a nameserver that should be used to resolve the given hostname.
     *
     * @param hostname the hostname
     * @param address the nameserver address (which may be unresolved).
     * @param originalTtl the TTL as returned by the DNS server
     * @param loop the {@link EventLoop} used to register the TTL timeout
     */
    void Cache(string hostname, EndPoint address, long originalTtl, IEventLoop loop);

    /**
     * Clears all cached nameservers.
     *
     * @see #clear(String)
     */
    void Clear();

    /**
     * Clears the cached nameservers for the specified hostname.
     *
     * @return {@code true} if and only if there was an entry for the specified host name in the cache and
     *         it has been removed by this method
     */
    bool Clear(string hostname);
}
