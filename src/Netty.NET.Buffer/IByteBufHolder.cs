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

using Netty.NET.Common;

namespace Netty.NET.Buffer;

/**
 * A packet which is send or receive.
 */
public interface IByteBufHolder : IReferenceCounted
{
    /**
     * Return the data which is held by this {@link ByteBufHolder}.
     */
    ByteBuf Content { get; }

    /**
     * Creates a deep copy of this {@link ByteBufHolder}.
     */
    IByteBufHolder Copy();

    /**
     * Duplicates this {@link ByteBufHolder}. Be aware that this will not automatically call {@link #retain()}.
     */
    IByteBufHolder Duplicate();

    /**
     * Duplicates this {@link ByteBufHolder}. This method returns a retained duplicate unlike {@link #duplicate()}.
     *
     * @see ByteBuf#retainedDuplicate()
     */
    IByteBufHolder RetainedDuplicate();

    /**
     * Returns a new {@link ByteBufHolder} which contains the specified {@code content}.
     */
    IByteBufHolder Replace(ByteBuf content);

    new IByteBufHolder Retain();
    new IByteBufHolder Retain(int increment);
    new IByteBufHolder Touch();
    new IByteBufHolder Touch(object hint);
}
