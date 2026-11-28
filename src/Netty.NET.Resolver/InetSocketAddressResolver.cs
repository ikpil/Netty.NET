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
using Netty.NET.Common.Internal;

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

    public override bool IsSupported(EndPoint address)
    {
        return address is IPEndPoint or DnsEndPoint;
    }

    protected override bool DoIsResolved(EndPoint address)
    {
        return address is IPEndPoint;
    }

    protected override async Task<IPEndPoint> DoResolveAsync(EndPoint unresolvedAddress, CancellationToken cancellationToken)
    {
        // Note that InetSocketAddress.getHostName() will never incur a reverse lookup here,
        // because an unresolved address always has a host name.
        // CLR: DnsEndPoint.Host never performs reverse DNS. Honor its optional address-family constraint.
        var endpoint = (DnsEndPoint)unresolvedAddress;
        if (endpoint.AddressFamily != AddressFamily.Unspecified)
        {
            IReadOnlyList<IPEndPoint> socketAddresses = await DoResolveAllAsync(endpoint, cancellationToken).ConfigureAwait(false);
            if (socketAddresses.Count == 0)
            {
                // CLR: Common's UnknownHostException (owner decision 2026-11-25), as DefaultNameResolver reports a host
                // without addresses.
                throw new UnknownHostException(endpoint.Host);
            }

            return socketAddresses[0];
        }

        IPAddress inetAddress = await _nameResolver.ResolveAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        return new IPEndPoint(inetAddress, endpoint.Port);
    }

    protected override async Task<IReadOnlyList<IPEndPoint>> DoResolveAllAsync(EndPoint unresolvedAddress,
        CancellationToken cancellationToken)
    {
        // Note that InetSocketAddress.getHostName() will never incur a reverse lookup here,
        // because an unresolved address always has a host name.
        var endpoint = (DnsEndPoint)unresolvedAddress;
        IReadOnlyList<IPAddress> inetAddresses =
            await _nameResolver.ResolveAllAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        var socketAddresses = new List<IPEndPoint>(inetAddresses.Count);

        foreach (IPAddress inetAddress in inetAddresses)
        {
            if (endpoint.AddressFamily == AddressFamily.Unspecified || inetAddress.AddressFamily == endpoint.AddressFamily)
            {
                socketAddresses.Add(new IPEndPoint(inetAddress, endpoint.Port));
            }
        }

        return socketAddresses;
    }

    public override void Dispose()
    {
        _nameResolver.Dispose();
    }
}
