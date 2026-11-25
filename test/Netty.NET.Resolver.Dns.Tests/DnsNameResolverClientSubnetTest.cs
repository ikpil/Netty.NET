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
using System.Threading.Tasks;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: @Disabled without a value is [Fact(Skip = ...)] with the reason the source comment gives (the test needs real DNS
// servers); the body is ported and compiled. resolveAll(host, singleton(record)) is ResolveAllAsync(host, additionals)
// and future.syncUninterruptibly().getNow() an awaited Task; SocketUtils.addressByName of an address literal is
// IPAddress.Parse and getAddress() GetAddressBytes(); SocketUtils.socketAddress("8.8.8.8", 53) of a literal is
// new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53) (resolver-dns brief); System.out.println is Console.WriteLine;
// shutdownGracefully(0, 0, SECONDS) is ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero) without waiting, as in the
// source.
public class DnsNameResolverClientSubnetTest
{

    // See https://www.gsic.uva.es/~jnisigl/dig-edns-client-subnet.html
    // Ignore as this needs to query real DNS servers.
    [Fact(Skip = "Ignore as this needs to query real DNS servers.")]
    public async Task TestSubnetQuery()
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        DnsNameResolver resolver = NewResolver(group).Build();
        try
        {
            // Same as:
            // # /.bind-9.9.3-edns/bin/dig @ns1.google.com www.google.es +client=157.88.0.0/24
            Task<IReadOnlyList<IPAddress>> future = resolver.ResolveAllAsync("www.google.es",
                    new List<IDnsRecord>
                    {
                            // Suggest max payload size of 1024
                            // 157.88.0.0 / 24
                            new DefaultDnsOptEcsRecord(1024, 24,
                                                       IPAddress.Parse("157.88.0.0").GetAddressBytes())
                    }, TestContext.Current.CancellationToken);
            foreach (IPAddress address in await future)
            {
                Console.WriteLine(address);
            }
        }
        finally
        {
            resolver.Dispose();
            _ = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        }
    }

    private static DnsNameResolverBuilder NewResolver(IEventLoopGroup group)
    {
        return new DnsNameResolverBuilder(group.Next())
                .DatagramChannelType(typeof(NioDatagramChannel))
                .NameServerProvider(
                        new SingletonDnsServerAddressStreamProvider(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53)))
                .MaxQueriesPerResolve(1)
                .OptResourceEnabled(false)
                .Ndots(1);
    }
}
