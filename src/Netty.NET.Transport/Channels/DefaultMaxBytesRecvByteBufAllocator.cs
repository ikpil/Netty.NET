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
 * The {@link RecvByteBufAllocator} that yields a buffer size prediction based upon decrementing the value from
 * the max bytes per read.
 */
public class DefaultMaxBytesRecvByteBufAllocator : IMaxBytesRecvByteBufAllocator
{
    private readonly object _gate = new();
    private volatile int _maxBytesPerRead;
    private volatile int _maxBytesPerIndividualRead;
    public DefaultMaxBytesRecvByteBufAllocator() : this(64 * 1024, 64 * 1024) { }
    public DefaultMaxBytesRecvByteBufAllocator(int maxBytesPerRead, int maxBytesPerIndividualRead)
    {
        CheckMaxBytesPerReadPair(maxBytesPerRead, maxBytesPerIndividualRead);
        _maxBytesPerRead = maxBytesPerRead;
        _maxBytesPerIndividualRead = maxBytesPerIndividualRead;
    }
    public IRecvByteBufAllocator.IHandle NewHandle() => new HandleImpl(this);
    public int MaxBytesPerRead() => _maxBytesPerRead;
    public DefaultMaxBytesRecvByteBufAllocator MaxBytesPerRead(int maxBytesPerRead)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytesPerRead);
        // There is a dependency between this.maxBytesPerRead and this.maxBytesPerIndividualRead (a < b).
        // Write operations must be synchronized, but independent read operations can just be volatile.
        lock (_gate)
        {
            if (maxBytesPerRead < _maxBytesPerIndividualRead)
                throw new ArgumentOutOfRangeException(nameof(maxBytesPerRead));
            _maxBytesPerRead = maxBytesPerRead;
        }
        return this;
    }
    public int MaxBytesPerIndividualRead() => _maxBytesPerIndividualRead;
    public DefaultMaxBytesRecvByteBufAllocator MaxBytesPerIndividualRead(int maxBytesPerIndividualRead)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytesPerIndividualRead);
        // There is a dependency between this.maxBytesPerRead and this.maxBytesPerIndividualRead (a < b).
        // Write operations must be synchronized, but independent read operations can just be volatile.
        lock (_gate)
        {
            if (maxBytesPerIndividualRead > _maxBytesPerRead)
                throw new ArgumentOutOfRangeException(nameof(maxBytesPerIndividualRead));
            _maxBytesPerIndividualRead = maxBytesPerIndividualRead;
        }
        return this;
    }
    public (int MaxBytesPerRead, int MaxBytesPerIndividualRead) MaxBytesPerReadPair()
    {
        lock (_gate)
            return (_maxBytesPerRead, _maxBytesPerIndividualRead);
    }
    public DefaultMaxBytesRecvByteBufAllocator MaxBytesPerReadPair(int maxBytesPerRead, int maxBytesPerIndividualRead)
    {
        CheckMaxBytesPerReadPair(maxBytesPerRead, maxBytesPerIndividualRead);
        // There is a dependency between this.maxBytesPerRead and this.maxBytesPerIndividualRead (a < b).
        // Write operations must be synchronized, but independent read operations can just be volatile.
        lock (_gate)
        {
            _maxBytesPerRead = maxBytesPerRead;
            _maxBytesPerIndividualRead = maxBytesPerIndividualRead;
        }
        return this;
    }
    IMaxBytesRecvByteBufAllocator IMaxBytesRecvByteBufAllocator.MaxBytesPerRead(int value) => MaxBytesPerRead(value);
    IMaxBytesRecvByteBufAllocator IMaxBytesRecvByteBufAllocator.MaxBytesPerIndividualRead(int value) => MaxBytesPerIndividualRead(value);
    IMaxBytesRecvByteBufAllocator IMaxBytesRecvByteBufAllocator.MaxBytesPerReadPair(int total, int individual) => MaxBytesPerReadPair(total, individual);
    private static void CheckMaxBytesPerReadPair(int total, int individual)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(total);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(individual);
        if (total < individual)
            throw new ArgumentOutOfRangeException(nameof(individual));
    }
    private sealed class HandleImpl(DefaultMaxBytesRecvByteBufAllocator owner) : IRecvByteBufAllocator.IExtendedHandle
    {
        private int _individualReadMax;
        private int _bytesToRead;
        private int _lastBytesRead;
        private int _attemptBytesRead;
        public ByteBuf Allocate(IByteBufAllocator alloc) => alloc.IoBuffer(Guess());
        public int Guess() => Math.Min(_individualReadMax, _bytesToRead);
        public void Reset(IChannelConfig config)
        {
            _bytesToRead = owner.MaxBytesPerRead();
            _individualReadMax = owner.MaxBytesPerIndividualRead();
        }
        public void IncMessagesRead(int amt) { }
        public void LastBytesRead(int bytes)
        {
            _lastBytesRead = bytes;
            // Ignore if bytes is negative, the interface contract states it will be detected externally after call.
            // The value may be "invalid" after this point, but it doesn't matter because reading will be stopped.
            _bytesToRead = unchecked(_bytesToRead - bytes);
        }
        public int LastBytesRead() => _lastBytesRead;
        public bool ContinueReading() => ContinueReading(() => _attemptBytesRead == _lastBytesRead);
        public bool ContinueReading(Func<bool> maybeMoreDataSupplier)
        {
            // Keep reading if we are allowed to read more bytes, and our last read filled up the buffer we provided.
            return _bytesToRead > 0 && maybeMoreDataSupplier();
        }
        public void ReadComplete() { }
        public int AttemptedBytesRead() => _attemptBytesRead;
        public void AttemptedBytesRead(int bytes) => _attemptBytesRead = bytes;
    }
}
