/*
 * Copyright 2026 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Globalization;
using System.Threading;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Utility methods for creating {@link Http2Connection.PropertyKey}s.
 */
// CLR: the final package-private class with a private constructor is an internal static class; AtomicInteger is an int
// updated with Interlocked (docs/codec-http-porting.md, Shared CLR adaptations: "AtomicInteger ... int with
// Interlocked"), getAndIncrement() is Interlocked.Increment - 1, decrementAndGet() Interlocked.Decrement and get()
// Volatile.Read. The package-private members are internal; the count in the message uses the invariant culture.
internal static class Http2ConnectionPropertyKeys
{

    /**
     * Safe-guard against misuse: global keys are expected to be created rarely, typically once per use-case and
     * stored in a {@code static} field, so there should never be a legitimate need for more than this many.
     */
    private const int MAX_GLOBAL_KEYS = 8;

    private static int nextIndex;

    /**
     * Creates a new key that, unlike one returned by {@link Http2Connection#newKey()}, is not tied to a specific
     * {@link Http2Connection} instance. The returned key can be used with the streams of any {@link Http2Connection},
     * which makes it suitable for cases where a key needs to be shared, e.g. stored in a {@code static} field,
     * across multiple connections, or created before a {@link Http2Connection} instance is even available.
     * <p>
     * Accessing a property that is associated with a key returned by this method is slower than accessing one
     * associated with a key returned by {@link Http2Connection#newKey()}. Prefer the latter whenever a specific
     * {@link Http2Connection} instance is available up-front.
     */
    internal static IHttp2Connection.IPropertyKey NewGlobalKey()
    {
        int index = Interlocked.Increment(ref nextIndex) - 1;
        if (index >= MAX_GLOBAL_KEYS)
        {
            Interlocked.Decrement(ref nextIndex);
            throw new InvalidOperationException("Only " + MAX_GLOBAL_KEYS.ToString(CultureInfo.InvariantCulture) +
                " global keys can be created");
        }
        return new GlobalPropertyKey(index);
    }

    /**
     * The number of keys created via {@link #newGlobalKey()} so far, which also doubles as the exclusive upper
     * bound for {@link GlobalPropertyKey#index}.
     */
    internal static int KeyCount()
    {
        return Volatile.Read(ref nextIndex);
    }

    /**
     * Implementation of {@link Http2Connection.PropertyKey} that specifies the index position of the property and,
     * unlike {@link DefaultHttp2Connection.DefaultPropertyKey}, is not tied to a specific {@link Http2Connection}.
     */
    internal sealed class GlobalPropertyKey : IHttp2Connection.IPropertyKey
    {
        internal readonly int index;

        // CLR: Java's private constructor is internal: the enclosing class cannot reach a private member of a nested
        // class in C#.
        internal GlobalPropertyKey(int index)
        {
            this.index = index;
        }

        public override bool Equals(object o)
        {
            if (!(o is GlobalPropertyKey))
            {
                return false;
            }
            GlobalPropertyKey that = (GlobalPropertyKey) o;
            return index == that.index;
        }

        public override int GetHashCode()
        {
            return index;
        }
    }
}
