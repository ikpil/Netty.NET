/*
 * Copyright 2025 The Netty Project
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

namespace Netty.NET.Common.Internal;

internal class DirectCleaner : ICleaner
{
    public ICleanableDirectBuffer allocate(int capacity)
    {
        return new CleanableDirectBufferImpl(PlatformDependent.allocateDirectNoCleaner(capacity));
    }

    public void freeDirectBuffer(ArraySegment<byte> buffer)
    {
        PlatformDependent.freeDirectNoCleaner(buffer);
    }

    ICleanableDirectBuffer reallocate(ICleanableDirectBuffer buffer, int capacity)
    {
        ArraySegment<byte> newByteBuffer = PlatformDependent.reallocateDirectNoCleaner(buffer.buffer(), capacity);
        return new CleanableDirectBufferImpl(newByteBuffer);
    }

    private class CleanableDirectBufferImpl : ICleanableDirectBuffer
    {
        private readonly ArraySegment<byte> _buffer;

        internal CleanableDirectBufferImpl(ArraySegment<byte> buffer)
        {
            _buffer = buffer;
        }

        public ArraySegment<byte> buffer()
        {
            return _buffer;
        }

        public void clean()
        {
            PlatformDependent.freeDirectNoCleaner(_buffer);
        }
    }
}