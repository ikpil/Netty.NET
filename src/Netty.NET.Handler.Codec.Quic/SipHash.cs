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

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * <a href="https://www.aumasson.jp/siphash/siphash.pdf">Siphash implementation</a>.
 */
// CLR: java.nio.ByteBuffer is ReadOnlyMemory<byte> (see IQuicConnectionIdGenerator): it starts at the buffer's position,
// so position is 0 and the absolute get(index)/getLong(index) read the span at that index. A ByteBuffer has a byte
// order; macHash reverses a big-endian getLong so that every word is read little-endian, which is
// BinaryPrimitives.ReadInt64LittleEndian here (ReadOnlyMemory has no byte order). get(index) is the signed Java byte,
// widened with sign extension as in Java ((long)(sbyte)). Long.rotateLeft is long.RotateLeft, Long.BYTES sizeof(long),
// the switch fall-through goto case, assert Debug.Assert and IllegalArgumentException/IllegalStateException are
// ArgumentException/InvalidOperationException. The arithmetic wraps (unchecked), as Java's does.
internal sealed class SipHash
{

    internal const int SEED_LENGTH = 16;

    // Make this class allocation free as soon as its constructed.
    private readonly int compressionRounds;
    private readonly int finalizationRounds;

    // As specified in https://www.aumasson.jp/siphash/siphash.pdf
    private const long INITIAL_STATE_V0 = 0x736f6d6570736575L; // "somepseu"
    private const long INITIAL_STATE_V1 = 0x646f72616e646f6dL; // "dorandom"
    private const long INITIAL_STATE_V2 = 0x6c7967656e657261L; // "lygenera"
    private const long INITIAL_STATE_V3 = 0x7465646279746573L;  // "tedbytes"

    private readonly long initialStateV0;
    private readonly long initialStateV1;
    private readonly long initialStateV2;
    private readonly long initialStateV3;

    private long v0;
    private long v1;
    private long v2;
    private long v3;

    internal SipHash(int compressionRounds, int finalizationRounds, byte[] seed)
    {
        if (seed.Length != SEED_LENGTH)
        {
            throw new ArgumentException("seed must be of length " + SEED_LENGTH);
        }

        this.compressionRounds = ObjectUtil.CheckPositive(compressionRounds, "compressionRounds");
        this.finalizationRounds = ObjectUtil.CheckPositive(finalizationRounds, "finalizationRounds");

        // Wrap the seed to extract two longs that will be used to generate the initial state.
        // Use little-endian as in the paper.
        long k0 = BinaryPrimitives.ReadInt64LittleEndian(seed.AsSpan(0, sizeof(long)));
        long k1 = BinaryPrimitives.ReadInt64LittleEndian(seed.AsSpan(sizeof(long), sizeof(long)));

        initialStateV0 = INITIAL_STATE_V0 ^ k0;
        initialStateV1 = INITIAL_STATE_V1 ^ k1;
        initialStateV2 = INITIAL_STATE_V2 ^ k0;
        initialStateV3 = INITIAL_STATE_V3 ^ k1;
    }

    internal long MacHash(ReadOnlyMemory<byte> input)
    {
        ReadOnlySpan<byte> span = input.Span;
        v0 = initialStateV0;
        v1 = initialStateV1;
        v2 = initialStateV2;
        v3 = initialStateV3;
        int remaining = span.Length;
        int position = 0;
        int len = remaining - (remaining % sizeof(long));

        for (int offset = position; offset < len; offset +=  sizeof(long))
        {
            // We use little-endian as in the paper.
            long m = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset, sizeof(long)));
            v3 ^= m;

            for (int i = 0; i < compressionRounds; i++)
            {
                Sipround();
            }

            v0 ^= m;
        }

        // Get last bits.
        int left = remaining & (sizeof(long) - 1);
        long b = (long) remaining << 56;
        Debug.Assert(left < sizeof(long));

        switch (left)
        {
            case 7:
                b |= (long) (sbyte) span[position + len + 6] << 48;
                goto case 6;
            case 6:
                b |= (long) (sbyte) span[position + len + 5] << 40;
                goto case 5;
            case 5:
                b |= (long) (sbyte) span[position + len + 4] << 32;
                goto case 4;
            case 4:
                b |= (long) (sbyte) span[position + len + 3] << 24;
                goto case 3;
            case 3:
                b |= (long) (sbyte) span[position + len + 2] << 16;
                goto case 2;
            case 2:
                b |= (long) (sbyte) span[position + len + 1] << 8;
                goto case 1;
            case 1:
                b |= (long) (sbyte) span[position + len];
                break;
            case 0:
                break;
            default:
                throw new InvalidOperationException("Unexpected value: " + left);
        }

        v3 ^= b;

        for (int i = 0; i < compressionRounds; i++)
        {
            Sipround();
        }

        v0 ^= b;
        v2 ^= 0xFF;

        for (int i = 0; i < finalizationRounds; i++)
        {
            Sipround();
        }

        return v0 ^ v1 ^ v2 ^ v3;
    }

    private void Sipround()
    {
        unchecked
        {
            v0 += v1;
            v2 += v3;
            v1 = long.RotateLeft(v1, 13);
            v3 = long.RotateLeft(v3, 16);
            v1 ^= v0;
            v3 ^= v2;

            v0 = long.RotateLeft(v0, 32);

            v2 += v1;
            v0 += v3;
            v1 = long.RotateLeft(v1, 17);
            v3 = long.RotateLeft(v3, 21);
            v1 ^= v2;
            v3 ^= v0;

            v2 = long.RotateLeft(v2, 32);
        }
    }
}
