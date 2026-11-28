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

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: java.util.SplittableRandom is not ported; an unseeded System.Random replaces the unseeded generators, as the
// Buffer and Transport test ports do (test/Netty.NET.Buffer.Tests/PooledByteBufAllocatorTest.cs). The tests draw
// nextInt(bound) as Next(maxValue), whose "specified maximum value is exclusive"
// (https://learn.microsoft.com/en-us/dotnet/api/system.random?view=net-10.0), and the data only needs to be random, not
// a particular sequence. (byte) rng.nextInt() keeps the low eight bits of a random int; Next() is non-negative, and its
// low eight bits are still uniformly distributed. The conditional casts its 0 to byte, because C# gives
// i % 4 != 0 ? 0 : (byte) ... the type int where Java gives it the type byte. The static initializer is the static
// constructor.
public abstract class AbstractCompressionTest
{

    protected readonly Random rand = new Random();

    protected static readonly byte[] BYTES_SMALL = new byte[256];
    protected static readonly byte[] BYTES_LARGE = new byte[256 * 1024];

    static AbstractCompressionTest()
    {
        FillArrayWithCompressibleData(BYTES_SMALL);
        FillArrayWithCompressibleData(BYTES_LARGE);
    }

    private static void FillArrayWithCompressibleData(byte[] array)
    {
        Random rng = new Random();

        for (int i = 0; i < array.Length; i++)
        {
            array[i] = i % 4 != 0 ? (byte) 0 : (byte) rng.Next();
        }
    }
}
