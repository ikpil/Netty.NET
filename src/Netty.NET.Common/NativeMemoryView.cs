/*
 * Copyright 2013 The Netty Project
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
using System.Buffers;

namespace Netty.NET.Common;

/// <summary>A bounded, borrowed view of existing byte memory.</summary>
/// <remarks>Disposal closes the view. The supplied memory remains caller-owned.</remarks>
public sealed class NativeMemoryView : IDisposable
{
    private readonly ViewManager _manager;

    public NativeMemoryView(Memory<byte> memory)
    {
        Length = memory.Length;
        _manager = new ViewManager(memory);
    }

    [Obsolete("Wrapping raw addresses is unsupported. Supply bounded Memory<byte>.")]
    public NativeMemoryView(nint address, int length)
    {
        throw new PlatformNotSupportedException("Raw address views are unavailable on the managed allocator.");
    }

    public int Length { get; }
    public Memory<byte> Memory => _manager.Memory;
    public void Dispose()
    {
        ((IDisposable)_manager).Dispose();
    }

    private sealed class ViewManager(Memory<byte> memory) : MemoryManager<byte>
    {
        private readonly object _sync = new();
        private Memory<byte> _memory = memory;
        private bool _disposed;

        public override Memory<byte> Memory
        {
            get
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    return CreateMemory(_memory.Length);
                }
            }
        }

        public override Span<byte> GetSpan()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                return _memory.Span;
            }
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                return _memory.Slice(elementIndex).Pin();
            }
        }

        public override void Unpin()
        {
            throw new NotSupportedException("Dispose the returned MemoryHandle.");
        }
        protected override void Dispose(bool disposing)
        {
            lock (_sync)
            {
                _disposed = true;
                _memory = default;
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
