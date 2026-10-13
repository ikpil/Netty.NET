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
    /// <remarks>The range is not copied and initially fully readable. When doFree is true,
    /// the pointer must be the base of an allocation compatible with NativeMemory.Free;
    /// ownership transfers only when this method succeeds. Otherwise the caller owns the
    /// allocation and must keep it valid throughout buffer access and outstanding pins.
    /// A capacity change detaches to owned native storage. Final release or detachment
    /// invalidates new access to the old Memory; outstanding pins delay its owned free.
    /// External storage does not charge Netty's native reservation or allocator metrics.</remarks>
    public static ByteBuf WrappedBuffer(nint memoryAddress, int size, bool doFree)
    {
        var manager = new NativeAddressMemoryManager(memoryAddress, size);
        try
        {
            var buffer = new UnpooledDirectByteBuf(UnpooledByteBufAllocator.Default, manager);
            manager.TakeOwnership(doFree);
            return buffer;
        }
        catch
        {
            ((IDisposable)manager).Dispose();
            throw;
        }
    }
}
