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
 * A noop {@link AuthoritativeDnsServerCache} that actually never caches anything.
 */
// CLR: InetSocketAddress is EndPoint and EventLoop the Transport IEventLoop.
public sealed class NoopAuthoritativeDnsServerCache : IAuthoritativeDnsServerCache
{
    public static readonly NoopAuthoritativeDnsServerCache INSTANCE = new NoopAuthoritativeDnsServerCache();

    private NoopAuthoritativeDnsServerCache()
    {
    }

    public IDnsServerAddressStream Get(string hostname)
    {
        return null;
    }

    public void Cache(string hostname, EndPoint address, long originalTtl, IEventLoop loop)
    {
        // NOOP
    }

    public void Clear()
    {
        // NOOP
    }

    public bool Clear(string hostname)
    {
        return false;
    }
}
