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
     * Creates a new buffer which wraps the specified memory address. If {@code doFree} is true the
     * memoryAddress will automatically be freed once the reference count of the {@link ByteBuf} reaches {@code 0}.
     */
    /// <remarks>Use the overload that accepts bounded Memory&lt;byte&gt;. This overload
    /// always throws before accessing the address or taking ownership.</remarks>
    [Obsolete("Wrapping raw addresses is unsupported. Use WrappedBuffer(Memory<byte>).")]
    public static ByteBuf WrappedBuffer(nint memoryAddress, int size, bool doFree)
    {
        throw new PlatformNotSupportedException("Raw address buffers are unavailable on the managed allocator.");
    }
}
