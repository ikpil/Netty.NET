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
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Resolver;

/**
 * A composite {@link SimpleNameResolver} that resolves a host name against a sequence of {@link NameResolver}s.
 *
 * In case of a failure, only the last one will be reported.
 */
public sealed class CompositeNameResolver<T> : SimpleNameResolver<T>
{
    private readonly INameResolver<T>[] _resolvers;

    /**
     * @param executor the {@link EventExecutor} which is used to notify the listeners of the {@link Future} returned
     *                 by {@link #resolve(String)}
     * @param resolvers the {@link NameResolver}s to be tried sequentially
     */
    public CompositeNameResolver(IEventExecutor executor, params INameResolver<T>[] resolvers) : base(executor)
    {
        ObjectUtil.CheckNotNull(resolvers, "resolvers");

        for (int i = 0; i < resolvers.Length; i++)
        {
            ObjectUtil.CheckNotNull(resolvers[i], "resolvers[" + i.ToString(CultureInfo.InvariantCulture) + ']');
        }

        if (resolvers.Length < 2)
        {
            // CLR: Arrays.asList(resolvers).toString() formats as "[a, b]".
            throw new ArgumentException("resolvers: [" + string.Join(", ", (object[])resolvers) +
                    "] (expected: at least 2 resolvers)");
        }

        _resolvers = (INameResolver<T>[])resolvers.Clone();
    }

    protected override Task<T> DoResolveAsync(string inetHost, CancellationToken cancellationToken)
    {
        return DoResolveRecAsync(inetHost, 0, null, cancellationToken);
    }

    // CLR: a Task continuation replaces the promise listener; the recursion over resolverIndex is unchanged.
    private async Task<T> DoResolveRecAsync(string inetHost, int resolverIndex, Exception lastFailure,
        CancellationToken cancellationToken)
    {
        if (resolverIndex >= _resolvers.Length)
        {
            ExceptionDispatchInfo.Throw(lastFailure);
        }

        cancellationToken.ThrowIfCancellationRequested();

        INameResolver<T> resolver = _resolvers[resolverIndex];
        Exception cause;

        try
        {
            return await resolver.ResolveAsync(inetHost, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            cancellationToken.ThrowIfCancellationRequested();

            cause = e;
        }

        return await DoResolveRecAsync(inetHost, resolverIndex + 1, cause, cancellationToken).ConfigureAwait(false);
    }

    protected override Task<IReadOnlyList<T>> DoResolveAllAsync(string inetHost, CancellationToken cancellationToken)
    {
        return DoResolveAllRecAsync(inetHost, 0, null, cancellationToken);
    }

    private async Task<IReadOnlyList<T>> DoResolveAllRecAsync(string inetHost, int resolverIndex, Exception lastFailure,
        CancellationToken cancellationToken)
    {
        if (resolverIndex >= _resolvers.Length)
        {
            ExceptionDispatchInfo.Throw(lastFailure);
        }

        cancellationToken.ThrowIfCancellationRequested();

        INameResolver<T> resolver = _resolvers[resolverIndex];
        Exception cause;

        try
        {
            return await resolver.ResolveAllAsync(inetHost, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            cancellationToken.ThrowIfCancellationRequested();

            cause = e;
        }

        return await DoResolveAllRecAsync(inetHost, resolverIndex + 1, cause, cancellationToken).ConfigureAwait(false);
    }
    // Like the original, this composite does not own or close its component resolvers.
}
