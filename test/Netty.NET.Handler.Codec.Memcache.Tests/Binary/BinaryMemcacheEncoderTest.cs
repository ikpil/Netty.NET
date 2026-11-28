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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Memcache;
using Netty.NET.Handler.Codec.Memcache.Binary;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Memcache.Tests.Binary;

/**
 * Verifies the correct functionality of the {@link AbstractBinaryMemcacheEncoder}.
 */
// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setup() runs from the
// constructor and the @AfterEach teardown() from Dispose() (https://xunit.net/docs/shared-context).
// CharsetUtil.UTF_8 is Encoding.UTF8, and the anonymous Executable passed to assertThrows is a lambda. The (byte)
// 0x80 and (byte) 0xAA magic values are the System.Byte values 128 and 170, which ReadByte() returns for the same
// bits (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
public class BinaryMemcacheEncoderTest : IDisposable
{

    public const int DEFAULT_HEADER_SIZE = 24;

    private EmbeddedChannel channel;

    public BinaryMemcacheEncoderTest()
    {
        Setup();
    }

    public void Dispose()
    {
        Teardown();
    }

    private void Setup()
    {
        channel = new EmbeddedChannel(new BinaryMemcacheRequestEncoder());
    }

    private void Teardown()
    {
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void ShouldEncodeDefaultHeader()
    {
        IBinaryMemcacheRequest request = new DefaultBinaryMemcacheRequest();

        bool result = channel.WriteOutbound(request);
        Assert.True(result);

        ByteBuf written = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(DEFAULT_HEADER_SIZE, written.ReadableBytes);
        Assert.Equal((byte) 0x80, written.ReadByte());
        Assert.Equal((byte) 0x00, written.ReadByte());
        written.Release();
    }

    [Fact]
    public void ShouldEncodeCustomHeader()
    {
        IBinaryMemcacheRequest request = new DefaultBinaryMemcacheRequest();
        request.SetMagic((byte) 0xAA);
        request.SetOpcode(BinaryMemcacheOpcodes.GET);

        bool result = channel.WriteOutbound(request);
        Assert.True(result);

        ByteBuf written = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(DEFAULT_HEADER_SIZE, written.ReadableBytes);
        Assert.Equal((byte) 0xAA, written.ReadByte());
        Assert.Equal(BinaryMemcacheOpcodes.GET, written.ReadByte());
        written.Release();
    }

    [Fact]
    public void ShouldEncodeExtras()
    {
        string extrasContent = "netty<3memcache";
        ByteBuf extras = Unpooled.CopiedBuffer(extrasContent, Encoding.UTF8);
        int extrasLength = extras.ReadableBytes;

        IBinaryMemcacheRequest request = new DefaultBinaryMemcacheRequest(Unpooled.EmptyBuffer, extras);

        bool result = channel.WriteOutbound(request);
        Assert.True(result);

        ByteBuf written = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(DEFAULT_HEADER_SIZE + extrasLength, written.ReadableBytes);
        written.SkipBytes(DEFAULT_HEADER_SIZE);
        Assert.Equal(extrasContent, written.ReadSlice(extrasLength).ToString(Encoding.UTF8));
        written.Release();
    }

    [Fact]
    public void ShouldEncodeKey()
    {
        ByteBuf key = Unpooled.CopiedBuffer("netty", Encoding.UTF8);
        int keyLength = key.ReadableBytes;

        IBinaryMemcacheRequest request = new DefaultBinaryMemcacheRequest(key);

        bool result = channel.WriteOutbound(request);
        Assert.True(result);

        ByteBuf written = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(DEFAULT_HEADER_SIZE + keyLength, written.ReadableBytes);
        written.SkipBytes(DEFAULT_HEADER_SIZE);
        Assert.Equal("netty", written.ReadSlice(keyLength).ToString(Encoding.UTF8));
        written.Release();
    }

    [Fact]
    public void ShouldEncodeContent()
    {
        DefaultMemcacheContent content1 =
            new DefaultMemcacheContent(Unpooled.CopiedBuffer("Netty", Encoding.UTF8));
        DefaultLastMemcacheContent content2 =
            new DefaultLastMemcacheContent(Unpooled.CopiedBuffer(" Rocks!", Encoding.UTF8));
        int totalBodyLength = content1.Content.ReadableBytes + content2.Content.ReadableBytes;

        IBinaryMemcacheRequest request = new DefaultBinaryMemcacheRequest();
        request.SetTotalBodyLength(totalBodyLength);

        bool result = channel.WriteOutbound(request);
        Assert.True(result);
        result = channel.WriteOutbound(content1);
        Assert.True(result);
        result = channel.WriteOutbound(content2);
        Assert.True(result);

        ByteBuf written = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(DEFAULT_HEADER_SIZE, written.ReadableBytes);
        written.Release();

        written = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(content1.Content.ReadableBytes, written.ReadableBytes);
        Assert.Equal("Netty",
                written.ReadSlice(content1.Content.ReadableBytes).ToString(Encoding.UTF8)
        );
        written.Release();

        written = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(content2.Content.ReadableBytes, written.ReadableBytes);
        Assert.Equal(" Rocks!",
                written.ReadSlice(content2.Content.ReadableBytes).ToString(Encoding.UTF8)
        );
        written.Release();
    }

    [Fact]
    public void ShouldFailWithoutLastContent()
    {
        channel.WriteOutbound(new DefaultMemcacheContent(Unpooled.EmptyBuffer));
        Assert.Throws<EncoderException>(() => channel.WriteOutbound(new DefaultBinaryMemcacheRequest()));
    }
}
