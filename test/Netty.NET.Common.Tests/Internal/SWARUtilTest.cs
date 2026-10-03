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

using System;
using System.Diagnostics;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

public class SWARUtilTest
{
    private readonly Random random = new Random();

    [Fact]
    void ContainsUpperCaseLong()
    {
        // given
        byte[] asciiTable = GetExtendedAsciiTable();
        ShuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(long))
        {
            long value = GetLong(asciiTable, idx);
            bool actual = SWARUtil.ContainsUpperCase(value);
            bool expected = false;
            for (int i = 0; i < sizeof(long); i++)
            {
                expected |= (asciiTable[idx + i] >= 65 && asciiTable[idx + i] <= 90);
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void ContainsUpperCaseInt()
    {
        // given
        byte[] asciiTable = GetExtendedAsciiTable();
        ShuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(int))
        {
            int value = GetInt(asciiTable, idx);
            bool containsUpperCase = SWARUtil.ContainsUpperCase(value);
            bool expectedContainsUpperCase = false;
            for (int i = 0; i < sizeof(int); i++)
            {
                expectedContainsUpperCase |= (asciiTable[idx + i] >= 65 && asciiTable[idx + i] <= 90);
            }

            // then
            Assert.Equal(expectedContainsUpperCase, containsUpperCase);
        }
    }

    [Fact]
    void ContainsLowerCaseLong()
    {
        // given
        byte[] asciiTable = GetExtendedAsciiTable();
        ShuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(long))
        {
            long value = GetLong(asciiTable, idx);
            bool actual = SWARUtil.ContainsLowerCase(value);
            bool expected = false;
            for (int i = 0; i < sizeof(long); i++)
            {
                expected |= (asciiTable[idx + i] >= 97 && asciiTable[idx + i] <= 122);
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void ContainsLowerCaseInt()
    {
        // given
        byte[] asciiTable = GetExtendedAsciiTable();
        ShuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(int))
        {
            int value = GetInt(asciiTable, idx);
            bool actual = SWARUtil.ContainsLowerCase(value);
            bool expected = false;
            for (int i = 0; i < sizeof(int); i++)
            {
                expected |= (asciiTable[idx + i] >= 97 && asciiTable[idx + i] <= 122);
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void ToUpperCaseLong()
    {
        // given
        byte[] asciiTable = GetExtendedAsciiTable();
        ShuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(long))
        {
            long value = GetLong(asciiTable, idx);
            long actual = SWARUtil.ToUpperCase(value);
            long expected = 0L;
            for (int i = 0; i < sizeof(long); i++)
            {
                byte b = AsciiStringUtil.ToUpperCase(asciiTable[idx + i]);
                expected |= (long)((b & 0xff)) << (56 - (sizeof(long) * i));
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void ToUpperCaseInt()
    {
        // given
        byte[] asciiTable = GetExtendedAsciiTable();
        ShuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(int))
        {
            int value = GetInt(asciiTable, idx);
            int actual = SWARUtil.ToUpperCase(value);
            int expected = 0;
            for (int i = 0; i < sizeof(int); i++)
            {
                byte b = AsciiStringUtil.ToUpperCase(asciiTable[idx + i]);
                expected |= (b & 0xff) << (24 - (8 * i));
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void ToLowerCaseLong()
    {
        // given
        byte[] asciiTable = GetExtendedAsciiTable();
        ShuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(long))
        {
            long value = GetLong(asciiTable, idx);
            long actual = SWARUtil.ToLowerCase(value);
            long expected = 0L;
            for (int i = 0; i < sizeof(long); i++)
            {
                byte b = AsciiStringUtil.ToLowerCase(asciiTable[idx + i]);
                expected |= (long)((b & 0xff)) << (56 - (8 * i));
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void ToLowerCaseInt()
    {
        // given
        byte[] asciiTable = GetExtendedAsciiTable();
        ShuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(int))
        {
            int value = GetInt(asciiTable, idx);
            int actual = SWARUtil.ToLowerCase(value);
            int expected = 0;
            for (int i = 0; i < sizeof(int); i++)
            {
                byte b = AsciiStringUtil.ToLowerCase(asciiTable[idx + i]);
                expected |= (b & 0xff) << (24 - (8 * i));
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    private static void ShuffleArray(byte[] array, Random random)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int index = random.Next(i + 1);
            byte tmp = array[index];
            array[index] = array[i];
            array[i] = tmp;
        }
    }

    private static byte[] GetExtendedAsciiTable()
    {
        byte[] table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            table[i] = (byte)i;
        }

        return table;
    }

    private static long GetLong(byte[] bytes, int idx)
    {
        Debug.Assert(idx >= 0 && bytes.Length >= idx + 8);
        return (long)bytes[idx] << 56 |
               ((long)bytes[idx + 1] & 0xff) << 48 |
               ((long)bytes[idx + 2] & 0xff) << 40 |
               ((long)bytes[idx + 3] & 0xff) << 32 |
               ((long)bytes[idx + 4] & 0xff) << 24 |
               ((long)bytes[idx + 5] & 0xff) << 16 |
               ((long)bytes[idx + 6] & 0xff) << 8 |
               (long)bytes[idx + 7] & 0xff;
    }

    private static int GetInt(byte[] bytes, int idx)
    {
        Debug.Assert(idx >= 0 && bytes.Length >= idx + 4);
        return bytes[idx] << 24 |
               (bytes[idx + 1] & 0xff) << 16 |
               (bytes[idx + 2] & 0xff) << 8 |
               bytes[idx + 3] & 0xff;
    }
}
