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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Resolver;

/**
 * A skeletal {@link AddressResolver} implementation.
 */
public abstract class AbstractAddressResolver<T> : IAddressResolver<T> where T : EndPoint
{
    /**
     * @param executor the {@link EventExecutor} which is used to notify the listeners of the {@link Future} returned
     *                 by {@link #resolve(SocketAddress)}
     */
    /**
     * @param executor the {@link EventExecutor} which is used to notify the listeners of the {@link Future} returned
     *                 by {@link #resolve(SocketAddress)}
     * @param addressType the type of the {@link SocketAddress} supported by this resolver
     */
    // CLR: reified generics replace TypeParameterMatcher and its explicit Class overload.
    protected AbstractAddressResolver(IEventExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        Executor = executor;
    }

    /**
     * Returns the {@link EventExecutor} which is used to notify the listeners of the {@link Future} returned
     * by {@link #resolve(SocketAddress)}.
     */
    protected IEventExecutor Executor { get; }

    public virtual bool IsSupported(EndPoint address) => address is T;

    public bool IsResolved(EndPoint address)
    {
        if (!IsSupported(address))
            throw new NotSupportedException("Address type not supported by the resolver.");
        return DoIsResolved(address);
    }

    /**
     * Invoked by {@link #isResolved(SocketAddress)} to check if the specified {@code address} has been resolved
     * already.
     */
    // CLR: the input can be DnsEndPoint even when the resolved T is IPEndPoint.
    protected abstract bool DoIsResolved(EndPoint address);

    public Task<T> ResolveAsync(EndPoint address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!IsSupported(address))
        {
            // Address type not supported by the resolver
            return Task.FromException<T>(new NotSupportedException("Address type not supported by the resolver."));
        }
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);
        if (IsResolved(address))
        {
            // Resolved already; no need to perform a lookup
            return Task.FromResult((T)address);
        }
        try
        { return DoResolveAsync(address, cancellationToken); }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested && error.CancellationToken == cancellationToken)
        { return Task.FromCanceled<T>(cancellationToken); }
        catch (Exception error) { return Task.FromException<T>(error); }
    }

    public Task<IReadOnlyList<T>> ResolveAllAsync(EndPoint address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!IsSupported(address))
        {
            // Address type not supported by the resolver
            return Task.FromException<IReadOnlyList<T>>(new NotSupportedException("Address type not supported by the resolver."));
        }
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<T>>(cancellationToken);
        if (IsResolved(address))
        {
            // Resolved already; no need to perform a lookup
            return Task.FromResult<IReadOnlyList<T>>(new[] { (T)address });
        }
        try
        { return DoResolveAllAsync(address, cancellationToken); }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested && error.CancellationToken == cancellationToken)
        { return Task.FromCanceled<IReadOnlyList<T>>(cancellationToken); }
        catch (Exception error) { return Task.FromException<IReadOnlyList<T>>(error); }
    }

    /**
     * Invoked by {@link #resolve(SocketAddress)} to perform the actual name
     * resolution.
     */
    protected abstract Task<T> DoResolveAsync(EndPoint unresolvedAddress, CancellationToken cancellationToken);
    /**
     * Invoked by {@link #resolveAll(SocketAddress)} to perform the actual name
     * resolution.
     */
    protected abstract Task<IReadOnlyList<T>> DoResolveAllAsync(EndPoint unresolvedAddress, CancellationToken cancellationToken);
    public virtual void Dispose() { }
}
