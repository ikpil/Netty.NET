/*
 * Copyright 2014 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Resolver;

/**
 * A skeletal {@link NameResolver} implementation.
 */
public abstract class SimpleNameResolver<T> : INameResolver<T>
{
    /**
     * @param executor the {@link EventExecutor} which is used to notify the listeners of the {@link Future} returned
     *                 by {@link #resolve(String)}
     */
    protected SimpleNameResolver(IEventExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        Executor = executor;
    }

    /**
     * Returns the {@link EventExecutor} which is used to notify the listeners of the {@link Future} returned
     * by {@link #resolve(String)}.
     */
    // CLR: Tasks do not impose executor affinity on their consumers.
    protected IEventExecutor Executor { get; }

    public Task<T> ResolveAsync(string inetHost, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);
        try
        { return DoResolveAsync(inetHost, cancellationToken); }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested && error.CancellationToken == cancellationToken)
        { return Task.FromCanceled<T>(cancellationToken); }
        catch (Exception error) { return Task.FromException<T>(error); }
    }

    public Task<IReadOnlyList<T>> ResolveAllAsync(string inetHost, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<T>>(cancellationToken);
        try
        { return DoResolveAllAsync(inetHost, cancellationToken); }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested && error.CancellationToken == cancellationToken)
        { return Task.FromCanceled<IReadOnlyList<T>>(cancellationToken); }
        catch (Exception error) { return Task.FromException<IReadOnlyList<T>>(error); }
    }

    /**
     * Invoked by {@link #resolve(String)} to perform the actual name resolution.
     */
    protected abstract Task<T> DoResolveAsync(string inetHost, CancellationToken cancellationToken);
    /**
     * Invoked by {@link #resolveAll(String)} to perform the actual name resolution.
     */
    protected abstract Task<IReadOnlyList<T>> DoResolveAllAsync(string inetHost, CancellationToken cancellationToken);
    public virtual void Dispose() { }
}
