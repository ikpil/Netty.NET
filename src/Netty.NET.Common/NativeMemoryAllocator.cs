/*
 * Copyright 2025 The Netty Project
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
using System.Runtime.InteropServices;
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

/// <summary>Allocates owned native byte memory with an explicit reservation limit.</summary>
/// <remarks>The limit and count include the one-byte allocation backing an empty owner.
/// They exclude native allocator metadata. No limit is inferred from the managed heap.</remarks>
public sealed unsafe class NativeMemoryAllocator
{
    private long _reservedBytes;

    /// <summary>The shared Netty native reservation domain.</summary>
    /// <remarks>A positive io.netty.maxDirectMemory environment setting supplies
    /// its byte limit at first use. Other values use no inferred managed-heap limit.
    /// Construct an allocator explicitly for an independent reservation domain.</remarks>
    public static NativeMemoryAllocator Shared => SharedAllocator.Instance;

    private static class SharedAllocator
    {
        internal static readonly NativeMemoryAllocator Instance = Create();
        private static NativeMemoryAllocator Create()
        {
            long limit = SystemPropertyUtil.getLong("io.netty.maxDirectMemory", -1);
            return new NativeMemoryAllocator(limit > 0 ? limit : long.MaxValue);
        }
    }

    public NativeMemoryAllocator(long maximumBytes = long.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        MaximumBytes = maximumBytes;
    }

    public long MaximumBytes { get; }
    public long ReservedBytes => Volatile.Read(ref _reservedBytes);

    /// <summary>Allocates memory with unspecified initial contents unless clear is true.</summary>
    public NativeMemoryOwner Allocate(int length, bool clear = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        long bytes = Math.Max(1, length);
        var handle = new NativeAllocationHandle(this, bytes);
        var owner = new NativeMemoryOwner(this, handle, length);
        Reserve(bytes);
        try
        {
            void* pointer = clear ? NativeMemory.AllocZeroed((nuint)bytes) : NativeMemory.Alloc((nuint)bytes);
            if (pointer == null) throw new OutOfMemoryException();
            handle.Initialize(pointer);
            GC.AddMemoryPressure(bytes);
            handle.HasMemoryPressure = true;
            return owner;
        }
        catch
        {
            if (handle.IsInvalid) Release(bytes);
            else handle.Dispose();
            throw;
        }
    }

    /// <summary>Transfers an owner's allocation to a replacement of the requested length.</summary>
    /// <remarks>The retained prefix is unchanged. New bytes are unspecified. On failure,
    /// the original owner remains usable. On success, its memory views are invalidated.
    /// All unpinned users must have ceased, and any outstanding pin rejects reallocation.</remarks>
    public NativeMemoryOwner Reallocate(NativeMemoryOwner owner, int length)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (!ReferenceEquals(owner.Allocator, this))
            throw new ArgumentException("The owner belongs to another allocator.", nameof(owner));

        long bytes = Math.Max(1, length);
        var replacementHandle = new NativeAllocationHandle(this, bytes);
        var replacement = new NativeMemoryOwner(this, replacementHandle, length);
        lock (owner.Sync)
        {
            owner.ThrowIfDisposed();
            if (owner.PinCount != 0)
                throw new InvalidOperationException("Dispose all pins before reallocating native memory.");

            long delta = bytes - owner.Handle.Bytes;
            bool addedPressure = false;
            if (delta > 0) Reserve(delta);
            try
            {
                if (delta > 0)
                {
                    GC.AddMemoryPressure(delta);
                    addedPressure = true;
                }
                void* pointer = NativeMemory.Realloc((void*)owner.Handle.DangerousGetHandle(), (nuint)bytes);
                if (pointer == null) throw new OutOfMemoryException();
                replacementHandle.Initialize(pointer);
                replacementHandle.HasMemoryPressure = true;
                owner.TransferAllocation();
            }
            catch
            {
                if (addedPressure) GC.RemoveMemoryPressure(delta);
                if (delta > 0) Release(delta);
                throw;
            }
            if (delta < 0)
            {
                GC.RemoveMemoryPressure(-delta);
                Release(-delta);
            }
            return replacement;
        }
    }

    private void Reserve(long bytes)
    {
        while (true)
        {
            long current = Volatile.Read(ref _reservedBytes);
            if (bytes > MaximumBytes - current)
                throw new OutOfMemoryException($"Native memory reservation exceeds {MaximumBytes} bytes.");
            if (Interlocked.CompareExchange(ref _reservedBytes, current + bytes, current) == current) return;
        }
    }

    internal void Release(long bytes) => Interlocked.Add(ref _reservedBytes, -bytes);

    internal sealed class NativeAllocationHandle : SafeHandle
    {
        private readonly NativeMemoryAllocator _allocator;
        internal readonly long Bytes;
        internal bool HasMemoryPressure;

        internal NativeAllocationHandle(NativeMemoryAllocator allocator, long bytes) : base(0, ownsHandle: true)
        {
            _allocator = allocator;
            Bytes = bytes;
        }

        public override bool IsInvalid => handle == 0;
        internal void Initialize(void* pointer) => SetHandle((nint)pointer);

        protected override bool ReleaseHandle()
        {
            NativeMemory.Free((void*)handle);
            if (HasMemoryPressure) GC.RemoveMemoryPressure(Bytes);
            _allocator.Release(Bytes);
            return true;
        }
    }
}
