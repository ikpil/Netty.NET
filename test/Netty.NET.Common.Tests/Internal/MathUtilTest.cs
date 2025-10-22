/*
 * Copyright 2020 The Netty Project
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

namespace Netty.NET.Common.Tests.Internal;

using static Netty.NET.Common.Internal.MathUtil;

public class MathUtilTest
{
    [Fact]
    public void testFindNextPositivePowerOfTwo()
    {
        Assert.Equal(1, findNextPositivePowerOfTwo(0));
        Assert.Equal(1, findNextPositivePowerOfTwo(1));
        Assert.Equal(1024, findNextPositivePowerOfTwo(1000));
        Assert.Equal(1024, findNextPositivePowerOfTwo(1023));
        Assert.Equal(2048, findNextPositivePowerOfTwo(2048));
        Assert.Equal(1 << 30, findNextPositivePowerOfTwo((1 << 30) - 1));
        Assert.Equal(1, findNextPositivePowerOfTwo(-1));
        Assert.Equal(1, findNextPositivePowerOfTwo(-10000));
    }

    [Fact]
    public void testSafeFindNextPositivePowerOfTwo()
    {
        Assert.Equal(1, safeFindNextPositivePowerOfTwo(0));
        Assert.Equal(1, safeFindNextPositivePowerOfTwo(1));
        Assert.Equal(1024, safeFindNextPositivePowerOfTwo(1000));
        Assert.Equal(1024, safeFindNextPositivePowerOfTwo(1023));
        Assert.Equal(2048, safeFindNextPositivePowerOfTwo(2048));
        Assert.Equal(1 << 30, safeFindNextPositivePowerOfTwo((1 << 30) - 1));
        Assert.Equal(1, safeFindNextPositivePowerOfTwo(-1));
        Assert.Equal(1, safeFindNextPositivePowerOfTwo(-10000));
        Assert.Equal(1 << 30, safeFindNextPositivePowerOfTwo(int.MaxValue));
        Assert.Equal(1 << 30, safeFindNextPositivePowerOfTwo((1 << 30) + 1));
        Assert.Equal(1, safeFindNextPositivePowerOfTwo(int.MinValue));
        Assert.Equal(1, safeFindNextPositivePowerOfTwo(int.MinValue + 1));
    }

    [Fact]
    public void testIsOutOfBounds()
    {
        Assert.False(isOutOfBounds(0, 0, 0));
        Assert.False(isOutOfBounds(0, 0, 1));
        Assert.False(isOutOfBounds(0, 1, 1));
        Assert.True(isOutOfBounds(1, 1, 1));
        Assert.True(isOutOfBounds(int.MaxValue, 1, 1));
        Assert.True(isOutOfBounds(int.MaxValue, int.MaxValue, 1));
        Assert.True(isOutOfBounds(int.MaxValue, int.MaxValue, int.MaxValue));
        Assert.False(isOutOfBounds(0, int.MaxValue, int.MaxValue));
        Assert.False(isOutOfBounds(0, int.MaxValue - 1, int.MaxValue));
        Assert.True(isOutOfBounds(0, int.MaxValue, int.MaxValue - 1));
        Assert.False(isOutOfBounds(int.MaxValue - 1, 1, int.MaxValue));
        Assert.True(isOutOfBounds(int.MaxValue - 1, 1, int.MaxValue - 1));
        Assert.True(isOutOfBounds(int.MaxValue - 1, 2, int.MaxValue));
        Assert.True(isOutOfBounds(1, int.MaxValue, int.MaxValue));
        Assert.True(isOutOfBounds(0, 1, int.MinValue));
        Assert.True(isOutOfBounds(0, 1, -1));
        Assert.True(isOutOfBounds(0, int.MaxValue, 0));
    }
}