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
using System.Threading;
using Netty.NET.Common;

namespace Netty.NET.Buffer;

// CLR: bounded storage reuse replaces the Java recycler of ByteBuf objects.
// Physical-thread identity never flows through ExecutionContext; closing the gate
// drains idle storage and sends late returns directly to native release.
internal sealed class ThreadLocalDirectBufferCache : IDisposable
{
    private readonly object _sync = new();
    private readonly WeakReference<Thread> _thread = new(Thread.CurrentThread);
    private readonly int _limit;
    private NativeMemoryOwner _idle;
    private bool _closed;
    internal NativeMemoryAllocator NativeAllocator { get; }

    internal ThreadLocalDirectBufferCache(int limit, NativeMemoryAllocator nativeAllocator = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        _limit = limit;
        NativeAllocator = nativeAllocator ?? NativeMemoryAllocator.Shared;
    }

    internal ByteBuf Rent()
    {
        if (!_thread.TryGetTarget(out Thread thread) || !ReferenceEquals(thread, Thread.CurrentThread))
            throw new InvalidOperationException("A direct-buffer cache belongs to its physical thread.");
        NativeMemoryOwner owner;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            owner = _idle;
            _idle = null;
        }
        return ThreadLocalDirectByteBuf.Create(this, owner);
    }

    internal void Return(NativeMemoryOwner owner)
    {
        lock (_sync)
        {
            if (!_closed && _idle == null && owner.Length <= _limit &&
                _thread.TryGetTarget(out Thread thread) && ReferenceEquals(thread, Thread.CurrentThread))
            {
                _idle = owner;
                return;
            }
        }
        owner.Dispose();
    }

    public void Dispose()
    {
        NativeMemoryOwner owner;
        lock (_sync)
        {
            _closed = true;
            owner = _idle;
            _idle = null;
        }
        owner?.Dispose();
    }
}
