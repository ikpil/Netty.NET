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
using Netty.NET.Common.Concurrent;
using Xunit;

namespace Netty.NET.Common.Tests.Concurrent;

public class MpscIntQueueTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    public void MustFillWithSpecifiedEmptyEntry(int size)
    {
        IMpscIntQueue queue = IMpscIntQueue.Create(size, -1);
        int filled = queue.Fill(size, () => 42);
        Assert.Equal(size, filled);
        for (int i = 0; i < size; i++)
        {
            Assert.Equal(42, queue.Poll());
        }
        Assert.Equal(-1, queue.Poll());
        Assert.True(queue.IsEmpty());
    }
}
