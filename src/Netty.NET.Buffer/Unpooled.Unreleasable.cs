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

namespace Netty.NET.Buffer;

public static partial class Unpooled
{
    /**
     * Return a unreleasable view on the given {@link ByteBuf} which will just ignore release and retain calls.
     */
    /// <remarks>Borrowed facade: no reference is acquired. The original owner must
    /// remain alive and is responsible for the final release.</remarks>
    public static ByteBuf UnreleasableBuffer(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return new UnreleasableByteBuf(buffer);
    }
}
