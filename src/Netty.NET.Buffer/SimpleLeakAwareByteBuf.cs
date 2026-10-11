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
using System.Diagnostics;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

// CLR: this stage supports the shared reference-count hierarchy. Pooled independent
// derived counts, byte-order wrappers and allocator-wide leak wrapping remain pending.
internal class SimpleLeakAwareByteBuf : WrappedByteBuf
{
    /**
     * This object's is associated with the {@link ResourceLeakTracker}. When {@link ResourceLeakTracker#close(Object)}
     * is called this object will be used as the argument. It is also assumed that this object is used when
     * {@link ResourceLeakDetector#track(Object)} is called to create {@link #leak}.
     */
    private readonly ByteBuf _trackedByteBuf;
    private readonly IResourceLeakTracker<ByteBuf> _leak;

    internal SimpleLeakAwareByteBuf(ByteBuf wrapped, IResourceLeakTracker<ByteBuf> leak)
        : this(wrapped, wrapped, leak) { }

    internal SimpleLeakAwareByteBuf(ByteBuf wrapped, ByteBuf trackedByteBuf, IResourceLeakTracker<ByteBuf> leak)
        : base(wrapped)
    {
        ArgumentNullException.ThrowIfNull(trackedByteBuf);
        ArgumentNullException.ThrowIfNull(leak);
        _trackedByteBuf = trackedByteBuf;
        _leak = leak;
    }

    public override ByteBuf Slice() => NewSharedLeakAwareByteBuf(base.Slice());
    public override ByteBuf Slice(int index, int length) => NewSharedLeakAwareByteBuf(base.Slice(index, length));
    public override ByteBuf Duplicate() => NewSharedLeakAwareByteBuf(base.Duplicate());
    public override ByteBuf ReadSlice(int length) => NewSharedLeakAwareByteBuf(base.ReadSlice(length));
    public override ByteBuf AsReadOnly() => NewSharedLeakAwareByteBuf(base.AsReadOnly());

    public override ByteBuf RetainedSlice()
    {
        try { return NewSharedLeakAwareByteBuf(base.RetainedSlice()); }
        catch (IllegalReferenceCountException failure) { AddCloseTrace(failure); throw; }
    }

    public override ByteBuf RetainedSlice(int index, int length)
    {
        try { return NewSharedLeakAwareByteBuf(base.RetainedSlice(index, length)); }
        catch (IllegalReferenceCountException failure) { AddCloseTrace(failure); throw; }
    }

    public override ByteBuf RetainedDuplicate()
    {
        try { return NewSharedLeakAwareByteBuf(base.RetainedDuplicate()); }
        catch (IllegalReferenceCountException failure) { AddCloseTrace(failure); throw; }
    }

    public override ByteBuf ReadRetainedSlice(int length)
    {
        try { return NewSharedLeakAwareByteBuf(base.ReadRetainedSlice(length)); }
        catch (IllegalReferenceCountException failure) { AddCloseTrace(failure); throw; }
    }

    public override ByteBuf Retain(int increment = 1)
    {
        try { base.Retain(increment); return this; }
        catch (IllegalReferenceCountException failure) { AddCloseTrace(failure); throw; }
    }

    public override bool Release(int decrement = 1)
    {
        try
        {
            if (!base.Release(decrement)) return false;
            CloseLeak();
            return true;
        }
        catch (IllegalReferenceCountException failure) { AddCloseTrace(failure); throw; }
    }

    public override ByteBuf Touch(object hint = null) => this;

    private void CloseLeak()
    {
        // Close the ResourceLeakTracker with the tracked ByteBuf as argument. This must be the same that was used when
        // calling DefaultResourceLeak.track(...).
        bool closed = _leak.Close(_trackedByteBuf);
        Debug.Assert(closed);
    }

    private void AddCloseTrace(IllegalReferenceCountException failure)
        => ThrowableUtil.AddSuppressed(failure, _leak.GetCloseStackTraceIfAny());

    private SimpleLeakAwareByteBuf NewSharedLeakAwareByteBuf(ByteBuf wrapped)
        => NewLeakAwareByteBuf(wrapped, _trackedByteBuf, _leak);

    protected virtual SimpleLeakAwareByteBuf NewLeakAwareByteBuf(ByteBuf buffer, ByteBuf trackedByteBuf,
        IResourceLeakTracker<ByteBuf> leakTracker) => new(buffer, trackedByteBuf, leakTracker);
}
