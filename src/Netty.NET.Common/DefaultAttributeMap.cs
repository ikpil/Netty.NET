/*
 * Copyright 2012 The Netty Project
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
using System.Threading;

namespace Netty.NET.Common;

/// <summary>Stores shared, atomically updated reference values under typed attribute keys.</summary>
/// <remarks>
/// ConcurrentDictionary publishes one slot per key. Clearing a value retains its slot;
/// removing a slot detaches it, so existing holders and later lookups become independent.
/// The original sorted copy-on-write implementation and its comments are recorded in
/// docs/common-clr-design.md under Native attribute map and atomic slots.
/// </remarks>
public class DefaultAttributeMap : IAttributeMap
{
    private readonly ConcurrentDictionary<IAttributeKey, object> attributes = new(ReferenceEqualityComparer.Instance);

    public IAttribute<T> Attr<T>(AttributeKey<T> key) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        DefaultAttribute<T> replacement = null;
        bool interrupted = false;
        try
        {
            for (;;)
            {
                try
                {
                    // Factories can run more than once; creating an unpublished empty slot
                    // has no user callback or side effect. Always inspect the published result.
                    var attribute = (DefaultAttribute<T>)attributes.GetOrAdd(key,
                        static (key, map) => new DefaultAttribute<T>(map, (AttributeKey<T>)key), this);
                    if (!attribute.IsRemoved) return attribute;
                    replacement ??= new DefaultAttribute<T>(this, key);
                    if (attributes.TryUpdate(key, replacement, attribute)) return replacement;
                }
                catch (ThreadInterruptedException) { interrupted = true; }
            }
        }
        finally
        {
            // Native dictionary writes can wait on monitors. Retry from the
            // published state, then retain the interrupt for a later blocking wait.
            if (interrupted) Thread.CurrentThread.Interrupt();
        }
    }

    public bool HasAttr<T>(AttributeKey<T> key) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        return attributes.ContainsKey(key);
    }

    private void RemoveAttributeIfMatch<T>(AttributeKey<T> key, DefaultAttribute<T> attribute) where T : class
    {
        // Conditional removal must match the old slot as well as the key: a
        // concurrent Attr may already have published its replacement.
        bool interrupted = false;
        try
        {
            for (;;)
            {
                try
                {
                    attributes.TryRemove(new KeyValuePair<IAttributeKey, object>(key, attribute));
                    return;
                }
                catch (ThreadInterruptedException) { interrupted = true; }
            }
        }
        finally
        {
            // Detachment and value clearing already happened. Finish the
            // conditional deletion before restoring any consumed interrupt.
            if (interrupted) Thread.CurrentThread.Interrupt();
        }
    }

    private sealed class DefaultAttribute<T>(DefaultAttributeMap map, AttributeKey<T> key) : IAttribute<T> where T : class
    {
        private DefaultAttributeMap attributeMap = map;
        private T value;

        internal bool IsRemoved => Volatile.Read(ref attributeMap) == null;
        public AttributeKey<T> Key() => key;
        public T Get() => Volatile.Read(ref value);
        public void Set(T value) => Volatile.Write(ref this.value, value);
        public T GetAndSet(T value) => Interlocked.Exchange(ref this.value, value);
        public bool CompareAndSet(T oldValue, T newValue) =>
            ReferenceEquals(Interlocked.CompareExchange(ref value, newValue, oldValue), oldValue);

        public T SetIfAbsent(T value)
        {
            return Interlocked.CompareExchange(ref this.value, value, null);
        }

        public T GetAndRemove()
        {
            DefaultAttributeMap owner = Interlocked.Exchange(ref attributeMap, null);
            T oldValue = GetAndSet(null);
            owner?.RemoveAttributeIfMatch(key, this);
            return oldValue;
        }

        public void Remove()
        {
            DefaultAttributeMap owner = Interlocked.Exchange(ref attributeMap, null);
            Set(null);
            owner?.RemoveAttributeIfMatch(key, this);
        }
    }
}
