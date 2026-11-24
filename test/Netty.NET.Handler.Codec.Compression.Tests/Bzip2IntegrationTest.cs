/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: the inherited AbstractIntegrationTest tests run for this class (createEncoder and createDecoder are the overrides
// CreateEncoder and CreateDecoder). SplittableRandom is not ported (as in AbstractIntegrationTest and the Transport
// testsuite ports, test/Netty.NET.Transport.Tests/SocketEchoSourceTest.cs): new SplittableRandom(rand.nextLong()) is a
// Random seeded from the shared rand, and PlatformDependent.splittableRandomNextBytes(...) is its NextBytes. The tests
// need random data of the given sizes (500, 1100 and 2300 bytes give 3, 4 and 5 Huffman tables), not particular bytes.
public class Bzip2IntegrationTest : AbstractIntegrationTest
{

    protected override EmbeddedChannel CreateEncoder()
    {
        return new EmbeddedChannel(new Bzip2Encoder());
    }

    protected override EmbeddedChannel CreateDecoder()
    {
        return new EmbeddedChannel(new Bzip2Decoder());
    }

    [Fact]
    public void Test3Tables()
    {
        byte[] data = new byte[500];
        new Random(rand.Next()).NextBytes(data);
        TestIdentity(data, true);
        TestIdentity(data, false);
    }

    [Fact]
    public void Test4Tables()
    {
        byte[] data = new byte[1100];
        new Random(rand.Next()).NextBytes(data);
        TestIdentity(data, true);
        TestIdentity(data, false);
    }

    [Fact]
    public void Test5Tables()
    {
        byte[] data = new byte[2300];
        new Random(rand.Next()).NextBytes(data);
        TestIdentity(data, true);
        TestIdentity(data, false);
    }
}
