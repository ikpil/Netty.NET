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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Resolver;

/**
 * A {@link NameResolver} that resolves {@link InetAddress} and force Round Robin by choosing a single address
 * randomly in {@link #resolve(String)} and {@link #resolve(String, Promise)}
 * if multiple are returned by the {@link NameResolver}.
 * Use {@link #asAddressResolver()} to create a {@link InetSocketAddress} resolver
 */
public class RoundRobinInetAddressResolver : InetNameResolver
{
    private readonly INameResolver<IPAddress> _nameResolver;

    /**
     * @param executor the {@link EventExecutor} which is used to notify the listeners of the {@link Future} returned by
     * {@link #resolve(String)}
     * @param nameResolver the {@link NameResolver} used for name resolution
     */
    public RoundRobinInetAddressResolver(IEventExecutor executor, INameResolver<IPAddress> nameResolver) : base(executor)
    {
        ArgumentNullException.ThrowIfNull(nameResolver);

        _nameResolver = nameResolver;
    }

    protected override async Task<IPAddress> DoResolveAsync(string inetHost, CancellationToken cancellationToken)
    {
        // hijack the doResolve request, but do a doResolveAll request under the hood.
        // Note that InetSocketAddress.getHostName() will never incur a reverse lookup here,
        // because an unresolved address always has a host name.
        IReadOnlyList<IPAddress> inetAddresses =
            await _nameResolver.ResolveAllAsync(inetHost, cancellationToken).ConfigureAwait(false);
        int numAddresses = inetAddresses.Count;
        if (numAddresses > 0)
        {
            // if there are multiple addresses: we shall pick one by one
            // to support the round robin distribution
            return inetAddresses[RandomIndex(numAddresses)];
        }

        // CLR: UnknownHostException is Common's UnknownHostException (owner decision 2026-11-25).
        throw new UnknownHostException(inetHost);
    }

    protected override async Task<IReadOnlyList<IPAddress>> DoResolveAllAsync(string inetHost, CancellationToken cancellationToken)
    {
        IReadOnlyList<IPAddress> inetAddresses =
            await _nameResolver.ResolveAllAsync(inetHost, cancellationToken).ConfigureAwait(false);
        if (inetAddresses.Count != 0)
        {
            // create a copy to make sure that it's modifiable random access collection
            var result = new IPAddress[inetAddresses.Count];
            // rotate by different distance each time to force round robin distribution
            // CLR: Collections.rotate(result, distance) moves element i to (i + distance) % size.
            int distance = RandomIndex(inetAddresses.Count);

            for (int i = 0; i < result.Length; i++)
            {
                result[(i + distance) % result.Length] = inetAddresses[i];
            }

            return result;
        }

        return inetAddresses;
    }

    private static int RandomIndex(int numAddresses)
    {
        return numAddresses == 1 ? 0 : Random.Shared.Next(numAddresses);
    }

    public override void Dispose()
    {
        _nameResolver.Dispose();
    }
}
