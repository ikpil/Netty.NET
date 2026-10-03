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
using System.Security.Cryptography;

namespace Netty.NET.Common.Internal;

public static class ConstantTimeUtils
{
    /**
     * Compare two {@code int}s without leaking timing information.
     * <p>
     * The {@code int} return type is intentional and is designed to allow cascading of constant time operations:
     * <pre>
     *     int l1 = 1;
     *     int l2 = 1;
     *     int l3 = 1;
     *     int l4 = 500;
     *     bool equals = (equalsConstantTime(l1, l2) & equalsConstantTime(l3, l4)) != 0;
     * </pre>
     * @param x the first value.
     * @param y the second value.
     * @return {@code 0} if not equal. {@code 1} if equal.
     */
    public static int EqualsConstantTime(int x, int y)
    {
        int z = ~(x ^ y);
        z &= z >> 16;
        z &= z >> 8;
        z &= z >> 4;
        z &= z >> 2;
        z &= z >> 1;
        return z & 1;
    }

    /**
     * Compare two {@code longs}s without leaking timing information.
     * <p>
     * The {@code int} return type is intentional and is designed to allow cascading of constant time operations:
     * <pre>
     *     long l1 = 1;
     *     long l2 = 1;
     *     long l3 = 1;
     *     long l4 = 500;
     *     bool equals = (equalsConstantTime(l1, l2) & equalsConstantTime(l3, l4)) != 0;
     * </pre>
     * @param x the first value.
     * @param y the second value.
     * @return {@code 0} if not equal. {@code 1} if equal.
     */
    public static int EqualsConstantTime(long x, long y)
    {
        long z = ~(x ^ y);
        z &= z >> 32;
        z &= z >> 16;
        z &= z >> 8;
        z &= z >> 4;
        z &= z >> 2;
        z &= z >> 1;
        return (int)(z & 1);
    }

    /**
     * Compare two {@code byte} arrays for equality without leaking timing information.
     * For performance reasons no bounds checking on the parameters is performed.
     * <p>
     * The {@code int} return type is intentional and is designed to allow cascading of constant time operations:
     * <pre>
     *     byte[] s1 = new {1, 2, 3};
     *     byte[] s2 = new {1, 2, 3};
     *     byte[] s3 = new {1, 2, 3};
     *     byte[] s4 = new {4, 5, 6};
     *     bool equals = (equalsConstantTime(s1, 0, s2, 0, s1.length) &
     *                       equalsConstantTime(s3, 0, s4, 0, s3.length)) != 0;
     * </pre>
     * @param bytes1 the first byte array.
     * @param startPos1 the position (inclusive) to start comparing in {@code bytes1}.
     * @param bytes2 the second byte array.
     * @param startPos2 the position (inclusive) to start comparing in {@code bytes2}.
     * @param length the amount of bytes to compare. This is assumed to be validated as not going out of bounds
     * by the caller.
     * @return {@code 0} if not equal. {@code 1} if equal.
     */
    public static int EqualsConstantTime(byte[] bytes1, int startPos1,
        byte[] bytes2, int startPos2, int length)
    {
        ArgumentNullException.ThrowIfNull(bytes1);
        ArgumentNullException.ThrowIfNull(bytes2);
        if (length <= 0) return 1;
        // CLR adaptation: validate equal-length slices before calling the BCL's
        // fixed-time primitive. Its timing contract depends on length, not bytes.
        return CryptographicOperations.FixedTimeEquals(
            bytes1.AsSpan(startPos1, length), bytes2.AsSpan(startPos2, length)) ? 1 : 0;
    }

    /**
     * Compare two {@link ICharSequence} objects without leaking timing information.
     * <p>
     * The {@code int} return type is intentional and is designed to allow cascading of constant time operations:
     * <pre>
     *     string s1 = "foo";
     *     string s2 = "foo";
     *     string s3 = "foo";
     *     string s4 = "goo";
     *     bool equals = (equalsConstantTime(s1, s2) & equalsConstantTime(s3, s4)) != 0;
     * </pre>
     * @param s1 the first value.
     * @param s2 the second value.
     * @return {@code 0} if not equal. {@code 1} if equal.
     */
    public static int EqualsConstantTime(ICharSequence s1, ICharSequence s2)
    {
        if (s1.Count != s2.Count)
        {
            return 0;
        }

        // Benchmarking demonstrates that using an int to accumulate is faster than other data types.
        int c = 0;
        for (int i = 0; i < s1.Count; ++i)
        {
            c |= s1[i] ^ s2[i];
        }

        return EqualsConstantTime(c, 0);
    }
}
