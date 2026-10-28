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
using System.Net;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver;

/**
 * Creates and manages {@link NameResolver}s so that each {@link EventExecutor} has its own resolver instance.
 */
public abstract class AddressResolverGroup<T> : IDisposable where T : EndPoint
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(AddressResolverGroup<T>));

    /**
     * Note that we do not use a {@link ConcurrentMap} here because it is usually expensive to instantiate a resolver.
     */
    private readonly Dictionary<IEventExecutor, Entry> _resolvers = new(ReferenceEqualityComparer.Instance);

    /**
     * Returns the {@link AddressResolver} associated with the specified {@link EventExecutor}. If there's no associated
     * resolver found, this method creates and returns a new resolver instance created by
     * {@link #newResolver(EventExecutor)} so that the new resolver is reused on another
     * {@code #getResolver(EventExecutor)} call with the same {@link EventExecutor}.
     */
    public IAddressResolver<T> GetResolver(IEventExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        lock (_resolvers)
        {
            if (executor.IsShuttingDown())
                throw new InvalidOperationException("Executor not accepting a task.");
            if (_resolvers.TryGetValue(executor, out var current))
                return current.Resolver;
            IAddressResolver<T> resolver;
            try
            { resolver = NewResolver(executor); }
            catch (Exception error) { throw new InvalidOperationException("Failed to create a new resolver.", error); }
            var entry = new Entry(this, executor, resolver);
            _resolvers.Add(executor, entry);
            // CLR lifecycle cleanup does not submit work to an executor that has already terminated.
            // Clearing Entry detaches ownership; the Task then retains only an empty continuation state.
            // Do not retain the registering caller's ExecutionContext/SynchronizationContext
            // for the executor's lifetime. Entry is cleared when the group closes.
            executor.Termination.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(entry.OnTermination);
            return resolver;
        }
    }

    /**
     * Invoked by {@link #getResolver(EventExecutor)} to create a new {@link AddressResolver}.
     */
    protected abstract IAddressResolver<T> NewResolver(IEventExecutor executor);

    /**
     * Closes all {@link NameResolver}s created by this group.
     */
    public void Dispose()
    {
        var resolvers = new List<IAddressResolver<T>>();
        lock (_resolvers)
        {
            foreach (var entry in _resolvers.Values)
                resolvers.Add(entry.Detach());
            _resolvers.Clear();
        }
        foreach (var resolver in resolvers)
            CloseResolver(resolver);
    }

    private void OnTermination(Entry entry)
    {
        IAddressResolver<T> resolver;
        lock (_resolvers)
        {
            if (entry.Executor == null)
                return;
            _resolvers.Remove(entry.Executor);
            resolver = entry.Detach();
        }
        CloseResolver(resolver);
    }

    private static void CloseResolver(IAddressResolver<T> resolver)
    {
        try
        { resolver.Dispose(); }
        catch (Exception error) { Logger.Warn("Failed to close a resolver:", error); }
    }

    private sealed class Entry(AddressResolverGroup<T> owner, IEventExecutor executor, IAddressResolver<T> resolver)
    {
        private AddressResolverGroup<T> _owner = owner;
        internal IEventExecutor Executor = executor;
        internal IAddressResolver<T> Resolver = resolver;
        internal void OnTermination() => Volatile.Read(ref _owner)?.OnTermination(this);
        internal IAddressResolver<T> Detach()
        {
            var result = Resolver;
            Resolver = null;
            Executor = null;
            Volatile.Write(ref _owner, null);
            return result;
        }
    }
}
