/*
 * Copyright 2015 The Netty Project
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
using System.Linq;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.PlatformDependent;

namespace Netty.NET.Common.Tests.Internal;

public class PlatformDependentTest
{
    private static readonly Random r = new Random();

    interface IEqualityChecker
    {
        bool equals(byte[] bytes1, int startPos1, byte[] bytes2, int startPos2, int length);
    }

    class EqualityChecker : IEqualityChecker
    {
        private Func<byte[], int, byte[], int, int, bool> _invoker;

        public EqualityChecker(Func<byte[], int, byte[], int, int, bool> invoker)
        {
            _invoker = invoker;
        }

        public bool equals(byte[] bytes1, int startPos1, byte[] bytes2, int startPos2, int length)
        {
            return _invoker.Invoke(bytes1, startPos1, bytes2, startPos2, length);
        }
    }

    [Fact]
    public void testEqualsConsistentTime()
    {
        testEquals0(new EqualityChecker((byte[] bytes1, int startPos1, byte[] bytes2, int startPos2, int length) =>
        {
            return PlatformDependent.equalsConstantTime(bytes1, startPos1, bytes2, startPos2, length) != 0;
        }));
    }

    [Fact]
    public void testEquals()
    {
        testEquals0(new EqualityChecker((byte[] bytes1, int startPos1, byte[] bytes2, int startPos2, int length) =>
        {
            return PlatformDependent.equals(bytes1, startPos1, bytes2, startPos2, length);
        }));
    }

    [Fact]
    public void testIsZero()
    {
        byte[] bytes = new byte[100];
        Assert.True(PlatformDependent.isZero(bytes, 0, 0));
        Assert.True(PlatformDependent.isZero(bytes, 0, -1));
        Assert.True(PlatformDependent.isZero(bytes, 0, 100));
        Assert.True(PlatformDependent.isZero(bytes, 10, 90));
        bytes[10] = 1;
        Assert.True(PlatformDependent.isZero(bytes, 0, 10));
        Assert.False(PlatformDependent.isZero(bytes, 0, 11));
        Assert.False(PlatformDependent.isZero(bytes, 10, 1));
        Assert.True(PlatformDependent.isZero(bytes, 11, 89));
    }

    private static byte[] B(char[] c)
    {
        return c.Select(x => (byte)x).ToArray();
    }

    private static void testEquals0(IEqualityChecker equalsChecker)
    {
        byte[] bytes1 = B(['H', 'e', 'l', 'l', 'o', ' ', 'W', 'o', 'r', 'l', 'd']);
        byte[] bytes2 = B(['H', 'e', 'l', 'l', 'o', ' ', 'W', 'o', 'r', 'l', 'd']);
        Assert.NotSame(bytes1, bytes2);
        Assert.True(equalsChecker.equals(bytes1, 0, bytes2, 0, bytes1.Length));
        Assert.True(equalsChecker.equals(bytes1, 2, bytes2, 2, bytes1.Length - 2));

        bytes1 = new byte[] { 1, 2, 3, 4, 5, 6 };
        bytes2 = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
        Assert.NotSame(bytes1, bytes2);
        Assert.False(equalsChecker.equals(bytes1, 0, bytes2, 1, bytes1.Length));
        Assert.True(equalsChecker.equals(bytes2, 0, bytes1, 0, bytes1.Length));

        bytes1 = new byte[] { 1, 2, 3, 4 };
        bytes2 = new byte[] { 1, 2, 3, 5 };
        Assert.False(equalsChecker.equals(bytes1, 0, bytes2, 0, bytes1.Length));
        Assert.True(equalsChecker.equals(bytes1, 0, bytes2, 0, 3));

        bytes1 = new byte[] { 1, 2, 3, 4 };
        bytes2 = new byte[] { 1, 3, 3, 4 };
        Assert.False(equalsChecker.equals(bytes1, 0, bytes2, 0, bytes1.Length));
        Assert.True(equalsChecker.equals(bytes1, 2, bytes2, 2, bytes1.Length - 2));

        bytes1 = new byte[0];
        bytes2 = new byte[0];
        Assert.NotSame(bytes1, bytes2);
        Assert.True(equalsChecker.equals(bytes1, 0, bytes2, 0, 0));

        bytes1 = new byte[100];
        bytes2 = new byte[100];
        for (int i = 0; i < 100; i++)
        {
            bytes1[i] = (byte)i;
            bytes2[i] = (byte)i;
        }

        Assert.True(equalsChecker.equals(bytes1, 0, bytes2, 0, bytes1.Length));
        bytes1[50] = 0;
        Assert.False(equalsChecker.equals(bytes1, 0, bytes2, 0, bytes1.Length));
        Assert.True(equalsChecker.equals(bytes1, 51, bytes2, 51, bytes1.Length - 51));
        Assert.True(equalsChecker.equals(bytes1, 0, bytes2, 0, 50));

        bytes1 = new byte[] { 1, 2, 3, 4, 5 };
        bytes2 = new byte[] { 3, 4, 5 };
        Assert.False(equalsChecker.equals(bytes1, 0, bytes2, 0, bytes2.Length));
        Assert.True(equalsChecker.equals(bytes1, 2, bytes2, 0, bytes2.Length));
        Assert.True(equalsChecker.equals(bytes2, 0, bytes1, 2, bytes2.Length));

        for (int i = 0; i < 1000; ++i)
        {
            bytes1 = new byte[i];
            r.nextBytes(bytes1);
            bytes2 = bytes1.ToArray();
            Assert.True(equalsChecker.equals(bytes1, 0, bytes2, 0, bytes1.Length));
        }

        Assert.True(equalsChecker.equals(bytes1, 0, bytes2, 0, 0));
        Assert.True(equalsChecker.equals(bytes1, 0, bytes2, 0, -1));
    }

    private static char randomCharInByteRange()
    {
        return (char)r.Next(255 + 1);
    }

    [Fact]
    public void testHashCodeAscii()
    {
        for (int i = 0; i < 1000; ++i)
        {
            // byte[] and char[] need to be initialized such that there values are within valid "ascii" range
            byte[] bytes = new byte[i];
            char[] bytesChar = new char[i];
            for (int j = 0; j < bytesChar.Length; ++j)
            {
                bytesChar[j] = randomCharInByteRange();
                bytes[j] = (byte)(bytesChar[j] & 0xff);
            }

            string str = new string(bytesChar);
            Assert.Equal(hashCodeAsciiSafe(bytes, 0, bytes.Length),
                hashCodeAscii(bytes, 0, bytes.Length),
                "length=" + i);
            Assert.Equal(hashCodeAscii(bytes, 0, bytes.Length),
                hashCodeAscii(str),
                "length=" + i);
        }
    }

    [Fact]
    public void testAllocateWithCapacity0()
    {
        if (!PlatformDependent.hasDirectBufferNoCleanerConstructor())
            return;

        ByteBuffer buffer = PlatformDependent.allocateDirectNoCleaner(0);
        Assert.NotEqual(0, PlatformDependent.directBufferAddress(buffer));
        Assert.Equal(0, buffer.capacity());
        PlatformDependent.freeDirectNoCleaner(buffer);
    }

    @EnabledForJreRange(min = JRE.JAVA_25)

    [Fact]
    void java25MustHaveCleanerImplAvailable()
    {
        Assert.True(CleanerJava25.isSupported(),
            "The CleanerJava25 implementation must be supported on Java 25+");
        // Note: we're not testing on `PlatformDependent.directBufferPreferred()` because some builds
        // might intentionally disable it, in order to exercise those code paths.
    }
}