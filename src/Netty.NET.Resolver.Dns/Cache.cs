/*
 * Copyright 2018 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Resolver.Dns;

/**
 * Abstract cache that automatically removes entries for a hostname once the TTL for an entry is reached.
 *
 * @param <E>
 */
// CLR: ConcurrentMap<String, Entries> is ConcurrentDictionary<string, Entries>; ConcurrentMap.remove(key, value) is
// TryRemove(KeyValuePair), which removes the entry only when "both the specified key and value" match
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2.tryremove?view=net-10.0);
// putIfAbsent is GetOrAdd. List<? extends E> is IReadOnlyList<E>: emptyList() is Array.Empty, singletonList(e) a
// one-element read-only collection and Collections.unmodifiableList a List.AsReadOnly() view. The cached values are
// reference types (E : class) because the source tests them against null. EventLoop is the Transport IEventLoop.
internal abstract class Cache<E> where E : class
{
    // CLR: FUTURE_UPDATER (an AtomicReferenceFieldUpdater over Entries.expirationFuture) is Interlocked.CompareExchange
    // and Interlocked.Exchange on that field and Volatile.Read for get (docs/handler-porting.md and
    // docs/transport-porting.md record Atomic*FieldUpdater and AtomicReference as Interlocked on the field).

    // CLR: the anonymous ScheduledFuture is the named CancelledScheduledFuture (see ScheduledFuture below).
    private static readonly ScheduledFuture CANCELLED = new CancelledScheduledFuture();

    // Two years are supported by all our EventLoop implementations and so safe to use as maximum.
    // See also: https://github.com/netty/netty/commit/b47fb817991b42ec8808c7d26538f3f2464e1fa6
    // CLR: (int) TimeUnit.DAYS.toSeconds(365 * 2) is the constant 365 * 2 days in seconds.
    internal const int MAX_SUPPORTED_TTL_SECS = 365 * 2 * 24 * 60 * 60;

    private readonly ConcurrentDictionary<string, Entries> resolveCache = new ConcurrentDictionary<string, Entries>();

    /**
     * Remove everything from the cache.
     */
    internal void Clear()
    {
        while (!resolveCache.IsEmpty)
        {
            // CLR: the entry set iterator with i.remove() is the ConcurrentDictionary enumerator, which tolerates
            // concurrent removal, and TryRemove of the key.
            foreach (KeyValuePair<string, Entries> e in resolveCache)
            {
                resolveCache.TryRemove(e.Key, out _);

                e.Value.ClearAndCancel();
            }
        }
    }

    /**
     * Clear all entries (if anything exists) for the given hostname and return {@code true} if anything was removed.
     */
    internal bool Clear(string hostname)
    {
        resolveCache.TryRemove(hostname, out Entries entries);
        return entries != null && entries.ClearAndCancel();
    }

    /**
     * Returns all caches entries for the given hostname.
     */
    internal IReadOnlyList<E> Get(string hostname)
    {
        resolveCache.TryGetValue(hostname, out Entries entries);
        return entries == null ? null : entries.Get();
    }

    /**
     * Cache a value for the given hostname that will automatically expire once the TTL is reached.
     */
    // CLR: cache(...) is Cache_, because a member cannot have the name of its enclosing type (CS0542); the owner rule
    // for such clashes (CS0102) is a trailing underscore (docs/codec-http-porting.md, Name clashes).
    internal void Cache_(string hostname, E value, int ttl, IEventLoop loop)
    {
        resolveCache.TryGetValue(hostname, out Entries entries);
        if (entries == null)
        {
            entries = new Entries(this, hostname);
            // CLR: putIfAbsent returns the previous value or null; GetOrAdd returns the value now mapped, which is the
            // new Entries when it was added.
            Entries oldEntries = resolveCache.GetOrAdd(hostname, entries);
            if (!ReferenceEquals(oldEntries, entries))
            {
                entries = oldEntries;
            }
        }
        entries.Add(value, ttl, loop);
    }

    /**
     * Return the number of hostnames for which we have cached something.
     */
    internal int Size()
    {
        return resolveCache.Count;
    }

    /**
     * Returns {@code true} if this entry should replace all other entries that are already cached for the hostname.
     */
    protected abstract bool ShouldReplaceAll(E entry);

    /**
     * Sort the {@link List} for a {@code hostname} before caching these.
     */
    protected virtual void SortEntries(
            string hostname, List<E> entries)
    {
        // NOOP.
    }

