/*
 * Copyright 2017 The Netty Project
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
using System.Linq;

namespace Netty.NET.Resolver.Dns;

/**
 * A {@link DnsServerAddressStreamProvider} which iterates through a collection of
 * {@link DnsServerAddressStreamProvider} until the first non-{@code null} result is found.
 */
// CLR: List<DnsServerAddressStreamProvider> is IList<IDnsServerAddressStreamProvider> and toArray(...) ToArray();
// providers.clone() of the varargs array is a copy of the array (Array.Clone is shallow, like the JDK array clone).
public sealed class MultiDnsServerAddressStreamProvider : IDnsServerAddressStreamProvider
{
    private readonly IDnsServerAddressStreamProvider[] providers;

    /**
     * Create a new instance.
     * @param providers The providers to use for DNS resolution. They will be queried in order.
     */
    public MultiDnsServerAddressStreamProvider(IList<IDnsServerAddressStreamProvider> providers)
    {
        this.providers = providers.ToArray();
    }

    /**
     * Create a new instance.
     * @param providers The providers to use for DNS resolution. They will be queried in order.
     */
    public MultiDnsServerAddressStreamProvider(params IDnsServerAddressStreamProvider[] providers)
    {
        this.providers = (IDnsServerAddressStreamProvider[]) providers.Clone();
    }

    public IDnsServerAddressStream NameServerAddressStream(string hostname)
    {
        foreach (IDnsServerAddressStreamProvider provider in providers)
        {
            IDnsServerAddressStream stream = provider.NameServerAddressStream(hostname);
            if (stream != null)
            {
                return stream;
            }
        }

        return null;
    }
}
