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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels.Pool;

/**
 * A skeletal {@link ChannelPoolMap} implementation. To find the right {@link ChannelPool}
 * the {@link Object#hashCode()} and {@link Object#equals(Object)} is used.
 */
public abstract class AbstractChannelPoolMap<TKey, TPool> : IChannelPoolMap<TKey, TPool>,
    IEnumerable<KeyValuePair<TKey, TPool>>, IDisposable, IAsyncDisposable
    where TKey : notnull where TPool : class, IChannelPool
{
    private readonly ConcurrentDictionary<TKey, TPool> _pools = new();
    public TPool Get(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_pools.TryGetValue(key, out var pool)) return pool;
        pool = NewPool(key);
        var winner = _pools.GetOrAdd(key, pool);
        if (!ReferenceEquals(pool, winner))
        {
            // We need to destroy the newly created pool as we not use it.
            _ = ObserveCloseAsync(pool);
        }
        return winner;
    }

    /**
     * Remove the {@link ChannelPool} from this {@link AbstractChannelPoolMap}. Returns {@code true} if removed,
     * {@code false} otherwise.
     *
     * If the removed pool extends {@link SimpleChannelPool} it will be closed asynchronously to avoid blocking in
     * this method.
     *
     * Please note that {@code null} keys are not allowed.
     */
    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_pools.TryRemove(key, out var pool)) return false;
        _ = ObserveCloseAsync(pool);
        return true;
    }

    /**
     * Remove the {@link ChannelPool} from this {@link AbstractChannelPoolMap}. Returns a future that comletes with a
     * {@code true} result if the pool has been removed by this call, otherwise the result is {@code false}.
     *
     * If the removed pool extends {@link SimpleChannelPool} it will be closed asynchronously to avoid blocking in
     * this method. The returned future will be completed once this asynchronous pool close operation completes.
     */
    public async Task<bool> RemoveAsync(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_pools.TryRemove(key, out var pool)) return false;
        await pool.CloseAsync().ConfigureAwait(false);
        return true;
    }

    // CLR: every pool exposes native CloseAsync. No blocking GlobalEventExecutor bridge is needed.
    /**
     * If the pool implementation supports asynchronous close, then use it to avoid a blocking close call in case
     * the ChannelPoolMap operations are called from an EventLoop.
     *
     * @param pool the ChannelPool to be closed
     */
    private static async Task ObserveCloseAsync(TPool pool)
    {
        try { await pool.CloseAsync().ConfigureAwait(false); }
        catch { /* Source Remove/Get do not expose their asynchronous close result. */ }
    }

    /**
     * Returns the number of {@link ChannelPool}s currently in this {@link AbstractChannelPoolMap}.
     */
    public int Size => _pools.Count;
    /**
     * Returns {@code true} if the {@link AbstractChannelPoolMap} is empty, otherwise {@code false}.
     */
    public bool IsEmpty => _pools.IsEmpty;
    public bool Contains(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _pools.ContainsKey(key);
    }
    /**
     * Called once a new {@link ChannelPool} needs to be created as non exists yet for the {@code key}.
     */
    protected abstract TPool NewPool(TKey key);
    public IEnumerator<KeyValuePair<TKey, TPool>> GetEnumerator() => _pools.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public async Task CloseAsync()
    {
        foreach (var key in _pools.Keys)
        {
            // Wait for remove to finish to ensure that resources are released before returning from close
            await RemoveAsync(key).ConfigureAwait(false);
        }
    }
    public void Close() => CloseAsync().GetAwaiter().GetResult();
    public void Dispose() => Close();
    public ValueTask DisposeAsync() => new(CloseAsync());
}