    /**
     * Returns {@code true} if both entries are equal.
     */
    protected abstract bool Equals(E entry, E otherEntry);

    // Directly extend AtomicReference for intrinsics and also to keep memory overhead low.
    // CLR: AtomicReference<List<E>> is Common's AtomicReference<IReadOnlyList<E>> (Interlocked on one field); Runnable
    // is Run(), which the loop schedules as an Action. The inner (non-static) class takes the enclosing Cache (Cache.this)
    // as a constructor argument.
    private sealed class Entries : AtomicReference<IReadOnlyList<E>>
    {
        private readonly Cache<E> cache;
        private readonly string hostname;
        // Needs to be package-private to be able to access it via the AtomicReferenceFieldUpdater
        internal ScheduledFuture expirationFuture;

        internal Entries(Cache<E> cache, string hostname) : base(Array.Empty<E>())
        {
            this.cache = cache;
            this.hostname = hostname;
        }

        internal void Add(E e, int ttl, IEventLoop loop)
        {
            if (!cache.ShouldReplaceAll(e))
            {
                for (;;)
                {
                    IReadOnlyList<E> entries = Get();
                    if (entries.Count != 0)
                    {
                        E firstEntry = entries[0];
                        if (cache.ShouldReplaceAll(firstEntry))
                        {
                            Debug.Assert(entries.Count == 1);

                            if (CompareAndSet(entries, SingletonList(e)))
                            {
                                ScheduleCacheExpirationIfNeeded(ttl, loop);
                                return;
                            }
                            else
                            {
                                // Need to try again as CAS failed
                                continue;
                            }
                        }

                        // Create a new List for COW semantics
                        List<E> newEntries = new List<E>(entries.Count + 1);
                        int i = 0;
                        E replacedEntry = null;
                        do
                        {
                            E entry = entries[i];
                            // Only add old entry if the address is not the same as the one we try to add as well.
                            // In this case we will skip it and just add the new entry as this may have
                            // more up-to-date data and cancel the old after we were able to update the cache.
                            if (!cache.Equals(e, entry))
                            {
                                newEntries.Add(entry);
                            }
                            else
                            {
                                replacedEntry = entry;
                                newEntries.Add(e);

                                ++i;
                                for (; i < entries.Count; ++i)
                                {
                                    newEntries.Add(entries[i]);
                                }
                                break;
                            }
                        } while (++i < entries.Count);
                        if (replacedEntry == null)
                        {
                            newEntries.Add(e);
                        }
                        cache.SortEntries(hostname, newEntries);

                        if (CompareAndSet(entries, newEntries.AsReadOnly()))
                        {
                            ScheduleCacheExpirationIfNeeded(ttl, loop);
                            return;
                        }
                    }
                    else if (CompareAndSet(entries, SingletonList(e)))
                    {
                        ScheduleCacheExpirationIfNeeded(ttl, loop);
                        return;
                    }
                }
            }
            else
            {
                Set(SingletonList(e));
                ScheduleCacheExpirationIfNeeded(ttl, loop);
            }
        }

        private void ScheduleCacheExpirationIfNeeded(int ttl, IEventLoop loop)
        {
            for (;;)
            {
                // We currently don't calculate a new TTL when we need to retry the CAS as we don't expect this to
                // be invoked very concurrently and also we use SECONDS anyway. If this ever becomes a problem
                // we can reconsider.
                ScheduledFuture oldFuture = Volatile.Read(ref expirationFuture);
                if (oldFuture == null || oldFuture.GetDelay() > ttl)
                {
                    ScheduledFuture newFuture = ScheduledFuture.Schedule(loop, Run, ttl);
                    // It is possible that
                    // 1. task will fire in between this line, or
                    // 2. multiple timers may be set if there is concurrency
                    // (1) Shouldn't be a problem because we will fail the CAS and then the next loop will see CANCELLED
                    //     so the ttl will not be less, and we will bail out of the loop.
                    // (2) This is a trade-off to avoid concurrency resulting in contention on a synchronized block.
                    if (ReferenceEquals(Interlocked.CompareExchange(ref expirationFuture, newFuture, oldFuture), oldFuture))
                    {
                        if (oldFuture != null)
                        {
                            oldFuture.Cancel(true);
                        }
                        break;
                    }
                    else
                    {
                        // There was something else scheduled in the meantime... Cancel and try again.
                        newFuture.Cancel(true);
                    }
                }
                else
                {
                    break;
                }
            }
        }

