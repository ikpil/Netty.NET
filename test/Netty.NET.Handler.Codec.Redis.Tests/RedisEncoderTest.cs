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
using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Redis.Tests.RedisCodecTestUtil;

namespace Netty.NET.Handler.Codec.Redis.Tests;

/**
 * Verifies the correct functionality of the {@link RedisEncoder}.
 */
// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setup() runs from the constructor
// and the @AfterEach teardown() from Dispose() (https://xunit.net/docs/shared-context). assertArrayEquals is
// Assert.Equal on the arrays, ArrayList is List, and the int concatenated into the expected text uses the invariant
// culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public class RedisEncoderTest : IDisposable
{

    private EmbeddedChannel channel;

    public RedisEncoderTest()
    {
        Setup();
    }

    public void Dispose()
    {
        Teardown();
    }

    private void Setup()
    {
        channel = new EmbeddedChannel(new RedisEncoder());
    }

    private void Teardown()
    {
        Assert.False(channel.Finish());
    }

    [Fact]
    public void ShouldEncodeInlineCommand()
    {
        IRedisMessage msg = new InlineCommandRedisMessage("ping");

        bool result = channel.WriteOutbound(msg);
        Assert.True(result);

        ByteBuf written = ReadAll(channel);
        Assert.Equal(BytesOf("ping\r\n"), BytesOf(written));
        written.Release();
    }

    [Fact]
    public void ShouldEncodeSimpleString()
    {
        IRedisMessage msg = new SimpleStringRedisMessage("simple");

        bool result = channel.WriteOutbound(msg);
        Assert.True(result);

        ByteBuf written = ReadAll(channel);
        Assert.Equal(BytesOf("+simple\r\n"), BytesOf(written));
        written.Release();
    }

    [Fact]
    public void ShouldEncodeError()
    {
        IRedisMessage msg = new ErrorRedisMessage("error1");

        bool result = channel.WriteOutbound(msg);
        Assert.True(result);

        ByteBuf written = ReadAll(channel);
        Assert.Equal(BytesOf("-error1\r\n"), BytesOf(written));
        written.Release();
    }

    [Fact]
    public void ShouldEncodeInteger()
    {
        IRedisMessage msg = new IntegerRedisMessage(1234L);

        bool result = channel.WriteOutbound(msg);
        Assert.True(result);

        ByteBuf written = ReadAll(channel);
        Assert.Equal(BytesOf(":1234\r\n"), BytesOf(written));
        written.Release();
    }

    [Fact]
    public void ShouldEncodeBulkStringContent()
    {
        IRedisMessage header = new BulkStringHeaderRedisMessage(16);
        IRedisMessage body1 = new DefaultBulkStringRedisContent(ByteBufOf("bulk\nstr").Retain());
        IRedisMessage body2 = new DefaultLastBulkStringRedisContent(ByteBufOf("ing\ntest").Retain());

        Assert.True(channel.WriteOutbound(header));
        Assert.True(channel.WriteOutbound(body1));
        Assert.True(channel.WriteOutbound(body2));

        ByteBuf written = ReadAll(channel);
        Assert.Equal(BytesOf("$16\r\nbulk\nstring\ntest\r\n"), BytesOf(written));
        written.Release();
    }

    [Fact]
    public void ShouldEncodeFullBulkString()
    {
        ByteBuf bulkString = ByteBufOf("bulk\nstring\ntest").Retain();
        int length = bulkString.ReadableBytes;
        IRedisMessage msg = new FullBulkStringRedisMessage(bulkString);

        bool result = channel.WriteOutbound(msg);
        Assert.True(result);

        ByteBuf written = ReadAll(channel);
        Assert.Equal(BytesOf("$" + length.ToString(CultureInfo.InvariantCulture) + "\r\nbulk\nstring\ntest\r\n"),
                BytesOf(written));
        written.Release();
    }

    [Fact]
    public void ShouldEncodeSimpleArray()
    {
        List<IRedisMessage> children = new List<IRedisMessage>();
        children.Add(new FullBulkStringRedisMessage(ByteBufOf("foo").Retain()));
        children.Add(new FullBulkStringRedisMessage(ByteBufOf("bar").Retain()));
        IRedisMessage msg = new ArrayRedisMessage(children);

        bool result = channel.WriteOutbound(msg);
        Assert.True(result);

        ByteBuf written = ReadAll(channel);
        Assert.Equal(BytesOf("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n"), BytesOf(written));
        written.Release();
    }

    [Fact]
    public void ShouldEncodeNullArray()
    {
        IRedisMessage msg = ArrayRedisMessage.NULL_INSTANCE;

        bool result = channel.WriteOutbound(msg);
        Assert.True(result);

        ByteBuf written = ReadAll(channel);
        Assert.Equal(BytesOf("*-1\r\n"), BytesOf(written));
        written.Release();
    }

    [Fact]
    public void ShouldEncodeEmptyArray()
    {
        IRedisMessage msg = ArrayRedisMessage.EMPTY_INSTANCE;

        bool result = channel.WriteOutbound(msg);
        Assert.True(result);

        ByteBuf written = ReadAll(channel);
        Assert.Equal(BytesOf("*0\r\n"), BytesOf(written));
        written.Release();
    }

    [Fact]
    public void ShouldEncodeNestedArray()
    {
        List<IRedisMessage> grandChildren = new List<IRedisMessage>();
        grandChildren.Add(new FullBulkStringRedisMessage(ByteBufOf("bar")));
        grandChildren.Add(new IntegerRedisMessage(-1234L));
        List<IRedisMessage> children = new List<IRedisMessage>();
        children.Add(new SimpleStringRedisMessage("foo"));
        children.Add(new ArrayRedisMessage(grandChildren));
        IRedisMessage msg = new ArrayRedisMessage(children);

        bool result = channel.WriteOutbound(msg);
        Assert.True(result);

        ByteBuf written = ReadAll(channel);
        Assert.Equal(BytesOf("*2\r\n+foo\r\n*2\r\n$3\r\nbar\r\n:-1234\r\n"), BytesOf(written));
        written.Release();
    }

    private static ByteBuf ReadAll(EmbeddedChannel channel)
    {
        ByteBuf buf = Unpooled.Buffer();
        ByteBuf read;
        while ((read = channel.ReadOutbound<ByteBuf>()) != null)
        {
            buf.WriteBytes(read);
            read.Release();
        }
        return buf;
    }
}
