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

namespace Netty.NET.Transport.Channels;

/**
 * WriteBufferWaterMark is used to set low water mark and high water mark for the write buffer.
 * <p>
 * If the number of bytes queued in the write buffer exceeds the
 * {@linkplain #high high water mark}, {@link Channel#isWritable()}
 * will start to return {@code false}.
 * <p>
 * If the number of bytes queued in the write buffer exceeds the
 * {@linkplain #high high water mark} and then
 * dropped down below the {@linkplain #low low water mark},
 * {@link Channel#isWritable()} will start to return
 * {@code true} again.
 * <p>
 * Note that messages needs to be handled by the {@link MessageSizeEstimator}
 * used by the channel for {@link Channel#isWritable()} to provide accurate back-pressure.
 */
public sealed class WriteBufferWaterMark
{
    public static readonly WriteBufferWaterMark DEFAULT = new(32 * 1024, 64 * 1024, false);
    private readonly int _low;
    private readonly int _high;
    /**
     * Create a new instance.
     *
     * @param low low water mark for write buffer.
     * @param high high water mark for write buffer
     */
    public WriteBufferWaterMark(int low, int high) : this(low, high, true) { }
    /**
     * This constructor is needed to keep backward-compatibility.
     */
    internal WriteBufferWaterMark(int low, int high, bool validate)
    {
        if (validate)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(low);
            if (high < low)
                throw new ArgumentOutOfRangeException(nameof(high), "High water mark cannot be less than low water mark.");
        }
        _low = low;
        _high = high;
    }
    /**
     * Returns the low water mark for the write buffer.
     */
    public int Low() => _low;
    /**
     * Returns the high water mark for the write buffer.
     */
    public int High() => _high;
    public override string ToString() => FormattableString.Invariant($"WriteBufferWaterMark(low: {_low}, high: {_high})");
}
