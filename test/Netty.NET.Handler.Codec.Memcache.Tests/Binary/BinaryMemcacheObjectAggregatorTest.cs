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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Memcache;
using Netty.NET.Handler.Codec.Memcache.Binary;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Memcache.Tests.Binary;

/**
 * Verifies the correct functionality of the {@link BinaryMemcacheObjectAggregator}.
 */
// CLR: CharsetUtil.UTF_8 is Encoding.UTF8 (docs/codec-base-porting.md), assertInstanceOf is Assert.IsAssignableFrom
// (https://raw.githubusercontent.com/xunit/assert.xunit/main/TypeAsserts.cs), and the (byte) 0x80 of the request is the
// System.Byte value with the same bits (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
public class BinaryMemcacheObjectAggregatorTest
{

    private static readonly byte[] SET_REQUEST_WITH_CONTENT = {
        0x80, 0x01, 0x00, 0x03,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x0B,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x66, 0x6f, 0x6f,
        0x01, 0x02, 0x03, 0x04,
        0x05, 0x06, 0x07, 0x08
    };

    public const int MAX_CONTENT_SIZE = 2 << 10;

    private EmbeddedChannel channel;

    [Fact]
    public void ShouldAggregateChunksOnDecode()
    {
        int smallBatchSize = 2;
        channel = new EmbeddedChannel(
            new BinaryMemcacheRequestDecoder(smallBatchSize),
            new BinaryMemcacheObjectAggregator(MAX_CONTENT_SIZE));

        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(SET_REQUEST_WITH_CONTENT);
        channel.WriteInbound(incoming);

        IFullBinaryMemcacheRequest request = channel.ReadInbound<IFullBinaryMemcacheRequest>();

        Assert.IsAssignableFrom<IFullBinaryMemcacheRequest>(request);
        Assert.NotNull(request);
        Assert.NotNull(request.Key());
        Assert.Null(request.Extras());

        Assert.Equal(8, request.Content.ReadableBytes);
        Assert.Equal((byte) 0x01, request.Content.ReadByte());
        Assert.Equal((byte) 0x02, request.Content.ReadByte());
        request.Release();

        Assert.Null(channel.ReadInbound());

        Assert.False(channel.Finish());
    }

    [Fact]
    public void ShouldRetainByteBufWhenAggregating()
    {
        channel = new EmbeddedChannel(
                new BinaryMemcacheRequestEncoder(),
                new BinaryMemcacheRequestDecoder(),
                new BinaryMemcacheObjectAggregator(MAX_CONTENT_SIZE));

        ByteBuf key = Unpooled.CopiedBuffer("Netty", Encoding.UTF8);
        ByteBuf extras = Unpooled.CopiedBuffer("extras", Encoding.UTF8);
        IBinaryMemcacheRequest request = new DefaultBinaryMemcacheRequest(key, extras);

        DefaultMemcacheContent content1 =
                new DefaultMemcacheContent(Unpooled.CopiedBuffer("Netty", Encoding.UTF8));
        DefaultLastMemcacheContent content2 =
                new DefaultLastMemcacheContent(Unpooled.CopiedBuffer(" Rocks!", Encoding.UTF8));
        int totalBodyLength = key.ReadableBytes + extras.ReadableBytes +
                content1.Content.ReadableBytes + content2.Content.ReadableBytes;
        request.SetTotalBodyLength(totalBodyLength);

        Assert.True(channel.WriteOutbound(request, content1, content2));

        Assert.Equal(3, channel.OutboundMessages().Count);
        Assert.True(channel.WriteInbound(channel.ReadOutbound(), channel.ReadOutbound(), channel.ReadOutbound()));

        IFullBinaryMemcacheRequest read = channel.ReadInbound<IFullBinaryMemcacheRequest>();
        Assert.NotNull(read);
        Assert.Equal("Netty", read.Key().ToString(Encoding.UTF8));
        Assert.Equal("extras", read.Extras().ToString(Encoding.UTF8));
        Assert.Equal("Netty Rocks!", read.Content.ToString(Encoding.UTF8));

        read.Release();
        Assert.False(channel.Finish());
    }
}
