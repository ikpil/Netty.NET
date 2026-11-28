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

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Resolver.Dns;

// CLR: Promise<?> originalPromise is the Task of the original promise and Promise<List<InetAddress>> a
// TaskCompletionSource<IReadOnlyList<IPAddress>>, as in DnsResolveContext. DnsRecord.CLASS_IN is
// IDnsRecord.CLASS_IN. resolved.getClass() == addressType(...) compares the AddressFamily
// (DnsNameResolver.AddressType). Collections.sort is stable and is the stable DnsNameResolver.SortStable
// (Enumerable.OrderBy, see DnsNameResolver.NewRedirectDnsServerStream). UnknownHostException is Common's
// UnknownHostException; EventLoop is IEventLoop.
internal sealed class DnsAddressResolveContext : DnsResolveContext<IPAddress>
{

    private readonly IDnsCache resolveCache;
    private readonly IAuthoritativeDnsServerCache authoritativeDnsServerCache;
    private readonly bool completeEarlyIfPossible;

    internal DnsAddressResolveContext(DnsNameResolver parent, IChannel channel,
                             Task originalPromise, string hostname, IDnsRecord[] additionals,
                             IDnsServerAddressStream nameServerAddrs, int allowedQueries, IDnsCache resolveCache,
                             IAuthoritativeDnsServerCache authoritativeDnsServerCache,
                             bool completeEarlyIfPossible)
        : base(parent, channel, originalPromise, hostname, IDnsRecord.CLASS_IN,
              parent.ResolveRecordTypes(), additionals, nameServerAddrs, allowedQueries)
    {
        this.resolveCache = resolveCache;
        this.authoritativeDnsServerCache = authoritativeDnsServerCache;
        this.completeEarlyIfPossible = completeEarlyIfPossible;
    }

    internal override DnsResolveContext<IPAddress> NewResolverContext(DnsNameResolver parent, IChannel channel,
                                                      Task originalPromise,
                                                      string hostname,
                                                      int dnsClass, DnsRecordType[] expectedTypes,
                                                      IDnsRecord[] additionals,
                                                      IDnsServerAddressStream nameServerAddrs, int allowedQueries)
    {
        return new DnsAddressResolveContext(parent, channel, originalPromise, hostname, additionals,
                nameServerAddrs, allowedQueries, resolveCache, authoritativeDnsServerCache, completeEarlyIfPossible);
    }

    internal override IPAddress ConvertRecord(IDnsRecord record, string hostname, IDnsRecord[] additionals,
                                              IEventLoop eventLoop)
    {
        return DnsAddressDecoder.DecodeAddress(record, hostname, parent.IsDecodeIdn());
    }

    internal override List<IPAddress> FilterResults(List<IPAddress> unfiltered)
    {
        DnsNameResolver.SortStable(unfiltered, PreferredAddressTypeComparator.Comparator(parent.PreferredAddressType()));
        return unfiltered;
    }

    internal override bool IsCompleteEarly(IPAddress resolved)
    {
        return completeEarlyIfPossible &&
                DnsNameResolver.AddressType(parent.PreferredAddressType()) == resolved.AddressFamily;
    }

    internal override bool IsDuplicateAllowed()
    {
        // We don't want include duplicates to mimic JDK behaviour.
        return false;
    }

    internal override void Cache(string hostname, IDnsRecord[] additionals,
               IDnsRecord result, IPAddress convertedResult)
    {
        resolveCache.Cache(hostname, additionals, convertedResult, result.TimeToLive(),
                Channel().EventLoop);
    }

    internal override void Cache(string hostname, IDnsRecord[] additionals, UnknownHostException cause)
    {
        resolveCache.Cache(hostname, additionals, cause, Channel().EventLoop);
    }

    internal override void DoSearchDomainQuery(string hostname,
                                               TaskCompletionSource<IReadOnlyList<IPAddress>> nextPromise)
    {
        // Query the cache for the hostname first and only do a query if we could not find it in the cache.
        if (!DnsNameResolver.DoResolveAllCached(hostname, additionals, nextPromise, resolveCache,
                parent.SearchDomains(), parent.Ndots(), parent.ResolvedInternetProtocolFamiliesUnsafe()))
        {
            base.DoSearchDomainQuery(hostname, nextPromise);
        }
    }

    internal override IDnsCache ResolveCache()
    {
        return resolveCache;
    }

    internal override IAuthoritativeDnsServerCache AuthoritativeDnsServerCache()
    {
        return authoritativeDnsServerCache;
    }
}
