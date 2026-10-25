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

namespace Netty.NET.Buffer;

/**
 * A {@link ByteBuf} implementation that wraps another buffer to prevent a user from increasing or decreasing the
 * wrapped buffer's reference count.
 */
/// <remarks>This borrowed facade does not retain its parent or make it immortal.
/// The caller must keep the underlying owner alive. Indices/marks and storage are
/// shared with the wrapped buffer. Copies have ordinary independent ownership.
/// Explicit BE/LE operations replace Java's mutable byte-order facade.</remarks>
internal sealed class UnreleasableByteBuf : WrappedByteBuf
{
    internal UnreleasableByteBuf(ByteBuf parent)
        : base(parent is UnreleasableByteBuf unreleasable ? unreleasable.Unwrap() : parent) { }

    // Original no-ops ignore even nonpositive counts and do not resurrect a dead owner.
    public override ByteBuf Retain(int increment = 1) => this;
    public override bool Release(int decrement = 1) => false;
    public override ByteBuf Touch(object hint = null) => this;

    public override ByteBuf AsReadOnly()
        => Buffer.IsReadOnly ? this : new UnreleasableByteBuf(Buffer.AsReadOnly());
    public override ByteBuf ReadSlice(int length) => new UnreleasableByteBuf(Buffer.ReadSlice(length));
    public override ByteBuf ReadRetainedSlice(int length)
    {
        // We could call buf.readSlice(..), and then call buf.release(). However this creates a leak in unit tests
        // because the release method on UnreleasableByteBuf will never allow the leak record to be cleaned up.
        // So we just use readSlice(..) because the end result should be logically equivalent.
        return ReadSlice(length);
    }
    public override ByteBuf Slice() => new UnreleasableByteBuf(Buffer.Slice());
    public override ByteBuf RetainedSlice()
    {
        // We could call buf.retainedSlice(), and then call buf.release(). However this creates a leak in unit tests
        // because the release method on UnreleasableByteBuf will never allow the leak record to be cleaned up.
        // So we just use slice() because the end result should be logically equivalent.
        return Slice();
    }
    public override ByteBuf Slice(int index, int length) => new UnreleasableByteBuf(Buffer.Slice(index, length));
    public override ByteBuf RetainedSlice(int index, int length)
    {
        // We could call buf.retainedSlice(..), and then call buf.release(). However this creates a leak in unit tests
        // because the release method on UnreleasableByteBuf will never allow the leak record to be cleaned up.
        // So we just use slice(..) because the end result should be logically equivalent.
        return Slice(index, length);
    }
    public override ByteBuf Duplicate() => new UnreleasableByteBuf(Buffer.Duplicate());
    public override ByteBuf RetainedDuplicate()
    {
        // We could call buf.retainedDuplicate(), and then call buf.release(). However this creates a leak in unit tests
        // because the release method on UnreleasableByteBuf will never allow the leak record to be cleaned up.
        // So we just use duplicate() because the end result should be logically equivalent.
        return Duplicate();
    }

}
