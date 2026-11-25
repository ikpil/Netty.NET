/*
 * Copyright 2024 The Netty Project
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
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Resolver.Dns;

/**
 * A thread local based address stream for a specific hostname.
 */
// CLR: FastThreadLocal is Common's FastThreadLocal<V>; its anonymous subclass with initialValue() is the named nested
// ProviderThreadLocal, which reads the enclosing stream's provider and hostname. InetSocketAddress is EndPoint.
internal sealed class ThreadLocalNameServerAddressStream : IDnsServerAddressStream
{

    private readonly string hostname;
    private readonly IDnsServerAddressStreamProvider dnsServerAddressStreamProvider;
    private readonly FastThreadLocal<IDnsServerAddressStream> threadLocal;

    private sealed class ProviderThreadLocal : FastThreadLocal<IDnsServerAddressStream>
    {
        private readonly ThreadLocalNameServerAddressStream parent;

        internal ProviderThreadLocal(ThreadLocalNameServerAddressStream parent)
        {
            this.parent = parent;
        }

        protected override IDnsServerAddressStream InitialValue()
        {
            return parent.dnsServerAddressStreamProvider.NameServerAddressStream(parent.hostname);
        }
    }

    internal ThreadLocalNameServerAddressStream(IDnsServerAddressStreamProvider dnsServerAddressStreamProvider) :
        this(dnsServerAddressStreamProvider, "")
    {
    }

    internal ThreadLocalNameServerAddressStream(IDnsServerAddressStreamProvider dnsServerAddressStreamProvider, string hostname)
    {
        threadLocal = new ProviderThreadLocal(this);
        this.dnsServerAddressStreamProvider = dnsServerAddressStreamProvider;
        this.hostname = hostname;
    }

    public EndPoint Next()
    {
        return threadLocal.Get().Next();
    }

    public IDnsServerAddressStream Duplicate()
    {
        return new ThreadLocalNameServerAddressStream(dnsServerAddressStreamProvider, hostname);
    }

    public int Size()
    {
        return threadLocal.Get().Size();
    }
}
