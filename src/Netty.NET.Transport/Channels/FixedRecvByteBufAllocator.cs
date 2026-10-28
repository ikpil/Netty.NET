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

namespace Netty.NET.Transport.Channels;

/**
 * The {@link RecvByteBufAllocator} that always yields the same buffer
 * size prediction.  This predictor ignores the feed back from the I/O thread.
 */
public class FixedRecvByteBufAllocator : DefaultMaxMessagesRecvByteBufAllocator
{
    private readonly int _bufferSize;
    /**
     * Creates a new predictor that always returns the same prediction of
     * the specified buffer size.
     */
    public FixedRecvByteBufAllocator(int bufferSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
        _bufferSize = bufferSize;
    }
    public override IRecvByteBufAllocator.IHandle NewHandle() => new HandleImpl(this, _bufferSize);
    public override FixedRecvByteBufAllocator RespectMaybeMoreData(bool respectMaybeMoreData)
    {
        base.RespectMaybeMoreData(respectMaybeMoreData);
        return this;
    }
    private sealed class HandleImpl(FixedRecvByteBufAllocator owner, int size) : MaxMessageHandle(owner)
    {
        public override int Guess() => size;
    }
}
