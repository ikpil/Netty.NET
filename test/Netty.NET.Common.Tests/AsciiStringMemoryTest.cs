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
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests;/**
 * Test the underlying memory methods for the {@link AsciiString} class.
 */
public class AsciiStringMemoryTest {
    private byte[] a;
    private byte[] b;
    private int aOffset = 22;
    private int bOffset = 53;
    private int length = 100;
    private AsciiString aAsciiString;
    private AsciiString bAsciiString;
    private readonly Random r = new Random();

    @BeforeEach
    public void setup() {
        a = new byte[128];
        b = new byte[256];
        r.nextBytes(a);
        r.nextBytes(b);
        aOffset = 22;
        bOffset = 53;
        length = 100;
        System.arraycopy(a, aOffset, b, bOffset, length);
        aAsciiString = new AsciiString(a, aOffset, length, false);
        bAsciiString = new AsciiString(b, bOffset, length, false);
    }

    [Fact]
    public void testSharedMemory() {
        ++a[aOffset];
        AsciiString aAsciiString1 = new AsciiString(a, aOffset, length, true);
        AsciiString aAsciiString2 = new AsciiString(a, aOffset, length, false);
        Assert.Equal(aAsciiString, aAsciiString1);
        Assert.Equal(aAsciiString, aAsciiString2);
        for (int i = aOffset; i < length; ++i) {
            Assert.Equal(a[i], aAsciiString.byteAt(i - aOffset));
        }
    }

    [Fact]
    public void testNotSharedMemory() {
        AsciiString aAsciiString1 = new AsciiString(a, aOffset, length, true);
        ++a[aOffset];
        Assert.NotEqual(aAsciiString, aAsciiString1);
        int i = aOffset;
        Assert.NotEqual(a[i], aAsciiString1.byteAt(i - aOffset));
        ++i;
        for (; i < length; ++i) {
            Assert.Equal(a[i], aAsciiString1.byteAt(i - aOffset));
        }
    }

    [Fact]
    public void forEachTest() {
        final AtomicReference<Integer> aCount = new AtomicReference<Integer>(0);
        final AtomicReference<Integer> bCount = new AtomicReference<Integer>(0);
        aAsciiString.forEachByte(new IByteProcessor() {
            int i;
            @Override
            public bool process(byte value) {
                Assert.Equal(value, bAsciiString.byteAt(i++), "failed at index: " + i);
                aCount.set(aCount.get() + 1);
                return true;
            }
        });
        bAsciiString.forEachByte(new IByteProcessor() {
            int i;
            @Override
            public bool process(byte value) {
                Assert.Equal(value, aAsciiString.byteAt(i++), "failed at index: " + i);
                bCount.set(bCount.get() + 1);
                return true;
            }
        });
        Assert.Equal(aAsciiString.length(), aCount.get().intValue());
        Assert.Equal(bAsciiString.length(), bCount.get().intValue());
    }

    [Fact]
    public void forEachWithIndexEndTest() {
        Assert.NotEqual(-1, aAsciiString.forEachByte(aAsciiString.length() - 1,
                1, new IndexOfProcessor(aAsciiString.byteAt(aAsciiString.length() - 1))));
    }

    [Fact]
    public void forEachWithIndexBeginTest() {
        Assert.NotEqual(-1, aAsciiString.forEachByte(0,
                1, new IndexOfProcessor(aAsciiString.byteAt(0))));
    }

    [Fact]
    public void forEachDescTest() {
        final AtomicReference<Integer> aCount = new AtomicReference<Integer>(0);
        final AtomicReference<Integer> bCount = new AtomicReference<Integer>(0);
        aAsciiString.forEachByteDesc(new IByteProcessor() {
            int i = 1;
            @Override
            public bool process(byte value) {
                Assert.Equal(value, bAsciiString.byteAt(bAsciiString.length() - (i++)), "failed at index: " + i);
                aCount.set(aCount.get() + 1);
                return true;
            }
        });
        bAsciiString.forEachByteDesc(new IByteProcessor() {
            int i = 1;
            @Override
            public bool process(byte value) {
                Assert.Equal(value, aAsciiString.byteAt(aAsciiString.length() - (i++)), "failed at index: " + i);
                bCount.set(bCount.get() + 1);
                return true;
            }
        });
        Assert.Equal(aAsciiString.length(), aCount.get().intValue());
        Assert.Equal(bAsciiString.length(), bCount.get().intValue());
    }

    [Fact]
    public void forEachDescWithIndexEndTest() {
        Assert.NotEqual(-1, bAsciiString.forEachByteDesc(bAsciiString.length() - 1,
                1, new IndexOfProcessor(bAsciiString.byteAt(bAsciiString.length() - 1))));
    }

    [Fact]
    public void forEachDescWithIndexBeginTest() {
        Assert.NotEqual(-1, bAsciiString.forEachByteDesc(0,
                1, new IndexOfProcessor(bAsciiString.byteAt(0))));
    }

    [Fact]
    public void subSequenceTest() {
        final int start = 12;
        final int end = aAsciiString.length();
        AsciiString aSubSequence = aAsciiString.subSequence(start, end, false);
        AsciiString bSubSequence = bAsciiString.subSequence(start, end, true);
        Assert.Equal(aSubSequence, bSubSequence);
        Assert.Equal(aSubSequence.hashCode(), bSubSequence.hashCode());
    }

    [Fact]
    public void copyTest() {
        byte[] aCopy = new byte[aAsciiString.length()];
        aAsciiString.copy(0, aCopy, 0, aCopy.length);
        AsciiString aAsciiStringCopy = new AsciiString(aCopy, false);
        Assert.Equal(aAsciiString, aAsciiStringCopy);
    }
}
