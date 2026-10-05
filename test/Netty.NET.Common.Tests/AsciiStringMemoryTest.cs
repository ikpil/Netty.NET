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
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests;

/**
 * Test the underlying memory methods for the {@link AsciiString} class.
 */
public class AsciiStringMemoryTest
{
    private byte[] a;
    private byte[] b;
    private int aOffset = 22;
    private int bOffset = 53;
    private int length = 100;
    private AsciiString aAsciiString;
    private AsciiString bAsciiString;
    private readonly Random r = new Random();

    public AsciiStringMemoryTest()
    {
        a = new byte[128];
        b = new byte[256];
        r.NextBytes(a);
        r.NextBytes(b);
        aOffset = 22;
        bOffset = 53;
        length = 100;
        Arrays.Arraycopy(a, aOffset, b, bOffset, length);
        aAsciiString = new AsciiString(a, aOffset, length, false);
        bAsciiString = new AsciiString(b, bOffset, length, false);
    }

    [Fact]
    public void TestSharedMemory()
    {
        unchecked { ++a[aOffset]; }
        AsciiString aAsciiString1 = new AsciiString(a, aOffset, length, true);
        AsciiString aAsciiString2 = new AsciiString(a, aOffset, length, false);
        Assert.Equal(aAsciiString, aAsciiString1);
        Assert.Equal(aAsciiString, aAsciiString2);
        for (int i = aOffset; i < length; ++i)
        {
            Assert.Equal(a[i], aAsciiString.ByteAt(i - aOffset));
        }
    }

    [Fact]
    public void TestNotSharedMemory()
    {
        AsciiString aAsciiString1 = new AsciiString(a, aOffset, length, true);
        unchecked { ++a[aOffset]; }
        Assert.NotEqual(aAsciiString, aAsciiString1);
        int i = aOffset;
        Assert.NotEqual(a[i], aAsciiString1.ByteAt(i - aOffset));
        ++i;
        for (; i < length; ++i)
        {
            Assert.Equal(a[i], aAsciiString1.ByteAt(i - aOffset));
        }
    }

    [Fact]
    public void TestByteIncrementWrapsWithoutChangingSharedAndCopiedMemoryContracts()
    {
        a[aOffset] = byte.MaxValue;
        TestSharedMemory();
        a[aOffset] = byte.MaxValue;
        TestNotSharedMemory();
    }

    [Fact]
    public void ForEachTest()
    {
        int aCount = 0;
        int bCount = 0;
        int aIndex = 0;
        int bIndex = 0;
        aAsciiString.ForEachByte(value =>
        {
            Assert.Equal(value, bAsciiString.ByteAt(aIndex++), "failed at index: " + aIndex);
            ++aCount;
            return true;
        });


        bAsciiString.ForEachByte(value =>
        {
            Assert.Equal(value, aAsciiString.ByteAt(bIndex++), "failed at index: " + bIndex);
            ++bCount;
            return true;
        });
        Assert.Equal(aAsciiString.Length(), aCount);
        Assert.Equal(bAsciiString.Length(), bCount);
    }

    [Fact]
    public void ForEachWithIndexEndTest()
    {
        Assert.NotEqual(-1, aAsciiString.ForEachByte(aAsciiString.Length() - 1,
            1, value => value != aAsciiString.ByteAt(aAsciiString.Length() - 1)));
    }

    [Fact]
    public void ForEachWithIndexBeginTest()
    {
        Assert.NotEqual(-1, aAsciiString.ForEachByte(0,
            1, value => value != aAsciiString.ByteAt(0)));
    }

    [Fact]
    public void ForEachDescTest()
    {
        int aCount = 0;
        int bCount = 0;
        int aIndex = 1;
        int bIndex = 1;
        aAsciiString.ForEachByteDesc(value =>
        {
            Assert.Equal(value, bAsciiString.ByteAt(bAsciiString.Length() - (aIndex++)), "failed at index: " + aIndex);
            ++aCount;
            return true;
        });

        bAsciiString.ForEachByteDesc(value =>
        {
            Assert.Equal(value, aAsciiString.ByteAt(aAsciiString.Length() - (bIndex++)), "failed at index: " + bIndex);
            ++bCount;
            return true;
        });
        Assert.Equal(aAsciiString.Length(), aCount);
        Assert.Equal(bAsciiString.Length(), bCount);
    }

    [Fact]
    public void ForEachDescWithIndexEndTest()
    {
        Assert.NotEqual(-1, bAsciiString.ForEachByteDesc(bAsciiString.Length() - 1,
            1, value => value != bAsciiString.ByteAt(bAsciiString.Length() - 1)));
    }

    [Fact]
    public void ForEachDescWithIndexBeginTest()
    {
        Assert.NotEqual(-1, bAsciiString.ForEachByteDesc(0,
            1, value => value != bAsciiString.ByteAt(0)));
    }

    [Fact]
    public void SubSequenceTest()
    {
        int start = 12;
        int end = aAsciiString.Length();
        AsciiString aSubSequence = aAsciiString.SubSequence(start, end, false);
        AsciiString bSubSequence = bAsciiString.SubSequence(start, end, true);
        Assert.Equal(aSubSequence, bSubSequence);
        Assert.Equal(aSubSequence.GetHashCode(), bSubSequence.GetHashCode());
    }

    [Fact]
    public void CopyTest()
    {
        byte[] aCopy = new byte[aAsciiString.Length()];
        aAsciiString.Copy(0, aCopy, 0, aCopy.Length);
        AsciiString aAsciiStringCopy = new AsciiString(aCopy, false);
        Assert.Equal(aAsciiString, aAsciiStringCopy);
    }
}
