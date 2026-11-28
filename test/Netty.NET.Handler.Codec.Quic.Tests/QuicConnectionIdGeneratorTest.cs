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

// CLR: QuicConnectionIdGenerator is IQuicConnectionIdGenerator and java.nio.ByteBuffer is ReadOnlyMemory<byte> :
// remaining() is Length, ByteBuffer.wrap(bytes) the memory over the array, and ByteBuffer equality (the remaining
// bytes) compares the bytes as arrays. Quiche.QUICHE_MAX_CONN_ID_LEN is Quic.MAX_CONN_ID_LEN (20),
// Integer.MAX_VALUE int.MaxValue and ThreadLocalRandom.current() Random.Shared. assertThrows(C.class,...) accepts
// subclasses of C, so it is Assert.ThrowsAny; IllegalArgumentException is ArgumentException and
// UnsupportedOperationException NotSupportedException. newId(null, 10) fails with the NullPointerException of the
// null buffer; a ReadOnlyMemory<byte> cannot be null, and the null array converts to an empty memory, which the
// generator rejects with ArgumentException (checkPositive of the input length), so the call still fails, with the
// argument exception of the CLR.
public class QuicConnectionIdGeneratorTest : AbstractQuicTest
{

    [Fact]
    public void TestRandomness()
    {
        IQuicConnectionIdGenerator idGenerator = IQuicConnectionIdGenerator.RandomGenerator();
        ReadOnlyMemory<byte> id = idGenerator.NewId(Quic.MAX_CONN_ID_LEN);
        ReadOnlyMemory<byte> id2 = idGenerator.NewId(Quic.MAX_CONN_ID_LEN);
        Assert.True(id.Length > 0);
        Assert.True(id2.Length > 0);
        Assert.NotEqual(id.ToArray(), id2.ToArray());

        id = idGenerator.NewId(10);
        id2 = idGenerator.NewId(10);
        Assert.Equal(10, id.Length);
        Assert.Equal(10, id2.Length);
        Assert.NotEqual(id.ToArray(), id2.ToArray());

        byte[] input = new byte[1024];
        Random.Shared.NextBytes(input);
        id = idGenerator.NewId(new ReadOnlyMemory<byte>(input), 10);
        id2 = idGenerator.NewId(new ReadOnlyMemory<byte>(input), 10);
        Assert.Equal(10, id.Length);
        Assert.Equal(10, id2.Length);
        Assert.NotEqual(id.ToArray(), id2.ToArray());
    }

    [Fact]
    public void TestThrowsIfInputTooBig()
    {
        IQuicConnectionIdGenerator idGenerator = IQuicConnectionIdGenerator.RandomGenerator();
        Assert.ThrowsAny<ArgumentException>(() => idGenerator.NewId(int.MaxValue));
    }

    [Fact]
    public void TestThrowsIfInputTooBig2()
    {
        IQuicConnectionIdGenerator idGenerator = IQuicConnectionIdGenerator.RandomGenerator();
        Assert.ThrowsAny<ArgumentException>(() =>
                idGenerator.NewId(new ReadOnlyMemory<byte>(new byte[8]), int.MaxValue));
    }

    [Fact]
    public void TestSignIdGenerator()
    {
        IQuicConnectionIdGenerator idGenerator = IQuicConnectionIdGenerator.SignGenerator();

        byte[] input = new byte[1024];
        byte[] input2 = new byte[1024];
        Random.Shared.NextBytes(input);
        Random.Shared.NextBytes(input2);
        ReadOnlyMemory<byte> id = idGenerator.NewId(new ReadOnlyMemory<byte>(input), 10);
        ReadOnlyMemory<byte> id2 = idGenerator.NewId(new ReadOnlyMemory<byte>(input), 10);
        ReadOnlyMemory<byte> id3 = idGenerator.NewId(new ReadOnlyMemory<byte>(input2), 10);
        Assert.Equal(10, id.Length);
        Assert.Equal(10, id2.Length);
        Assert.Equal(10, id3.Length);
        Assert.Equal(id.ToArray(), id2.ToArray());
        Assert.NotEqual(id.ToArray(), id3.ToArray());

        Assert.ThrowsAny<NotSupportedException>(() => idGenerator.NewId(10));
        Assert.ThrowsAny<ArgumentException>(() => idGenerator.NewId((byte[]) null, 10));
        Assert.ThrowsAny<ArgumentException>(() => idGenerator.NewId(new ReadOnlyMemory<byte>(new byte[0]), 10));
        Assert.ThrowsAny<ArgumentException>(() =>
                idGenerator.NewId(new ReadOnlyMemory<byte>(input), int.MaxValue));
    }
}
