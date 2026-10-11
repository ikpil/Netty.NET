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
     * Creates a new buffer which wraps the specified NIO buffer's current
     * slice.  A modification on the specified buffer's content will be
     * visible to the returned buffer.
     */
    /// <summary>Wraps an external read-only range without copying or taking ownership of its owner.</summary>
    /// <remarks>Slice the ReadOnlyMemory before calling to select the remaining range.
    /// The source owner must stay valid during access. Empty memory uses the shared sentinel.</remarks>
    public static ByteBuf WrappedBuffer(ReadOnlyMemory<byte> memory)
        => memory.IsEmpty ? EmptyBuffer : new ReadOnlyMemoryByteBuf(UnpooledByteBufAllocator.Default, memory);
}
