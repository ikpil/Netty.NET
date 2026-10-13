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

// CLR: shared views share their tracker; independently counted pooled retained views
// get forced tracking and release their parent through the original leak facade.
internal class SimpleLeakAwareByteBuf : WrappedByteBuf
{
    /**
     * This object's is associated with the {@link ResourceLeakTracker}. When {@link ResourceLeakTracker#close(Object)}
     * is called this object will be used as the argument. It is also assumed that this object is used when
     * {@link ResourceLeakDetector#track(Object)} is called to create {@link #leak}.
     */
    private readonly ByteBuf _trackedByteBuf;
    protected readonly IResourceLeakTracker<ByteBuf> _leak;

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
        try { return UnwrappedDerived(base.RetainedSlice()); }
        catch (IllegalReferenceCountException failure) { AddCloseTrace(failure); throw; }
    }

    public override ByteBuf RetainedSlice(int index, int length)
    {
        try { return UnwrappedDerived(base.RetainedSlice(index, length)); }
        catch (IllegalReferenceCountException failure) { AddCloseTrace(failure); throw; }
    }

    public override ByteBuf RetainedDuplicate()
    {
        try { return UnwrappedDerived(base.RetainedDuplicate()); }
        catch (IllegalReferenceCountException failure) { AddCloseTrace(failure); throw; }
    }

    public override ByteBuf ReadRetainedSlice(int length)
    {
        try { return UnwrappedDerived(base.ReadRetainedSlice(length)); }
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

    private ByteBuf UnwrappedDerived(ByteBuf derived)
    {
        // We only need to unwrap SwappedByteBuf implementations as these will be the only ones that may end up in
        // the AbstractLeakAwareByteBuf implementations beside slices / duplicates and "real" buffers.
        // CLR has explicit endian accessors, so no swapped facade needs unwrapping.
        if (derived is not PooledDerivedByteBuf { IsIndependent: true } pooled)
            return NewSharedLeakAwareByteBuf(derived);

        // Update the parent to point to this buffer so we correctly close the ResourceLeakTracker.
        pooled.SetParent(this);
        IResourceLeakTracker<ByteBuf> tracker = null;
        try
        {
            // force tracking of derived buffers (see issue #13414)
            tracker = ByteBuf.LeakDetector.TrackForcibly(derived);
            return NewLeakAwareByteBuf(derived, derived, tracker);
        }
        catch
        {
            // CLR virtual wrapper factories may throw; return the acquired parent reference.
            try { derived.Release(); }
            finally { tracker?.Close(derived); }
            throw;
        }
    }

    protected virtual SimpleLeakAwareByteBuf NewLeakAwareByteBuf(ByteBuf buffer, ByteBuf trackedByteBuf,
        IResourceLeakTracker<ByteBuf> leakTracker) => new(buffer, trackedByteBuf, leakTracker);
}
