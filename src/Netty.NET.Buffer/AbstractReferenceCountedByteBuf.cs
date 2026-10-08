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

using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

/**
 * Abstract base class for {@link ByteBuf} implementations that count references.
 */
public abstract class AbstractReferenceCountedByteBuf : ByteBuf
{
    // this is setting the ref cnt to the initial value
    private int _referenceCount = 1;
    protected AbstractReferenceCountedByteBuf(int maxCapacity) : base(maxCapacity) { }
    // Try to do non-volatile read for performance as the ensureAccessible() is racy anyway and only provide
    // a best-effort guard.
    // CLR: retain the existing common counter's volatile observational read.
    // It is still an accessibility guard, not an ownership acquisition.
    public sealed override int ReferenceCount => ReferenceCountUpdater.GetCount(ref _referenceCount);
/**
     * An unsafe operation intended for use by a subclass that sets the reference count of the buffer directly
     */
    protected void SetReferenceCount(int count) => ReferenceCountUpdater.SetCount(ref _referenceCount, count);
/**
     * An unsafe operation intended for use by a subclass that resets the reference count of the buffer to 1
     */
    protected void ResetReferenceCount() => ReferenceCountUpdater.Reset(ref _referenceCount);
    public sealed override ByteBuf Retain(int increment = 1)
    {
        ReferenceCountUpdater.Retain(ref _referenceCount, increment);
        return this;
    }
    public sealed override bool Release(int decrement = 1)
    {
        bool final = ReferenceCountUpdater.Release(ref _referenceCount, decrement);
        if (final) Deallocate();
        return final;
    }
/**
     * Called once {@link #refCnt()} is equals 0.
     */
    protected abstract void Deallocate();
}
