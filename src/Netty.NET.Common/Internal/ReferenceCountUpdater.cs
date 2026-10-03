/*
 * Copyright 2019 The Netty Project
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

using System.Threading;

namespace Netty.NET.Common.Internal;

/// <summary>Atomic reference-count operations on a caller-owned CLR integer field.</summary>
/// <remarks>Initialize the field to one and pass the same field by reference on
/// every operation. Zero is terminal until an explicit quiescent reset. A live
/// count supports the full positive Int32 range. No reflected field offsets,
/// provider selection or separately allocated counter object are required.
/// A successful final release transfers deallocation responsibility to its caller;
/// this counter neither owns storage nor keeps an unretained borrower alive.</remarks>
public static class ReferenceCountUpdater
{
    public static int GetCount(ref int count) => Volatile.Read(ref count);

    /// <summary>A best-effort accessibility check, which does not acquire ownership.</summary>
    public static bool IsLive(ref int count) => GetCount(ref count) > 0;

    /// <summary>Sets the count directly; nonpositive values represent released storage.</summary>
    /// <remarks>Only use while no other thread accesses this counter. This operation
    /// does not invoke deallocation and may deliberately reinitialize a released count.</remarks>
    public static void SetCount(ref int count, int value) => Volatile.Write(ref count, value > 0 ? value : 0);

    /// <summary>Resets the count to one while all prior users are quiescent.</summary>
    public static void Reset(ref int count) => SetCount(ref count, 1);

    public static void Retain(ref int count, int increment = 1)
    {
        ObjectUtil.checkPositive(increment, nameof(increment));
        while (true)
        {
            int current = Volatile.Read(ref count);
            if (current <= 0 || current > int.MaxValue - increment)
                throw new IllegalReferenceCountException(current, increment);
            if (Interlocked.CompareExchange(ref count, current + increment, current) == current)
                return;
        }
    }

    /// <summary>Returns true exactly when this operation transitions a live count to zero.</summary>
    public static bool Release(ref int count, int decrement = 1)
    {
        ObjectUtil.checkPositive(decrement, nameof(decrement));
        while (true)
        {
            int current = Volatile.Read(ref count);
            if (current < decrement)
                throw new IllegalReferenceCountException(current, -decrement);
            if (Interlocked.CompareExchange(ref count, current - decrement, current) == current)
                return current == decrement;
        }
    }
}
