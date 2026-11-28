/*
 * Copyright 2024 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Ssl;

/**
 * Each call to SSL_write will introduce about ~100 bytes of overhead. This coalescing queue attempts to increase
 * goodput by aggregating the plaintext in chunks of {@link #wrapDataSize}. If many small chunks are written
 * this can increase goodput, decrease the amount of calls to SSL_write, and decrease overall encryption operations.
 */
// CLR: the package-private constructor is internal; catch (Throwable) is catch (Exception) and
// PlatformDependent.throwException(cause) rethrows the cause unchanged, which is throw; (as
// src/Netty.NET.Handler.Codec/Base64Codec/Base64.cs records); ByteBuf capacity() is the Capacity property of the Buffer
// port.
internal abstract class SslHandlerCoalescingBufferQueue : AbstractCoalescingBufferQueue
{

    private readonly bool wantsDirectBuffer;

    internal SslHandlerCoalescingBufferQueue(IChannel channel, int initSize, bool wantsDirectBuffer)
        : base(channel, initSize)
    {
        this.wantsDirectBuffer = wantsDirectBuffer;
    }

    protected abstract int WrapDataSize();

    protected override ByteBuf Compose(IByteBufAllocator alloc, ByteBuf cumulation, ByteBuf next)
    {
        return AttemptCopyToCumulation(cumulation, next, WrapDataSize()) ? cumulation :
                CopyAndCompose(alloc, cumulation, next);
    }

    protected override ByteBuf ComposeFirst(IByteBufAllocator allocator, ByteBuf first, int bufferSize)
    {
        ByteBuf newFirst;

        if (wantsDirectBuffer)
        {
            newFirst = allocator.DirectBuffer(bufferSize);
        }
        else
        {
            newFirst = allocator.HeapBuffer(bufferSize);
        }

        try
        {
            newFirst.WriteBytes(first);
        }
        catch (Exception)
        {
            newFirst.Release();
            throw;
        }

        Debug.Assert(!first.IsReadable());
        first.Release();
        return newFirst;
    }

    protected override ByteBuf RemoveEmptyValue()
    {
        return null;
    }

    private static bool AttemptCopyToCumulation(ByteBuf cumulation, ByteBuf next, int wrapDataSize)
    {
        int inReadableBytes = next.ReadableBytes;
        // Nothing to copy so just release the buffer.
        if (inReadableBytes == 0)
        {
            next.Release();
            return true;
        }

        int cumulationCapacity = cumulation.Capacity;
        if (wrapDataSize - cumulation.ReadableBytes >= inReadableBytes &&
                // Avoid using the same buffer if next's data would make cumulation exceed the wrapDataSize.
                // Only copy if there is enough space available and the capacity is large enough, and attempt to
                // resize if the capacity is small.
                (cumulation.IsWritable(inReadableBytes) && cumulationCapacity >= wrapDataSize ||
                        cumulationCapacity < wrapDataSize &&
                                ByteBufUtil.EnsureWritableSuccess(cumulation.EnsureWritable(inReadableBytes, false))))
        {
            cumulation.WriteBytes(next);
            next.Release();
            return true;
        }

        return false;
    }
}
