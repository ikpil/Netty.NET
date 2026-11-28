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

using System;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: java.nio.ByteBuffer is ReadOnlyMemory<byte> : ByteBuffer.wrap(bytes) is the memory over the array,
// position(1) is Slice(1), and ByteBuffer equality (the remaining bytes) compares the bytes as arrays. id() is the
// internal Id(). ThreadLocalRandom.current() is Random.Shared. The NullPointerException of new
// QuicConnectionAddress((byte[]) null) comes from connId.clone() on the null array (a dereference, not a
// checkNotNull), whose CLR counterpart is NullReferenceException. testNullByteBuffer is CLR-inapplicable : a
// ReadOnlyMemory<byte> is a struct and cannot be null (a null array converts to an empty memory), so the null
// check of QuicConnectionAddress(ByteBuffer) has no counterpart.
public class QuicConnectionAddressTest : AbstractQuicTest
{

    [Fact]
    public void TestNullByteArray()
    {
        Assert.Throws<NullReferenceException>(() => new QuicConnectionAddress((byte[]) null));
    }

    [Fact]
    public void TestByteArrayIsCloned()
    {
        byte[] bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        QuicConnectionAddress address = new QuicConnectionAddress(bytes);
        Assert.Equal(new ReadOnlyMemory<byte>(bytes).ToArray(), address.Id().ToArray());
        Random.Shared.NextBytes(bytes);
        Assert.NotEqual(new ReadOnlyMemory<byte>(bytes).ToArray(), address.Id().ToArray());
    }

    [Fact]
    public void TesByteBufferIsDuplicated()
    {
        byte[] bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        ReadOnlyMemory<byte> buffer = new ReadOnlyMemory<byte>(bytes);
        QuicConnectionAddress address = new QuicConnectionAddress(bytes);
        Assert.Equal(buffer.ToArray(), address.Id().ToArray());
        buffer = buffer.Slice(1);
        Assert.NotEqual(buffer.ToArray(), address.Id().ToArray());
    }
}
