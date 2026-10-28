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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

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
        ArgumentNullException.ThrowIfNull(resolvers);
        foreach (var resolver in resolvers)
            ArgumentNullException.ThrowIfNull(resolver);
        if (resolvers.Length < 2)
            throw new ArgumentException("Expected at least 2 resolvers.", nameof(resolvers));
        _resolvers = (INameResolver<T>[])resolvers.Clone();
    }

    protected override Task<T> DoResolveAsync(string inetHost, CancellationToken cancellationToken)
        => ResolveSequentiallyAsync(resolver => resolver.ResolveAsync(inetHost, cancellationToken), cancellationToken);

    protected override Task<IReadOnlyList<T>> DoResolveAllAsync(string inetHost, CancellationToken cancellationToken)
        => ResolveSequentiallyAsync(resolver => resolver.ResolveAllAsync(inetHost, cancellationToken), cancellationToken);

    private async Task<TResult> ResolveSequentiallyAsync<TResult>(Func<INameResolver<T>, Task<TResult>> resolve,
        CancellationToken cancellationToken)
    {
        Exception lastFailure = null;
        foreach (var resolver in _resolvers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            { return await resolve(resolver).ConfigureAwait(false); }
            catch (Exception error)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lastFailure = error;
            }
        }
        ExceptionDispatchInfo.Capture(lastFailure).Throw();
        throw new InvalidOperationException();
    }
    // Like the original, this composite does not own or close its component resolvers.
}
