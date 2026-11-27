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
using System.Globalization;
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

/// <summary>Allocates managed byte storage with an explicit reservation limit.</summary>
/// <remarks>Reservations count active owners and storage retained by Netty.NET operation leases.
/// An empty owner counts as one byte. The GC controls the lifetime of retired arrays.</remarks>
public sealed class NativeMemoryAllocator
{
    private long _reservedBytes;

    /// <summary>The shared reservation domain.</summary>
    /// <remarks>A positive io.netty.maxDirectMemory environment setting supplies its byte limit at first use.</remarks>
    public static NativeMemoryAllocator Shared => SharedAllocator.Instance;

    private static class SharedAllocator
    {
        internal static readonly NativeMemoryAllocator Instance = Create();
        private static NativeMemoryAllocator Create()
        {
            long limit = SystemPropertyUtil.GetLong("io.netty.maxDirectMemory", -1);
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
        Reserve(bytes);
        try
        {
            var owner = new NativeMemoryOwner(this, length, clear);
            owner.TakeReservation();
            return owner;
        }
        catch
        {
            Release(bytes);
            throw;
        }
    }

    /// <summary>Transfers an owner's reservation to a replacement of the requested length.</summary>
    /// <remarks>The retained prefix is copied. On failure the original owner remains usable.
    /// On success its views are invalidated, and existing pins still reference the old array.</remarks>
    public NativeMemoryOwner Reallocate(NativeMemoryOwner owner, int length)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (!ReferenceEquals(owner.Allocator, this))
            throw new ArgumentException("The owner belongs to another allocator.", nameof(owner));

        lock (owner.Sync)
        {
            owner.EnsureReallocatable();
            Span<byte> previous = owner.GetSpan();
            long delta = (long)Math.Max(1, length) - Math.Max(1, owner.Length);
            if (delta > 0) Reserve(delta);
            NativeMemoryOwner replacement;
            try
            {
                replacement = new NativeMemoryOwner(this, length, clear: false);
                previous[..Math.Min(length, previous.Length)].CopyTo(replacement.GetSpan());
            }
            catch
            {
                if (delta > 0) Release(delta);
                throw;
            }
            owner.TransferReservation();
            replacement.TakeReservation();
            if (delta < 0) Release(-delta);
            return replacement;
        }
    }

    private void Reserve(long bytes)
    {
        while (true)
        {
            long current = Volatile.Read(ref _reservedBytes);
            if (bytes > MaximumBytes - current)
            {
                // CLR: the CAS loop rejects excess capacity without publishing an over-limit count.
                throw new OutOfDirectMemoryError(string.Create(CultureInfo.InvariantCulture,
                    $"failed to allocate {bytes} byte(s) of direct memory (used: {current}, max: {MaximumBytes})"));
            }
            if (Interlocked.CompareExchange(ref _reservedBytes, current + bytes, current) == current) return;
        }
    }

    internal void Release(long bytes) => Interlocked.Add(ref _reservedBytes, -bytes);
}
