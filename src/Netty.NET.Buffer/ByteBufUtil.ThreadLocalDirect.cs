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
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

public static partial class ByteBufUtil
{
    /**
     * Returns a cached thread-local direct buffer, if available.
     *
     * @return a cached thread-local direct buffer, if available.  {@code null} otherwise.
     */
    /// <remarks>Disabled by default; io.netty.threadLocalDirectBufferSize snapshots
    /// on the first call. Guaranteed physical fast-thread cleanup scopes cache one
    /// idle native owner at most. Other callers receive fresh uncached native storage.
    /// Wrappers and memory facades stay fresh; final release invalidates old borrows.
    /// Pins/operation leases delay reuse. Cross-thread returns and over-limit owners
    /// are freed. This raw transport helper does not add automatic leak wrapping.</remarks>
    public static ByteBuf ThreadLocalDirectBuffer()
    {
        if (ThreadLocalDirectSettings.Size <= 0) return null;
        return FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals()
            ? ThreadLocalDirectCaches.Local.Get().Rent()
            : ThreadLocalDirectByteBuf.Create(null);
    }

    private static class ThreadLocalDirectSettings
    {
        internal static readonly int Size = SystemPropertyUtil.GetInt("io.netty.threadLocalDirectBufferSize", 0);
        static ThreadLocalDirectSettings() { }
    }

    private static class ThreadLocalDirectCaches
    {
        internal static readonly LocalCache Local = new();
        static ThreadLocalDirectCaches() { }
    }

    private sealed class LocalCache : FastThreadLocal<ThreadLocalDirectBufferCache>
    {
        protected override ThreadLocalDirectBufferCache InitialValue() => new(ThreadLocalDirectSettings.Size);
        protected override void OnRemoval(ThreadLocalDirectBufferCache cache) => cache.Dispose();
    }
}
