/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Channel initializer useful in tests.
 */
// CLR: @Sharable is the Transport's [Sharable]; the public initChannel override is the protected InitChannel of the
// Transport's ChannelInitializer<T>; AtomicInteger is Common's AtomicInteger; the anonymous ExtendedHandle is a private
// nested class (newHandle() returns IRecvByteBufAllocator.IHandle, which the handle implements through
// IExtendedHandle), and UncheckedBooleanSupplier is Func<bool>.
[Sharable]
public class TestChannelInitializer : ChannelInitializer<IChannel>
{
    internal IChannelHandler handler;
    internal AtomicInteger maxReads;

    protected override void InitChannel(IChannel channel)
    {
        if (handler != null)
        {
            channel.Pipeline.AddLast(handler);
            handler = null;
        }
        if (maxReads != null)
        {
            channel.Config.SetRecvByteBufAllocator(new TestNumReadsRecvByteBufAllocator(maxReads));
        }
    }

    /**
     * Designed to read a single byte at a time to control the number of reads done at a fine granularity.
     */
    internal sealed class TestNumReadsRecvByteBufAllocator : IRecvByteBufAllocator
    {
        private readonly AtomicInteger numReads;
        internal TestNumReadsRecvByteBufAllocator(AtomicInteger numReads)
        {
            this.numReads = numReads;
        }

        public IRecvByteBufAllocator.IHandle NewHandle()
        {
            return new NumReadsHandle(numReads);
        }

        private sealed class NumReadsHandle : IRecvByteBufAllocator.IExtendedHandle
        {
            private readonly AtomicInteger numReads;
            private int attemptedBytesRead;
            private int lastBytesRead;
            private int numMessagesRead;

            internal NumReadsHandle(AtomicInteger numReads)
            {
                this.numReads = numReads;
            }

            public ByteBuf Allocate(IByteBufAllocator alloc)
            {
                return alloc.IoBuffer(Guess(), Guess());
            }

            public int Guess()
            {
                return 1; // only ever allocate buffers of size 1 to ensure the number of reads is controlled.
            }

            public void Reset(IChannelConfig config)
            {
                numMessagesRead = 0;
            }

            public void IncMessagesRead(int numMessages)
            {
                numMessagesRead += numMessages;
            }

            public void LastBytesRead(int bytes)
            {
                lastBytesRead = bytes;
            }

            public int LastBytesRead()
            {
                return lastBytesRead;
            }

            public void AttemptedBytesRead(int bytes)
            {
                attemptedBytesRead = bytes;
            }

            public int AttemptedBytesRead()
            {
                return attemptedBytesRead;
            }

            public bool ContinueReading()
            {
                return numMessagesRead < numReads.Get();
            }

            public bool ContinueReading(Func<bool> maybeMoreDataSupplier)
            {
                return ContinueReading();
            }

            public void ReadComplete()
            {
                // Nothing needs to be done or adjusted after each read cycle is completed.
            }
        }
    }
}
