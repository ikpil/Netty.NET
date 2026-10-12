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
using System.Diagnostics;
using Netty.NET.Common;

namespace Netty.NET.Buffer;

internal class SimpleLeakAwareCompositeByteBuf : WrappedCompositeByteBuf
{
    protected readonly IResourceLeakTracker<ByteBuf> Leak;
    internal SimpleLeakAwareCompositeByteBuf(CompositeByteBuf wrapped, IResourceLeakTracker<ByteBuf> leak)
        : base(wrapped)
    {
        ArgumentNullException.ThrowIfNull(leak);
        Leak = leak;
    }

    public override bool Release(int decrement = 1)
    {
        // Call unwrap() before just in case that super.release() will change the ByteBuf instance that is returned
        // by unwrap().
        ByteBuf unwrapped = Unwrap();
        if (!base.Release(decrement)) return false;
        CloseLeak(unwrapped);
        return true;
    }

    private void CloseLeak(ByteBuf trackedByteBuf)
    {
        // Close the ResourceLeakTracker with the tracked ByteBuf as argument. This must be the same that was used when
        // calling DefaultResourceLeak.track(...).
        bool closed = Leak.Close(trackedByteBuf);
        Debug.Assert(closed);
    }

    public override ByteBuf Slice() => NewLeakAwareByteBuf(base.Slice());
    public override ByteBuf Slice(int index, int length) => NewLeakAwareByteBuf(base.Slice(index, length));
    public override ByteBuf RetainedSlice() => NewLeakAwareByteBuf(base.RetainedSlice());
    public override ByteBuf RetainedSlice(int index, int length) => NewLeakAwareByteBuf(base.RetainedSlice(index, length));
    public override ByteBuf Duplicate() => NewLeakAwareByteBuf(base.Duplicate());
    public override ByteBuf RetainedDuplicate() => NewLeakAwareByteBuf(base.RetainedDuplicate());
    public override ByteBuf ReadSlice(int length) => NewLeakAwareByteBuf(base.ReadSlice(length));
    public override ByteBuf ReadRetainedSlice(int length) => NewLeakAwareByteBuf(base.ReadRetainedSlice(length));
    public override ByteBuf AsReadOnly() => NewLeakAwareByteBuf(base.AsReadOnly());

    private SimpleLeakAwareByteBuf NewLeakAwareByteBuf(ByteBuf wrapped) => NewLeakAwareByteBuf(wrapped, Unwrap(), Leak);
    protected virtual SimpleLeakAwareByteBuf NewLeakAwareByteBuf(ByteBuf wrapped, ByteBuf trackedByteBuf,
        IResourceLeakTracker<ByteBuf> leakTracker) => new(wrapped, trackedByteBuf, leakTracker);
}
