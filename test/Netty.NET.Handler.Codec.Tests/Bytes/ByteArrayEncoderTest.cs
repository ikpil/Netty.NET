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
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Bytes;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Buffer.Unpooled;

namespace Netty.NET.Handler.Codec.Tests.Bytes;

public class ByteArrayEncoderTest : IDisposable
{
    private EmbeddedChannel ch;

    // CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setUp() runs from the
    // constructor and the @AfterEach tearDown() from Dispose() (https://xunit.net/docs/shared-context).
    public ByteArrayEncoderTest()
    {
        SetUp();
    }

    public void Dispose()
    {
        TearDown();
    }

    private void SetUp()
    {
        ch = new EmbeddedChannel(new ByteArrayEncoder());
    }

    private void TearDown()
    {
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncode()
    {
        byte[] b = new byte[2048];
        // CLR: ThreadLocalRandom.current() is Random.Shared (https://learn.microsoft.com/dotnet/api/system.random.shared).
        Random.Shared.NextBytes(b);
        ch.WriteOutbound(b);
        ByteBuf encoded = ch.ReadOutbound<ByteBuf>();
        Assert.Equal(WrappedBuffer(b), encoded);
        encoded.Release();
    }

    [Fact]
    public void TestEncodeEmpty()
    {
        ch.WriteOutbound(EmptyArrays.EMPTY_BYTES);
        Assert.Same(EmptyBuffer, ch.ReadOutbound());
    }

    [Fact]
    public void TestEncodeOtherType()
    {
        string str = "Meep!";
        ch.WriteOutbound(str);
        Assert.Same(str, ch.ReadOutbound());
    }
}
