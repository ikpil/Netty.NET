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
    public void TestFindNextPositivePowerOfTwo()
    {
        Assert.Equal(1, FindNextPositivePowerOfTwo(0));
        Assert.Equal(1, FindNextPositivePowerOfTwo(1));
        Assert.Equal(1024, FindNextPositivePowerOfTwo(1000));
        Assert.Equal(1024, FindNextPositivePowerOfTwo(1023));
        Assert.Equal(2048, FindNextPositivePowerOfTwo(2048));
        Assert.Equal(1 << 30, FindNextPositivePowerOfTwo((1 << 30) - 1));
        Assert.Equal(1, FindNextPositivePowerOfTwo(-1));
        Assert.Equal(1, FindNextPositivePowerOfTwo(-10000));
    }

    [Fact]
    public void TestSafeFindNextPositivePowerOfTwo()
    {
        Assert.Equal(1, SafeFindNextPositivePowerOfTwo(0));
        Assert.Equal(1, SafeFindNextPositivePowerOfTwo(1));
        Assert.Equal(1024, SafeFindNextPositivePowerOfTwo(1000));
        Assert.Equal(1024, SafeFindNextPositivePowerOfTwo(1023));
        Assert.Equal(2048, SafeFindNextPositivePowerOfTwo(2048));
        Assert.Equal(1 << 30, SafeFindNextPositivePowerOfTwo((1 << 30) - 1));
        Assert.Equal(1, SafeFindNextPositivePowerOfTwo(-1));
        Assert.Equal(1, SafeFindNextPositivePowerOfTwo(-10000));
        Assert.Equal(1 << 30, SafeFindNextPositivePowerOfTwo(int.MaxValue));
        Assert.Equal(1 << 30, SafeFindNextPositivePowerOfTwo((1 << 30) + 1));
        Assert.Equal(1, SafeFindNextPositivePowerOfTwo(int.MinValue));
        Assert.Equal(1, SafeFindNextPositivePowerOfTwo(int.MinValue + 1));
    }

    [Fact]
    public void TestIsOutOfBounds()
    {
        Assert.False(IsOutOfBounds(0, 0, 0));
        Assert.False(IsOutOfBounds(0, 0, 1));
        Assert.False(IsOutOfBounds(0, 1, 1));
        Assert.True(IsOutOfBounds(1, 1, 1));
        Assert.True(IsOutOfBounds(int.MaxValue, 1, 1));
        Assert.True(IsOutOfBounds(int.MaxValue, int.MaxValue, 1));
        Assert.True(IsOutOfBounds(int.MaxValue, int.MaxValue, int.MaxValue));
        Assert.False(IsOutOfBounds(0, int.MaxValue, int.MaxValue));
        Assert.False(IsOutOfBounds(0, int.MaxValue - 1, int.MaxValue));
        Assert.True(IsOutOfBounds(0, int.MaxValue, int.MaxValue - 1));
        Assert.False(IsOutOfBounds(int.MaxValue - 1, 1, int.MaxValue));
        Assert.True(IsOutOfBounds(int.MaxValue - 1, 1, int.MaxValue - 1));
        Assert.True(IsOutOfBounds(int.MaxValue - 1, 2, int.MaxValue));
        Assert.True(IsOutOfBounds(1, int.MaxValue, int.MaxValue));
        Assert.True(IsOutOfBounds(0, 1, int.MinValue));
        Assert.True(IsOutOfBounds(0, 1, -1));
        Assert.True(IsOutOfBounds(0, int.MaxValue, 0));
    }
}