        internal bool ClearAndCancel()
        {
            IReadOnlyList<E> entries = GetAndSet(Array.Empty<E>());
            if (entries.Count == 0)
            {
                return false;
            }

            ScheduledFuture expirationFuture = Interlocked.Exchange(ref this.expirationFuture, CANCELLED);
            if (expirationFuture != null)
            {
                expirationFuture.Cancel(false);
            }

            return true;
        }

        internal void Run()
        {
            // We always remove all entries for a hostname once one entry expire. This is not the
            // most efficient to do but this way we can guarantee that if a DnsResolver
            // be configured to prefer one ip family over the other we will not return unexpected
            // results to the enduser if one of the A or AAAA records has different TTL settings.
            //
            // As a TTL is just a hint of the maximum time a cache is allowed to cache stuff it's
            // completely fine to remove the entry even if the TTL is not reached yet.
            //
            // See https://github.com/netty/netty/issues/7329
            cache.resolveCache.TryRemove(new KeyValuePair<string, Entries>(hostname, this));

            ClearAndCancel();
        }

        // CLR: Collections.singletonList(e), an immutable one-element list.
        private static IReadOnlyList<E> SingletonList(E e)
        {
            return Array.AsReadOnly(new[] { e });
        }
    }

    // CLR: java.util.concurrent.ScheduledFuture has no CLR counterpart. loop.schedule(task, ttl, SECONDS) is
    // loop.ScheduleAsync(Action, TimeSpan, CancellationToken) and cancel(...) is CancellationTokenSource.Cancel(), as
    // handler's SslHandler and AbstractSniHandler map ScheduledFuture; getDelay(TimeUnit.SECONDS), "the remaining delay
    // associated with this object" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/Delayed.html),
    // needs the deadline that the JDK ScheduledFutureTask keeps, so this private type keeps the deadline read from the
    // loop's Ticker with the token source. It holds only the two members Cache uses (cancel and getDelay).
    private class ScheduledFuture
    {
        private readonly CancellationTokenSource cancellation;
        private readonly Ticker ticker;
        private readonly long deadlineNanos;

        protected ScheduledFuture()
        {
        }

        private ScheduledFuture(CancellationTokenSource cancellation, Ticker ticker, long deadlineNanos)
        {
            this.cancellation = cancellation;
            this.ticker = ticker;
            this.deadlineNanos = deadlineNanos;
        }

        // CLR: loop.schedule(this, ttl, TimeUnit.SECONDS). The executor faults the returned Task when it rejects the
        // work (Common's AbstractScheduledEventExecutor.ScheduleNative), where the JDK schedule(...) throws
        // RejectedExecutionException, so a Task that is already faulted rethrows its exception here.
        internal static ScheduledFuture Schedule(IEventLoop loop, Action task, int ttl)
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            Ticker ticker = loop.Ticker();
            long deadlineNanos = ticker.NanoTime() + ttl * 1_000_000_000L;
            Task scheduled = loop.ScheduleAsync(task, TimeSpan.FromSeconds(ttl), cancellation.Token);
            if (scheduled.IsFaulted)
            {
                scheduled.GetAwaiter().GetResult();
            }
            return new ScheduledFuture(cancellation, ticker, deadlineNanos);
        }

        internal virtual bool Cancel(bool mayInterruptIfRunning)
        {
            if (cancellation.IsCancellationRequested)
            {
                return false;
            }
            cancellation.Cancel();
            return true;
        }

        // CLR: getDelay(TimeUnit.SECONDS): the remaining delay in whole seconds (TimeUnit.convert truncates), never
        // negative, as the delayNanos of the executor's scheduled tasks.
        internal virtual long GetDelay()
        {
            return Math.Max(0, deadlineNanos - ticker.NanoTime()) / 1_000_000_000L;
        }
    }

    // CLR: the anonymous ScheduledFuture of CANCELLED. Its compareTo, isCancelled, isDone and get overloads belong to the
    // JDK Future/Delayed contracts that no CLR caller reads; only cancel and getDelay are kept.
    private sealed class CancelledScheduledFuture : ScheduledFuture
    {
        internal override bool Cancel(bool mayInterruptIfRunning)
        {
            return false;
        }

        internal override long GetDelay()
        {
            // We ignore unit and always return the minimum value to ensure the TTL of the cancelled marker is
            // the smallest.
            return long.MinValue;
        }
    }
}
