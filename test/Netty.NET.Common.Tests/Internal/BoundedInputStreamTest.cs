/*
 * Copyright 2024 The Netty Project
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

using System.IO;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

public class BoundedInputStreamTest
{
    public static System.Collections.Generic.IEnumerable<object[]> Repetitions()
    {
        for (int i = 0; i < 50; i++) yield return new object[] { i };
    }

    [Theory]
    [MemberData(nameof(Repetitions))]
    public void testBoundEnforced(int repetition)
    {
        byte[] bytes = new byte[64];
        ThreadLocalRandom.current().nextBytes(bytes);
        using BoundedInputStream reader = new BoundedInputStream(new MemoryStream(bytes), bytes.Length - 1);
        Assert.Equal(bytes[0], (byte)reader.ReadByte());

        Assert.Throws<IOException>(() =>
        {
            int max = bytes.Length;
            do
            {
                int result = reader.Read(new byte[max], 0, max);
                Assert.True(result > 0, "Unexpected EOF before the bound was exceeded");
                max -= result;
            } while (max > 0);
        });
    }

    [Fact]
    public void testBoundEnforced256()
    {
        byte[] bytes = new byte[256];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)i;
        }


        using BoundedInputStream reader = new BoundedInputStream(new MemoryStream(bytes), bytes.Length - 1);
        foreach (byte expectedByte in bytes)
        {
            Assert.Equal(expectedByte, (byte)reader.ReadByte());
        }

        Assert.Throws<IOException>(() => reader.ReadByte());
        Assert.Throws<IOException>(() => reader.Read(new byte[1], 0, 1));
    }


    [Theory]
    [MemberData(nameof(Repetitions))]
    public void testBigReadsPermittedIfUnderlyingStreamIsSmall(int repetition)
    {
        byte[] bytes = new byte[64];
        ThreadLocalRandom.current().nextBytes(bytes);

        using BoundedInputStream reader = new BoundedInputStream(new MemoryStream(bytes), 8192);
        byte[] buffer = new byte[10000];
        Assert.Equal(reader.Read(buffer, 0, 10000), 64);
        Assert.Equal(bytes, Arrays.copyOfRange(buffer, 0, 64));
    }
}
