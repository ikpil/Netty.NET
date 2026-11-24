/*
 * Copyright 2013 The Netty Project
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
using System.Globalization;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: the inherited tests of AbstractIntegrationTest run for this class as JUnit runs them; createEncoder() and
// createDecoder() implement its abstract members. The signed byte[] literal of test1002 is an sbyte array that Bytes()
// reinterprets as System.Byte values, as the codec-dns tests do (test/Netty.NET.Handler.Codec.Dns.Tests/DnsResponseTest.cs).
// catch (Throwable t) { ...; throw t; } is catch (Exception) { ...; throw; }, and System.out.println is
// Console.WriteLine, which "Writes the specified string value, followed by the current line terminator, to the standard
// output stream" (https://learn.microsoft.com/en-us/dotnet/api/system.console.writeline?view=net-10.0).
public class SnappyIntegrationTest : AbstractIntegrationTest
{

    /**
     * The number of random regression tests run by testRandom() runs.  Whenever testRandom() finds the case that
     * the snappy codec can't encode/decode, it will print the generated source code of the offending test case.
     * You can always reproduce the problem using it rather than relying on testRandom().
     *
     * The default is 1, but you can increase it to increase the chance of finding any unusual cases.
     **/
    private const int RANDOM_RUNS = 1;

    protected override EmbeddedChannel CreateEncoder()
    {
        return new EmbeddedChannel(new SnappyFrameEncoder());
    }

    protected override EmbeddedChannel CreateDecoder()
    {
        return new EmbeddedChannel(new SnappyFrameDecoder());
    }

    private static byte[] Bytes(sbyte[] p)
    {
        return Array.ConvertAll(p, b => unchecked((byte) b));
    }

    [Fact]
    public void Test1002()
    {
        // Data from https://github.com/netty/netty/issues/1002
        byte[] data = Bytes(new sbyte[] {
                11,    0,    0,    0,    0,    0,   16,   65,   96,  119,  -22,   79,  -43,   76,  -75,  -93,
                11,  104,   96,  -99,  126,  -98,   27,  -36,   40,  117,  -65,   -3,  -57,  -83,  -58,    7,
                114,  -14,   68, -122,  124,   88,  118,   54,   45,  -26,  117,   13,  -45,   -9,   60,  -73,
                -53,  -44,   53,   68,  -77,  -71,  109,   43,  -38,   59,  100,  -12,  -87,   44, -106,  123,
                -107,   38,   13, -117,  -23,  -49,   29,   21,   26,   66,    1,   -1,   -1,   -1,   -1,   -1,
                -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
                -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
                -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
                -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   66,    0, -104,  -49,
                16, -120,   22,    8,  -52,  -54, -102,  -52, -119, -124,  -92,  -71,  101, -120,  -52,  -48,
                45,  -26,  -24,   26,   41,  -13,   36,   64,  -47,   15, -124,   -7,  -16,   91,   96,    0,
                -93,  -42,  101,   20,  -74,   39, -124,   35,   43,  -49,  -21,  -92,  -20,  -41,   79,   41,
                110, -105,   42,  -96,   90,   -9, -100,  -22,  -62,   91,    2,   35,  113,  117,  -71,   66,
                1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
                -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
                -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
                -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
                -1,   -1
        });
        TestIdentity(data, true);
    }

    // These tests were found using testRandom() with large RANDOM_RUNS.

    // Tests that copies do not attempt to overrun into a previous frame chunk
    [Fact]
    public void Test5323211032315942961()
    {
        TestWithSeed(5323211032315942961L);
    }

    // Tests that when generating the hash lookup table for finding copies, we
    // do not exceed the length of the input when there are no copies
    [Fact]
    public void Test7088170877360183401()
    {
        TestWithSeed(7088170877360183401L);
    }

    // CLR: rand.nextLong() covers every long value, so the source skips about half of the runs for a negative seed;
    // Random.NextInt64() returns only values "greater than or equal to 0", while NextInt64(long.MinValue, long.MaxValue)
    // returns "A 64-bit signed integer greater than or equal to minValue and less than maxValue"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.random.nextint64?view=net-10.0), which keeps the source's skip.
    [Fact]
    public void TestRandom()
    {
        for (int i = 0; i < RANDOM_RUNS; i++)
        {
            long seed = rand.NextInt64(long.MinValue, long.MaxValue);
            if (seed < 0)
            {
                // Use only positive seed to get prettier test name. :-)
                continue;
            }

            try
            {
                TestWithSeed(seed);
            }
            catch (Exception)
            {
                Console.WriteLine("Failed with random seed " + seed.ToString(CultureInfo.InvariantCulture) +
                        ". Here is a test for it:\n");
                PrintSeedAsTest(seed);
                throw;
            }
        }
    }

    // CLR: SplittableRandom is not ported (see AbstractIntegrationTest). The seeds name the data that once failed, but
    // that data came from java.util.Random(seed).nextBytes: upstream commit 9a469345987dc731ef67d2a61ceff8ccbe4067ad
    // (#16808, in the pinned E:\workspace\netty history) switched this method to SplittableRandom, so the Java test no
    // longer reproduces those bytes either. A System.Random created "using the specified seed value"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.random.-ctor?view=net-10.0) with the low 32 bits of the seed
    // keeps the data deterministic per test; NextBytes fills it.
    private void TestWithSeed(long seed)
    {
        byte[] data = new byte[16 * 1048576];
        new Random(unchecked((int) seed)).NextBytes(data);
        TestIdentity(data, true);
    }

    private static void PrintSeedAsTest(long l)
    {
        Console.WriteLine("@Test");
        Console.WriteLine("@Ignore");
        Console.WriteLine("public void test" + l.ToString(CultureInfo.InvariantCulture) + "(){");
        Console.WriteLine("    testWithSeed(" + l.ToString(CultureInfo.InvariantCulture) + "L);");
        Console.WriteLine("}");
    }
}
