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
using System.Threading;

namespace Netty.NET.Common;

/// <summary>A bounded, borrowed view of externally owned native memory.</summary>
/// <remarks>The caller must keep the external allocation valid throughout every
/// span and pin use. Disposing this descriptor invalidates new access and never
/// frees the external address. Pinning this view does not extend external ownership.</remarks>
public sealed unsafe class NativeMemoryView : IDisposable
{
    private readonly ViewManager _manager;
    public NativeMemoryView(nint address, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (address == 0 && length != 0)
            throw new ArgumentException("Nonempty native memory requires a nonzero address.", nameof(address));
        Address = address;
        Length = length;
        _manager = new ViewManager(this);
    }

    public nint Address { get; }
    public int Length { get; }
    public Memory<byte> Memory => _manager.Memory;
    public void Dispose() => ((IDisposable)_manager).Dispose();

    private sealed class ViewManager : MemoryManager<byte>
    {
        private readonly NativeMemoryView _view;
        private int _disposed;
        internal ViewManager(NativeMemoryView view) => _view = view;
        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, _view);
        public override Memory<byte> Memory
        {
            get { ThrowIfDisposed(); return CreateMemory(_view.Length); }
        }
        public override Span<byte> GetSpan()
        {
            ThrowIfDisposed();
            return new Span<byte>((void*)_view.Address, _view.Length);
        }
        public override MemoryHandle Pin(int elementIndex = 0)
        {
            ThrowIfDisposed();
            if ((uint)elementIndex > (uint)_view.Length)
                throw new ArgumentOutOfRangeException(nameof(elementIndex));
            // Borrowing provides no owner retention. Keep the descriptor alive;
            // the external owner remains the caller's responsibility.
            return new MemoryHandle((byte*)_view.Address + elementIndex, pinnable: new ViewPin(_view));
        }
        public override void Unpin() => throw new NotSupportedException("Dispose the returned MemoryHandle.");
        protected override void Dispose(bool disposing) => Interlocked.Exchange(ref _disposed, 1);
    }

    private sealed class ViewPin : IPinnable
    {
        private NativeMemoryView _view;
        internal ViewPin(NativeMemoryView view) => _view = view;
        public MemoryHandle Pin(int elementIndex) => throw new NotSupportedException("A pin lease is single-use.");
        public void Unpin() => Interlocked.Exchange(ref _view, null);
    }
}
