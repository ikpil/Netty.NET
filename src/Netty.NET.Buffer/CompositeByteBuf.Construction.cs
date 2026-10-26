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
using System.Collections.Generic;
using Netty.NET.Common;

namespace Netty.NET.Buffer;

public partial class CompositeByteBuf
{
    /// <summary>Creates a composite adopting the supplied buffer references and their captured readable ranges.</summary>
    /// <remarks>Array preflight failures leave inputs caller-owned. Once insertion begins,
    /// failure releases acquired components and the unvisited tail, following AddComponents.
    /// Null terminates insertion. Consolidation runs once after the batch; all bytes are readable.</remarks>
    public CompositeByteBuf(IByteBufAllocator allocator, bool direct, int maxNumComponents, params ByteBuf[] buffers)
        : this(allocator, direct, maxNumComponents, buffers, 0) { }

    // Original package-private array constructor. Only the suffix participates in
    // capacity preflight and ownership transfer; preceding entries remain untouched.
    internal CompositeByteBuf(IByteBufAllocator allocator, bool direct, int maxNumComponents,
        ByteBuf[] buffers, int offset)
        : this(allocator, direct, maxNumComponents)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(buffers);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, buffers.Length);
            AddComponentsCore(0, buffers, offset, false);
            SetIndex(0, Capacity);
        }
        catch
        {
            ReleaseFailedConstruction();
            throw;
        }
    }

    /// <summary>Creates a composite adopting references from an enumerable without materializing it.</summary>
    /// <remarks>Streaming ownership, null termination and enumerator disposal follow AddComponents.
    /// Failure releases acquired components; buffers not yielded by a failing enumerator remain caller-owned.
    /// A composite passed as IEnumerable is adopted as one buffer rather than enumerating its components.</remarks>
    public CompositeByteBuf(IByteBufAllocator allocator, bool direct, int maxNumComponents, IEnumerable<ByteBuf> buffers)
        : this(allocator, direct, maxNumComponents)
    {
        try
        {
            AddComponentsCore(0, buffers, false);
            SetIndex(0, Capacity);
        }
        catch
        {
            ReleaseFailedConstruction();
            throw;
        }
    }

    private void ReleaseFailedConstruction()
    {
        // CLR: a failed constructor cannot return its partial composite for caller cleanup.
        // Release only references already acquired; do not invoke a derived Deallocate
        // callback before the derived constructor has initialized its state.
        SetReferenceCount(0);
        for (int i = 0; i < _components.Count; ++i) ReferenceCountUtil.SafeRelease(_components[i].Source);
        _components.Clear();
    }
}
