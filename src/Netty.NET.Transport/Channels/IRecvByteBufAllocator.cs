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
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Channels;

/**
 * Allocates a new receive buffer whose capacity is probably large enough to read all inbound data and small enough
 * not to waste its space.
 */
public interface IRecvByteBufAllocator
{
    /**
     * Creates a new handle.  The handle provides the actual operations and keeps the internal information which is
     * required for predicting an optimal buffer capacity.
     */
    IHandle NewHandle();
    /**
     * @deprecated Use {@link ExtendedHandle}.
     */
    // CLR: retain the base contract for compatibility; the extended handle adds a native Func<bool> supplier.
    public interface IHandle
    {
        /**
         * Creates a new receive buffer whose capacity is probably large enough to read all inbound data and small
         * enough not to waste its space.
         */
        ByteBuf Allocate(IByteBufAllocator alloc);
        /**
         * Similar to {@link #allocate(ByteBufAllocator)} except that it does not allocate anything but just tells the
         * capacity.
         */
        int Guess();
        /**
         * Reset any counters that have accumulated and recommend how many messages/bytes should be read for the next
         * read loop.
         * <p>
         * This may be used by {@link #continueReading()} to determine if the read operation should complete.
         * </p>
         * This is only ever a hint and may be ignored by the implementation.
         * @param config The channel configuration which may impact this object's behavior.
         */
        void Reset(IChannelConfig config);
        /**
         * Increment the number of messages that have been read for the current read loop.
         * @param numMessages The amount to increment by.
         */
        void IncMessagesRead(int numMessages);
        /**
         * Set the bytes that have been read for the last read operation.
         * This may be used to increment the number of bytes that have been read.
         * @param bytes The number of bytes from the previous read operation. This may be negative if an read error
         * occurs. If a negative value is seen it is expected to be return on the next call to
         * {@link #lastBytesRead()}. A negative value will signal a termination condition enforced externally
         * to this class and is not required to be enforced in {@link #continueReading()}.
         */
        void LastBytesRead(int bytes);
        /**
         * Get the amount of bytes for the previous read operation.
         * @return The amount of bytes for the previous read operation.
         */
        int LastBytesRead();
        /**
         * Set how many bytes the read operation will (or did) attempt to read.
         * @param bytes How many bytes the read operation will (or did) attempt to read.
         */
        void AttemptedBytesRead(int bytes);
        /**
         * Get how many bytes the read operation will (or did) attempt to read.
         * @return How many bytes the read operation will (or did) attempt to read.
         */
        int AttemptedBytesRead();
        /**
         * Determine if the current read loop should continue.
         * @return {@code true} if the read loop should continue reading. {@code false} if the read loop is complete.
         */
        bool ContinueReading();
        /**
         * The read has completed.
         */
        void ReadComplete();
    }
    public interface IExtendedHandle : IHandle
    {
        /**
         * Same as {@link Handle#continueReading()} except "more data" is determined by the supplier parameter.
         * @param maybeMoreDataSupplier A supplier that determines if there maybe more data to read.
         */
        bool ContinueReading(Func<bool> maybeMoreDataSupplier);
    }
    /**
     * A {@link Handle} which delegates all call to some other {@link Handle}.
     */
    public class DelegatingHandle : IHandle
    {
        private readonly IHandle _delegate;
        public DelegatingHandle(IHandle handle)
        {
            ArgumentNullException.ThrowIfNull(handle);
            _delegate = handle;
        }
        /**
         * Get the {@link Handle} which all methods will be delegated to.
         * @return the {@link Handle} which all methods will be delegated to.
         */
        protected IHandle Delegate() => _delegate;
        public virtual ByteBuf Allocate(IByteBufAllocator alloc) => _delegate.Allocate(alloc);
        public virtual int Guess() => _delegate.Guess();
        public virtual void Reset(IChannelConfig config) => _delegate.Reset(config);
        public virtual void IncMessagesRead(int numMessages) => _delegate.IncMessagesRead(numMessages);
        public virtual void LastBytesRead(int bytes) => _delegate.LastBytesRead(bytes);
        public virtual int LastBytesRead() => _delegate.LastBytesRead();
        public virtual void AttemptedBytesRead(int bytes) => _delegate.AttemptedBytesRead(bytes);
        public virtual int AttemptedBytesRead() => _delegate.AttemptedBytesRead();
        public virtual bool ContinueReading() => _delegate.ContinueReading();
        public virtual void ReadComplete() => _delegate.ReadComplete();
    }
}
