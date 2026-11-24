/*
 * Copyright 2019 The Netty Project
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
using System.IO.Hashing;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: owner decision 2026-11-23: java.util.zip.Checksum, CRC32 and Adler32 are System.IO.Hashing's
// NonCryptographicHashAlgorithm, Crc32 and Adler32, and the jpountz XXHash32 that the test uses as an independent
// reference is System.IO.Hashing.XxHash32 with the same seed. Crc32 is written System.IO.Hashing.Crc32 because
// codec-compression's own Crc32 (bzip2) is in an enclosing namespace. checksum.update(BYTE_ARRAY, off, len) is
// Append over the same array range, and checksum.getValue() of the reference algorithm is GetValue(checksum), which reads
// GetCurrentHashAsUInt32() of these types directly
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.crc32.getcurrenthashasuint32?view=net-10.0-pp,
// https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.adler32.getcurrenthashasuint32?view=net-10.0-pp,
// https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.xxhash32.getcurrenthashasuint32?view=net-10.0-pp) and
// does not go through ByteBufChecksum. The static @BeforeAll setUp() runs from the static constructor and is private
// (xUnit1013 asks public methods of a test class to be tests), and
// ThreadLocalRandom.current().nextBytes is Random.Shared.NextBytes, "a thread-safe Random instance"
// (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0).
public class ByteBufChecksumTest
{

    // CLR: Lz4Constants.DEFAULT_SEED (0x9747b28c) of the Lz4 family, which needs lz4-java and is not ported (owner
    // decision 2026-11-23); the value is repeated here. The literal is a uint in C#, so it is cast to the Java int.
    private const int DEFAULT_SEED = unchecked((int) 0x9747b28c);

    private static readonly byte[] BYTE_ARRAY = new byte[1024];

    static ByteBufChecksumTest()
    {
        SetUp();
    }

    private static void SetUp()
    {
        Random.Shared.NextBytes(BYTE_ARRAY);
    }

    [Fact]
    public void TestHeapByteBufUpdate()
    {
        TestUpdate(Unpooled.WrappedBuffer(BYTE_ARRAY));
    }

    [Fact]
    public void TestDirectByteBufUpdate()
    {
        ByteBuf buf = Unpooled.DirectBuffer(BYTE_ARRAY.Length);
        buf.WriteBytes(BYTE_ARRAY);
        TestUpdate(buf);
    }

    // CLR: the two Lz4XXHash32 lines of the source, testUpdate(xxHash32(DEFAULT_SEED), new Lz4XXHash32(DEFAULT_SEED), buf)
    // and testUpdate(xxHash32(DEFAULT_SEED), ByteBufChecksum.wrapChecksum(new Lz4XXHash32(DEFAULT_SEED)), buf), are not
    // ported: Lz4XXHash32 wraps lz4-java's XXHash32 and is library-not-ported (owner decision 2026-11-23). The xxHash32
    // reference against wrapChecksum(xxHash32) and the CRC32 and Adler32 checks are ported.
    private static void TestUpdate(ByteBuf buf)
    {
        try
        {
            // all variations of xxHash32: slow and naive, optimised, wrapped optimised;
            // the last two should be literally identical, but it's best to guard against
            // an accidental regression in ByteBufChecksum#wrapChecksum(Checksum)
            TestUpdate(XxHash32(DEFAULT_SEED), ByteBufChecksum.WrapChecksum(XxHash32(DEFAULT_SEED)), buf);

            // CRC32 and Adler32, special-cased to use ReflectiveByteBufChecksum
            TestUpdate(new System.IO.Hashing.Crc32(), ByteBufChecksum.WrapChecksum(new System.IO.Hashing.Crc32()), buf);
            TestUpdate(new Adler32(), ByteBufChecksum.WrapChecksum(new Adler32()), buf);
        }
        finally
        {
            buf.Release();
        }
    }

    private static void TestUpdate(NonCryptographicHashAlgorithm checksum, ByteBufChecksum wrapped, ByteBuf buf)
    {
        TestUpdate(checksum, wrapped, buf, 0, BYTE_ARRAY.Length);
        TestUpdate(checksum, wrapped, buf, 0, BYTE_ARRAY.Length - 1);
        TestUpdate(checksum, wrapped, buf, 1, BYTE_ARRAY.Length - 1);
        TestUpdate(checksum, wrapped, buf, 1, BYTE_ARRAY.Length - 2);
    }

    private static void TestUpdate(NonCryptographicHashAlgorithm checksum, ByteBufChecksum wrapped, ByteBuf buf, int off,
                                   int len)
    {
        checksum.Reset();
        wrapped.Reset();

        checksum.Append(new ReadOnlySpan<byte>(BYTE_ARRAY, off, len));
        wrapped.Update(buf, off, len);

        Assert.Equal(GetValue(checksum), wrapped.GetValue());
    }

    // CLR: the type is written System.IO.Hashing.XxHash32 because this method has the same name.
    private static NonCryptographicHashAlgorithm XxHash32(int seed)
    {
        return new System.IO.Hashing.XxHash32(seed);
    }

    // CLR: checksum.getValue() of the reference algorithm (see the class comment).
    private static long GetValue(NonCryptographicHashAlgorithm checksum)
    {
        switch (checksum)
        {
            case System.IO.Hashing.Crc32 crc32:
                return crc32.GetCurrentHashAsUInt32();
            case Adler32 adler32:
                return adler32.GetCurrentHashAsUInt32();
            case System.IO.Hashing.XxHash32 xxHash32:
                return xxHash32.GetCurrentHashAsUInt32();
            default:
                throw new NotSupportedException(checksum.GetType().FullName);
        }
    }
}
