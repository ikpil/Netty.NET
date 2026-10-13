/*
 * Copyright 2016 The Netty Project
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
using System.Runtime.InteropServices;
using System.Threading;

namespace Netty.NET.Buffer;

// CLR: a bounded MemoryManager replaces the JVM direct-ByteBuffer facade. The
// handle owns only NativeMemory.Alloc-compatible pointers when doFree is requested.
// Borrowed pointers require the caller to preserve the external allocation's lifetime.
internal sealed unsafe class NativeAddressMemoryManager : MemoryManager<byte>
{
    private readonly object _sync = new();
    private readonly AddressHandle _handle;
    private bool _disposed;
    internal int Length { get; }

    internal NativeAddressMemoryManager(nint address, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (address == 0 && length != 0)
            throw new ArgumentException("A nonempty range needs a nonzero native address.", nameof(address));
        if ((nuint)length > nuint.MaxValue - unchecked((nuint)address))
            throw new ArgumentOutOfRangeException(nameof(length), "The native address range wraps around.");
        Length = length;
        _handle = new AddressHandle(address);
    }

    // Construction failures leave the external pointer owned by the caller.
    // No throwing work follows ownership transfer in the public factory.
    internal void TakeOwnership(bool doFree) => _handle.DoFree = doFree;
    internal bool IsHandleReleased => Volatile.Read(ref _handle.Released);

    public override Span<byte> GetSpan()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new Span<byte>((void*)_handle.DangerousGetHandle(), Length);
        }
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (elementIndex < 0 || elementIndex > Length) throw new ArgumentOutOfRangeException(nameof(elementIndex));
            var lease = new PinLease();
            bool acquired = false;
            try
            {
                _handle.DangerousAddRef(ref acquired);
                lease.Activate(_handle);
                return new MemoryHandle((byte*)_handle.DangerousGetHandle() + elementIndex, pinnable: lease);
            }
            catch
            {
                if (!lease.Release() && acquired) _handle.DangerousRelease();
                throw;
            }
        }
    }

    // MemoryHandle structs can be copied. Every pin has an idempotent lease.
    public override void Unpin() => throw new NotSupportedException("Dispose the returned MemoryHandle.");
    protected override void Dispose(bool disposing)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _handle.Dispose();
        }
    }

    private sealed class AddressHandle : SafeHandle
    {
        internal bool DoFree;
        internal bool Released;
        internal AddressHandle(nint address) : base(0, ownsHandle: true) => SetHandle(address);
        public override bool IsInvalid => handle == 0;
        protected override bool ReleaseHandle()
        {
            if (DoFree) NativeMemory.Free((void*)handle);
            Volatile.Write(ref Released, true);
            return true;
        }
    }

    private sealed class PinLease : IPinnable
    {
        private AddressHandle _handle;
        internal void Activate(AddressHandle handle) => _handle = handle;
        public MemoryHandle Pin(int elementIndex) => throw new NotSupportedException("A pin lease is single-use.");
        public void Unpin() => Release();
        internal bool Release()
        {
            AddressHandle handle = Interlocked.Exchange(ref _handle, null);
            handle?.DangerousRelease();
            GC.SuppressFinalize(this);
            return handle != null;
        }
        ~PinLease() => Unpin();
    }
}
