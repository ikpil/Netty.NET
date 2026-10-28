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

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Resolver;

/**
 * A {@link AbstractAddressResolver} that resolves {@link InetSocketAddress}.
 */
public class InetSocketAddressResolver : AbstractAddressResolver<IPEndPoint>
{
    private readonly INameResolver<IPAddress> _nameResolver;

    /**
     * @param executor the {@link EventExecutor} which is used to notify the listeners of the {@link Future} returned
     *                 by {@link #resolve(java.net.SocketAddress)}
     * @param nameResolver the {@link NameResolver} used for name resolution
     */
    public InetSocketAddressResolver(IEventExecutor executor, INameResolver<IPAddress> nameResolver) : base(executor)
    {
        ArgumentNullException.ThrowIfNull(nameResolver);
        _nameResolver = nameResolver;
    }

    public override bool IsSupported(EndPoint address) => address is IPEndPoint or DnsEndPoint;
    protected override bool DoIsResolved(EndPoint address) => address is IPEndPoint;

    protected override async Task<IPEndPoint> DoResolveAsync(EndPoint unresolvedAddress, CancellationToken cancellationToken)
    {
        // Note that InetSocketAddress.getHostName() will never incur a reverse lookup here,
        // because an unresolved address always has a host name.
        // CLR: DnsEndPoint.Host never performs reverse DNS. Honor its optional address-family constraint.
        var endpoint = (DnsEndPoint)unresolvedAddress;
        if (endpoint.AddressFamily != AddressFamily.Unspecified)
        {
            var addresses = await DoResolveAllAsync(endpoint, cancellationToken).ConfigureAwait(false);
            if (addresses.Count == 0)
                throw new SocketException((int)SocketError.HostNotFound);
            return addresses[0];
        }
        var address = await _nameResolver.ResolveAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        return new IPEndPoint(address, endpoint.Port);
    }

    protected override async Task<IReadOnlyList<IPEndPoint>> DoResolveAllAsync(EndPoint unresolvedAddress,
        CancellationToken cancellationToken)
    {
        // Note that InetSocketAddress.getHostName() will never incur a reverse lookup here,
        // because an unresolved address always has a host name.
        var endpoint = (DnsEndPoint)unresolvedAddress;
        var addresses = await _nameResolver.ResolveAllAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        var result = new List<IPEndPoint>(addresses.Count);
        foreach (var address in addresses)
            if (endpoint.AddressFamily == AddressFamily.Unspecified || address.AddressFamily == endpoint.AddressFamily)
                result.Add(new IPEndPoint(address, endpoint.Port));
        return result;
    }

    public override void Dispose() => _nameResolver.Dispose();
}
