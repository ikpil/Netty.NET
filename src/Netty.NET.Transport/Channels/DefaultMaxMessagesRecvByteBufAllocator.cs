/*
 * Copyright 2015 The Netty Project
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
 * Default implementation of {@link MaxMessagesRecvByteBufAllocator} which respects {@link ChannelConfig#isAutoRead()}
 * and also prevents overflow.
 */
public abstract class DefaultMaxMessagesRecvByteBufAllocator : IMaxMessagesRecvByteBufAllocator
{
    private readonly bool _ignoreBytesRead;
    private volatile int _maxMessagesPerRead;
    private volatile bool _respectMaybeMoreData = true;
    protected DefaultMaxMessagesRecvByteBufAllocator() : this(1) { }
    protected DefaultMaxMessagesRecvByteBufAllocator(int maxMessagesPerRead) : this(maxMessagesPerRead, false) { }
    protected internal DefaultMaxMessagesRecvByteBufAllocator(int maxMessagesPerRead, bool ignoreBytesRead)
    {
        _ignoreBytesRead = ignoreBytesRead;
        MaxMessagesPerRead(maxMessagesPerRead);
    }
    public int MaxMessagesPerRead() => _maxMessagesPerRead;
    public IMaxMessagesRecvByteBufAllocator MaxMessagesPerRead(int maxMessagesPerRead)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessagesPerRead);
        _maxMessagesPerRead = maxMessagesPerRead;
        return this;
    }
    /**
     * Determine if future instances of {@link #newHandle()} will stop reading if we think there is no more data.
     * @param respectMaybeMoreData
     * <ul>
     *     <li>{@code true} to stop reading if we think there is no more data. This may save a system call to read from
     *          the socket, but if data has arrived in a racy fashion we may give up our {@link #maxMessagesPerRead()}
     *          quantum and have to wait for the selector to notify us of more data.</li>
     *     <li>{@code false} to keep reading (up to {@link #maxMessagesPerRead()}) or until there is no data when we
     *          attempt to read.</li>
     * </ul>
     * @return {@code this}.
     */
    public virtual DefaultMaxMessagesRecvByteBufAllocator RespectMaybeMoreData(bool respectMaybeMoreData)
    {
        _respectMaybeMoreData = respectMaybeMoreData;
        return this;
    }
    /**
     * Get if future instances of {@link #newHandle()} will stop reading if we think there is no more data.
     * @return
     * <ul>
     *     <li>{@code true} to stop reading if we think there is no more data. This may save a system call to read from
     *          the socket, but if data has arrived in a racy fashion we may give up our {@link #maxMessagesPerRead()}
     *          quantum and have to wait for the selector to notify us of more data.</li>
     *     <li>{@code false} to keep reading (up to {@link #maxMessagesPerRead()}) or until there is no data when we
     *          attempt to read.</li>
     * </ul>
     */
    public bool RespectMaybeMoreData() => _respectMaybeMoreData;
    public abstract IRecvByteBufAllocator.IHandle NewHandle();
    /**
     * Focuses on enforcing the maximum messages per read condition for {@link #continueReading()}.
     */
    public abstract class MaxMessageHandle : IRecvByteBufAllocator.IExtendedHandle
    {
        private readonly DefaultMaxMessagesRecvByteBufAllocator _owner;
        private readonly bool _respectMaybeMoreData;
        private readonly Func<bool> _defaultMaybeMoreSupplier;
        private IChannelConfig _config;
        private int _maxMessagePerRead;
        private int _totalMessages;
        private int _totalBytesRead;
        private int _attemptedBytesRead;
        private int _lastBytesRead;
        protected MaxMessageHandle(DefaultMaxMessagesRecvByteBufAllocator owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _owner = owner;
            _respectMaybeMoreData = owner.RespectMaybeMoreData();
            _defaultMaybeMoreSupplier = () => _attemptedBytesRead == _lastBytesRead;
        }
        /**
         * Only {@link ChannelConfig#getMaxMessagesPerRead()} is used.
         */
        public virtual void Reset(IChannelConfig config)
        {
            _config = config;
            _maxMessagePerRead = _owner.MaxMessagesPerRead();
            _totalMessages = _totalBytesRead = 0;
        }
        public virtual ByteBuf Allocate(IByteBufAllocator alloc) => alloc.IoBuffer(Guess());
        public abstract int Guess();
        public void IncMessagesRead(int amt) => _totalMessages = unchecked(_totalMessages + amt);
        public virtual void LastBytesRead(int bytes)
        {
            _lastBytesRead = bytes;
            if (bytes > 0)
                _totalBytesRead = unchecked(_totalBytesRead + bytes);
        }
        public int LastBytesRead() => _lastBytesRead;
        public virtual bool ContinueReading() => ContinueReading(_defaultMaybeMoreSupplier);
        public virtual bool ContinueReading(Func<bool> maybeMoreDataSupplier)
            => _config.IsAutoRead() && (!_respectMaybeMoreData || maybeMoreDataSupplier()) &&
                _totalMessages < _maxMessagePerRead && (_owner._ignoreBytesRead || _totalBytesRead > 0);
        public virtual void ReadComplete() { }
        public int AttemptedBytesRead() => _attemptedBytesRead;
        public void AttemptedBytesRead(int bytes) => _attemptedBytesRead = bytes;
        protected int TotalBytesRead() => _totalBytesRead < 0 ? int.MaxValue : _totalBytesRead;
    }
}
