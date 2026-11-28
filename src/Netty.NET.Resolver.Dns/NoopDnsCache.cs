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
 * A noop DNS cache that actually never caches anything.
 */
public sealed class NoopDnsCache : IDnsCache
{

    public static readonly NoopDnsCache INSTANCE = new NoopDnsCache();

    /**
     * Private singleton constructor.
     */
    private NoopDnsCache()
    {
    }

    public void Clear()
    {
    }

    public bool Clear(string hostname)
    {
        return false;
    }

    public IReadOnlyList<IDnsCacheEntry> Get(string hostname, IDnsRecord[] additionals)
    {
        return Array.Empty<IDnsCacheEntry>();
    }

    public IDnsCacheEntry Cache(string hostname, IDnsRecord[] additional,
                               IPAddress address, long originalTtl, IEventLoop loop)
    {
        return new NoopDnsCacheEntry(address);
    }

    public IDnsCacheEntry Cache(string hostname, IDnsRecord[] additional, Exception cause, IEventLoop loop)
    {
        return null;
    }

    public override string ToString()
    {
        return nameof(NoopDnsCache);
    }

    private sealed class NoopDnsCacheEntry : IDnsCacheEntry
    {
        private readonly IPAddress address;

        internal NoopDnsCacheEntry(IPAddress address)
        {
            this.address = address;
        }

        public IPAddress Address()
        {
            return address;
        }

        public Exception Cause()
        {
            return null;
        }

        public override string ToString()
        {
            return address.ToString();
        }
    }
}
