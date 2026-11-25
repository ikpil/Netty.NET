/*
 * Copyright 2019 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Resolver.Dns;

// CLR: the package-private final class is internal sealed. The resolved name server address is IPEndPoint and the
// Promise a TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>>, as in DnsQueryContext; DnsQuery is the
// codec-dns IDnsQuery and new DatagramDnsQuery(null, nameServerAddr, id) is unchanged.
internal sealed class DatagramDnsQueryContext : DnsQueryContext
{

    internal DatagramDnsQueryContext(IChannel channel,
                            IPEndPoint nameServerAddr,
                            DnsQueryContextManager queryContextManager,
                            IDnsQueryLifecycleObserver queryLifecycleObserver,
                            int maxPayLoadSize, bool recursionDesired,
                            long queryTimeoutMillis,
                            IDnsQuestion question, IDnsRecord[] additionals,
                            TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> promise,
                            Bootstrap socketBootstrap, bool retryWithTcpOnTimeout)
        : base(channel, nameServerAddr, queryContextManager, queryLifecycleObserver, maxPayLoadSize, recursionDesired,
                queryTimeoutMillis, question, additionals, promise, socketBootstrap, retryWithTcpOnTimeout)
    {
    }

    protected override IDnsQuery NewQuery(int id, IPEndPoint nameServerAddr)
    {
        return new DatagramDnsQuery(null, nameServerAddr, id);
    }

    protected override string Protocol()
    {
        return "UDP";
    }
}
