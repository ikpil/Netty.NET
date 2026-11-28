/*
 * Copyright 2020 The Netty Project
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

using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: DnsResolveContext.cnameResolveFromCache is the static CnameResolveFromCache of the non-generic
// DnsResolveContext, which holds the statics of the generic Java class
// (src/Netty.NET.Resolver.Dns/DnsResolveContext.cs). UnknownHostException is Common's UnknownHostException and
// assertThrows with the anonymous Executable is Assert.Throws with a lambda; Long.MAX_VALUE is long.MaxValue and
// channel.eventLoop() the EmbeddedChannel's EventLoop.
public class DnsResolveContextTest
{

    private const string HOSTNAME = "netty.io.";

    [Fact]
    public void TestCnameLoop()
    {
        for (int i = 1; i < 128; i++)
        {
            int cacheSize = i;
            Assert.Throws<UnknownHostException>(() =>
                    DnsResolveContext.CnameResolveFromCache(BuildCache(cacheSize), HOSTNAME));
        }
    }

    private static IDnsCnameCache BuildCache(int chainLength)
    {
        EmbeddedChannel channel = new EmbeddedChannel();
        IDnsCnameCache cache = new DefaultDnsCnameCache();

        if (chainLength == 1)
        {
            cache.Cache(HOSTNAME, HOSTNAME, long.MaxValue, channel.EventLoop);
        }
        else
        {
            string lastName = HOSTNAME;

            for (int i = 1; i < chainLength; i++)
            {
                string nextName = i + "." + lastName;
                cache.Cache(lastName, nextName, long.MaxValue, channel.EventLoop);
                lastName = nextName;
            }

            cache.Cache(lastName, HOSTNAME, long.MaxValue, channel.EventLoop);
        }

        return cache;
    }
}
