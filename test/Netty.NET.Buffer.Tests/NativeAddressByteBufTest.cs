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

namespace Netty.NET.Buffer.Tests;

public class NativeAddressByteBufTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawAddressWrappingRejectsTheAddressWithoutTakingOwnership(bool doFree)
    {
#pragma warning disable CS0618 // Exercise the legacy entry point without dereferencing the synthetic address.
        Assert.Throws<PlatformNotSupportedException>(() => Unpooled.WrappedBuffer(1, 16, doFree));
#pragma warning restore CS0618
    }

    [Fact]
    public void ManagedMemoryReplacementWrapsTheSelectedRangeWithoutCopying()
    {
        byte[] storage = [0, 1, 2, 3, 4];
        ByteBuf buffer = Unpooled.WrappedBuffer(storage.AsMemory(1, 3));
        try
        {
            Assert.Equal(3, buffer.ReadableBytes);
            Assert.Equal(1, buffer.GetByte(0));
            storage[2] = 9;
            Assert.Equal(9, buffer.GetByte(1));
            buffer.SetByte(2, 8);
            Assert.Equal(new byte[] { 0, 1, 9, 8, 4 }, storage);
        }
        finally
        {
            buffer.Release();
        }
    }
}
