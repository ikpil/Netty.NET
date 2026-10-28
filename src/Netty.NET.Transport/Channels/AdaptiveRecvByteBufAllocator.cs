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
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;

/**
 * The {@link RecvByteBufAllocator} that automatically increases and
 * decreases the predicted buffer size on feed back.
 * <p>
 * It gradually increases the expected number of readable bytes if the previous
 * read fully filled the allocated buffer.  It gradually decreases the expected
 * number of readable bytes if the read operation was not able to fill a certain
 * amount of the allocated buffer two times consecutively.  Otherwise, it keeps
 * returning the same prediction.
 */
public class AdaptiveRecvByteBufAllocator : DefaultMaxMessagesRecvByteBufAllocator
{
    public const int DEFAULT_MINIMUM = 64;
    // Use an initial value that is bigger than the common MTU of 1500
    public const int DEFAULT_INITIAL = 2048;
    public const int DEFAULT_MAXIMUM = 65536;
    /**
     * @deprecated There is state for {@link #maxMessagesPerRead()} which is typically based upon channel type.
     */
    [Obsolete("Create a per-channel allocator because maxMessagesPerRead is mutable.")]
    public static readonly AdaptiveRecvByteBufAllocator DEFAULT = new();
    private readonly int _minimum;
    private readonly int _initial;
    private readonly int _maximum;
    /**
     * Creates a new predictor with the default parameters.  With the default
     * parameters, the expected buffer size starts from {@code 1024}, does not
     * go down below {@code 64}, and does not go up above {@code 65536}.
     */
    // CLR: the original comment says 1024, but the pinned DEFAULT_INITIAL is 2048.
    public AdaptiveRecvByteBufAllocator() : this(DEFAULT_MINIMUM, DEFAULT_INITIAL, DEFAULT_MAXIMUM) { }
    /**
     * Creates a new predictor with the specified parameters.
     *
     * @param minimum  the inclusive lower bound of the expected buffer size
     * @param initial  the initial buffer size when no feed back was received
     * @param maximum  the inclusive upper bound of the expected buffer size
     */
    public AdaptiveRecvByteBufAllocator(int minimum, int initial, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);
        if (initial < minimum)
            throw new ArgumentOutOfRangeException(nameof(initial));
        if (maximum < initial)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        _minimum = minimum;
        _initial = initial;
        _maximum = maximum;
    }
    public override IRecvByteBufAllocator.IHandle NewHandle() => new HandleImpl(this, _minimum, _initial, _maximum);
    public override AdaptiveRecvByteBufAllocator RespectMaybeMoreData(bool respectMaybeMoreData)
    {
        base.RespectMaybeMoreData(respectMaybeMoreData);
        return this;
    }
    private sealed class HandleImpl : MaxMessageHandle
    {
        private readonly AdaptiveCalculator _calculator;
        internal HandleImpl(AdaptiveRecvByteBufAllocator owner, int minimum, int initial, int maximum) : base(owner)
            => _calculator = new AdaptiveCalculator(minimum, initial, maximum);
        public override void LastBytesRead(int bytes)
        {
            // If we read as much as we asked for we should check if we need to ramp up the size of our next guess.
            // This helps adjust more quickly when large amounts of data is pending and can avoid going back to
            // the selector to check for more data. Going back to the selector can add significant latency for large
            // data transfers.
            if (bytes == AttemptedBytesRead())
                _calculator.Record(bytes);
            base.LastBytesRead(bytes);
        }
        public override int Guess() => _calculator.NextSize;
        public override void ReadComplete() => _calculator.Record(TotalBytesRead());
    }
}
