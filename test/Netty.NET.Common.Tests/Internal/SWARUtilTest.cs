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
    void containsUpperCaseLong()
    {
        // given
        byte[] asciiTable = getExtendedAsciiTable();
        shuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(long))
        {
            long value = getLong(asciiTable, idx);
            bool actual = SWARUtil.containsUpperCase(value);
            bool expected = false;
            for (int i = 0; i < sizeof(long); i++)
            {
                expected |= char.IsUpper((char)asciiTable[idx + i]);
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void containsUpperCaseInt()
    {
        // given
        byte[] asciiTable = getExtendedAsciiTable();
        shuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(int))
        {
            int value = getInt(asciiTable, idx);
            bool containsUpperCase = SWARUtil.containsUpperCase(value);
            bool expectedContainsUpperCase = false;
            for (int i = 0; i < sizeof(int); i++)
            {
                expectedContainsUpperCase |= char.IsUpper((char)asciiTable[idx + i]);
            }

            // then
            Assert.Equal(expectedContainsUpperCase, containsUpperCase);
        }
    }

    [Fact]
    void containsLowerCaseLong()
    {
        // given
        byte[] asciiTable = getExtendedAsciiTable();
        shuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(long))
        {
            long value = getLong(asciiTable, idx);
            bool actual = SWARUtil.containsLowerCase(value);
            bool expected = false;
            for (int i = 0; i < sizeof(long); i++)
            {
                expected |= char.IsLower((char)asciiTable[idx + i]);
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void containsLowerCaseInt()
    {
        // given
        byte[] asciiTable = getExtendedAsciiTable();
        shuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(int))
        {
            int value = getInt(asciiTable, idx);
            bool actual = SWARUtil.containsLowerCase(value);
            bool expected = false;
            for (int i = 0; i < sizeof(int); i++)
            {
                expected |= char.IsLower((char)asciiTable[idx + i]);
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void toUpperCaseLong()
    {
        // given
        byte[] asciiTable = getExtendedAsciiTable();
        shuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(long))
        {
            long value = getLong(asciiTable, idx);
            long actual = SWARUtil.toUpperCase(value);
            long expected = 0L;
            for (int i = 0; i < sizeof(long); i++)
            {
                byte b = (byte)char.ToUpperInvariant((char)asciiTable[idx + i]);
                expected |= (long)((b & 0xff)) << (56 - (sizeof(long) * i));
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void toUpperCaseInt()
    {
        // given
        byte[] asciiTable = getExtendedAsciiTable();
        shuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(int))
        {
            int value = getInt(asciiTable, idx);
            int actual = SWARUtil.toUpperCase(value);
            int expected = 0;
            for (int i = 0; i < sizeof(int); i++)
            {
                byte b = (byte)char.ToUpperInvariant((char)asciiTable[idx + i]);
                expected |= (b & 0xff) << (24 - (sizeof(byte) * i));
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void toLowerCaseLong()
    {
        // given
        byte[] asciiTable = getExtendedAsciiTable();
        shuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(long))
        {
            long value = getLong(asciiTable, idx);
            long actual = SWARUtil.toLowerCase(value);
            long expected = 0L;
            for (int i = 0; i < sizeof(long); i++)
            {
                byte b = (byte)char.ToLowerInvariant((char)asciiTable[idx + i]);
                expected |= (long)((b & 0xff)) << (56 - (sizeof(byte) * i));
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    void toLowerCaseInt()
    {
        // given
        byte[] asciiTable = getExtendedAsciiTable();
        shuffleArray(asciiTable, random);

        // when
        for (int idx = 0; idx < asciiTable.Length; idx += sizeof(int))
        {
            int value = getInt(asciiTable, idx);
            int actual = SWARUtil.toLowerCase(value);
            int expected = 0;
            for (int i = 0; i < sizeof(int); i++)
            {
                byte b = (byte)char.ToLowerInvariant((char)asciiTable[idx + i]);
                expected |= (b & 0xff) << (24 - (sizeof(byte) * i));
            }

            // then
            Assert.Equal(expected, actual);
        }
    }

    private static void shuffleArray(byte[] array, Random random)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int index = random.Next(i + 1);
            byte tmp = array[index];
            array[index] = array[i];
            array[i] = tmp;
        }
    }

    private static byte[] getExtendedAsciiTable()
    {
        byte[] table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            table[i] = (byte)i;
        }

        return table;
    }

    private static long getLong(byte[] bytes, int idx)
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

    private static int getInt(byte[] bytes, int idx)
    {
        Debug.Assert(idx >= 0 && bytes.Length >= idx + 4);
        return bytes[idx] << 24 |
               (bytes[idx + 1] & 0xff) << 16 |
               (bytes[idx + 2] & 0xff) << 8 |
               bytes[idx + 3] & 0xff;
    }
}