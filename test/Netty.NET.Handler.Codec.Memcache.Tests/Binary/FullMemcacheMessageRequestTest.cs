/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Handler.Codec.Memcache.Binary;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Memcache.Tests.Binary;

// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setup() runs from the
// constructor and the @AfterEach teardown() from Dispose() (https://xunit.net/docs/shared-context).
// CharsetUtil.UTF_8 is Encoding.UTF8 and String.getBytes(Charset) is Encoding.GetBytes.
public class FullMemcacheMessageRequestTest : IDisposable
{

    private EmbeddedChannel channel;

    public FullMemcacheMessageRequestTest()
    {
        Setup();
    }

    public void Dispose()
    {
        Teardown();
    }

    private void Setup()
    {
        channel = new EmbeddedChannel(
                new BinaryMemcacheRequestEncoder(),
                new BinaryMemcacheRequestDecoder(),
                new BinaryMemcacheObjectAggregator(1024));
    }

    private void Teardown()
    {
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestEncodeDecode()
    {
        ByteBuf key = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("key"));
        ByteBuf content = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("content"));
        ByteBuf extras = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("extras"));
        IFullBinaryMemcacheRequest req = new DefaultFullBinaryMemcacheRequest(key, extras, content);
        Assert.True(channel.WriteOutbound(req));
        // header + content
        Assert.Equal(2, channel.OutboundMessages().Count);
        Assert.True(channel.WriteInbound(channel.ReadOutbound(), channel.ReadOutbound()));

        IFullBinaryMemcacheRequest read = channel.ReadInbound<IFullBinaryMemcacheRequest>();
        Assert.Equal("key", read.Key().ToString(Encoding.UTF8));
        Assert.Equal("content", read.Content.ToString(Encoding.UTF8));
        Assert.Equal("extras", read.Extras().ToString(Encoding.UTF8));
        read.Release();
    }
}
