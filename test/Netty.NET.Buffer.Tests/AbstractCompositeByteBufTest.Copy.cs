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

namespace Netty.NET.Buffer.Tests;

public abstract partial class AbstractCompositeByteBufTest
{
    [Fact]
    public void TestAllocatorIsSameWhenCopy() => AssertAllocatorIsSameWhenCopy(false);

    [Fact]
    public void TestAllocatorIsSameWhenCopyUsingIndexAndLength() => AssertAllocatorIsSameWhenCopy(true);

    private void AssertAllocatorIsSameWhenCopy(bool withIndexAndLength)
    {
        ByteBuf buffer = NewBuffer(8);
        ByteBuf copy = null;
        try
        {
            buffer.WriteZero(4);
            copy = withIndexAndLength ? buffer.Copy(0, 4) : buffer.Copy();
            Assert.Equal(buffer, copy);
            Assert.Equal(buffer.IsDirect, copy.IsDirect);
            Assert.Same(buffer.Allocator, copy.Allocator);
        }
        finally { buffer.Release(); copy?.Release(); }
    }
}
