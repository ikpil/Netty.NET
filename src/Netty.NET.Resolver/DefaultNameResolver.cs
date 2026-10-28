/*
 * Copyright 2015 The Netty Project
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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Resolver;

/**
 * A {@link InetNameResolver} that resolves using JDK's built-in domain name lookup mechanism.
 * Note that this resolver performs a blocking name lookup from the caller thread.
 */
// CLR: use the OS resolver through System.Net.Dns asynchronously instead of blocking the caller thread.
public class DefaultNameResolver : InetNameResolver
{
    public DefaultNameResolver(IEventExecutor executor) : base(executor) { }

    protected override async Task<IPAddress> DoResolveAsync(string inetHost, CancellationToken cancellationToken)
    {
        var addresses = await DoResolveAllAsync(inetHost, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0)
            throw new SocketException((int)SocketError.HostNotFound);
        return addresses[0];
    }

    protected override async Task<IReadOnlyList<IPAddress>> DoResolveAllAsync(string inetHost, CancellationToken cancellationToken)
    {
        // InetAddress treats null and the empty host as loopback; Dns treats the empty host as this machine.
        if (string.IsNullOrEmpty(inetHost))
            return new[] { IPAddress.Loopback };
        if (IPAddress.TryParse(inetHost, out var literal))
            return new[] { literal.IsIPv4MappedToIPv6 ? literal.MapToIPv4() : literal };
        return await Dns.GetHostAddressesAsync(inetHost, cancellationToken).ConfigureAwait(false);
    }
}
