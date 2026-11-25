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
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Resolver.Dns;

// CLR: the package-private final class is internal sealed over DnsResolveContext<IDnsRecord> (DnsRecord is the codec-dns
// IDnsRecord); Promise<?> originalPromise is the Task of the original promise, as in DnsResolveContext.
// UnknownHostException is Common's UnknownHostException (owner decision 2026-11-25); EventLoop is IEventLoop.
internal sealed class DnsRecordResolveContext : DnsResolveContext<IDnsRecord>
{

    internal DnsRecordResolveContext(DnsNameResolver parent, IChannel channel,
                            Task originalPromise, IDnsQuestion question, IDnsRecord[] additionals,
                            IDnsServerAddressStream nameServerAddrs, int allowedQueries)
        : this(parent, channel, originalPromise, question.Name(), question.DnsClass(),
             new DnsRecordType[] { question.Type() },
             additionals, nameServerAddrs, allowedQueries)
    {
    }

    private DnsRecordResolveContext(DnsNameResolver parent, IChannel channel,
                                    Task originalPromise,
                                    string hostname, int dnsClass, DnsRecordType[] expectedTypes,
                                    IDnsRecord[] additionals,
                                    IDnsServerAddressStream nameServerAddrs,
                                    int allowedQueries)
        : base(parent, channel, originalPromise, hostname, dnsClass, expectedTypes,
                additionals, nameServerAddrs, allowedQueries)
    {
    }

    internal override DnsResolveContext<IDnsRecord> NewResolverContext(DnsNameResolver parent, IChannel channel,
                                                    Task originalPromise,
                                                    string hostname,
                                                    int dnsClass, DnsRecordType[] expectedTypes,
                                                    IDnsRecord[] additionals,
                                                    IDnsServerAddressStream nameServerAddrs,
                                                    int allowedQueries)
    {
        return new DnsRecordResolveContext(parent, channel, originalPromise, hostname, dnsClass,
                                           expectedTypes, additionals, nameServerAddrs, allowedQueries);
    }

    internal override IDnsRecord ConvertRecord(IDnsRecord record, string hostname, IDnsRecord[] additionals,
                                               IEventLoop eventLoop)
    {
        return ReferenceCountUtil.Retain(record);
    }

    internal override List<IDnsRecord> FilterResults(List<IDnsRecord> unfiltered)
    {
        return unfiltered;
    }

    internal override bool IsCompleteEarly(IDnsRecord resolved)
    {
        return false;
    }

    internal override bool IsDuplicateAllowed()
    {
        return true;
    }

    internal override void Cache(string hostname, IDnsRecord[] additionals, IDnsRecord result,
                                 IDnsRecord convertedResult)
    {
        // Do not cache.
        // XXX: When we implement cache, we would need to retain the reference count of the result record.
    }

    internal override void Cache(string hostname, IDnsRecord[] additionals, UnknownHostException cause)
    {
        // Do not cache.
        // XXX: When we implement cache, we would need to retain the reference count of the result record.
    }

    internal override IDnsCnameCache CnameCache()
    {
        // We don't use a cache here at all as we also don't cache if we end up using the DnsRecordResolverContext.
        return NoopDnsCnameCache.INSTANCE;
    }
}
