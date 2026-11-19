/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Handler.Codec.Bytes;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Buffer.Unpooled;

namespace Netty.NET.Handler.Codec.Tests.Bytes;

public class ByteArrayDecoderTest
{
    private EmbeddedChannel ch;

    // CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setUp() runs from the
    // constructor (https://xunit.net/docs/shared-context).
    public ByteArrayDecoderTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        ch = new EmbeddedChannel(new ByteArrayDecoder());
    }

    [Fact]
    public void TestDecode()
    {
        byte[] b = new byte[2048];
        // CLR: ThreadLocalRandom.current() is Random.Shared (https://learn.microsoft.com/dotnet/api/system.random.shared).
        Random.Shared.NextBytes(b);
        ch.WriteInbound(WrappedBuffer(b));
        Assert.Equal(b, ch.ReadInbound<byte[]>());
    }

    [Fact]
    public void TestDecodeEmpty()
    {
        ch.WriteInbound(EmptyBuffer);
        Assert.Equal(EmptyArrays.EMPTY_BYTES, ch.ReadInbound<byte[]>());
    }

    [Fact]
    public void TestDecodeOtherType()
    {
        string str = "Meep!";
        ch.WriteInbound(str);
        Assert.Same(str, ch.ReadInbound());
    }
}
