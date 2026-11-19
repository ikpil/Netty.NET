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
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Redis.Tests.RedisCodecTestUtil;

namespace Netty.NET.Handler.Codec.Redis.Tests;

/**
 * Verifies the correct functionality of the {@link RedisDecoder} and {@link RedisArrayAggregator}.
 */
// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setup() runs from the constructor
// and the @AfterEach teardown() from Dispose() (https://xunit.net/docs/shared-context). assertInstanceOf is
// Assert.IsAssignableFrom (https://raw.githubusercontent.com/xunit/assert.xunit/main/TypeAsserts.cs), the anonymous
// Executables are lambdas, Integer.toString(int) is the invariant-culture decimal text
// (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring), and the char arguments of writeByte(int) are
// passed as their int values.
public class RedisDecoderTest : IDisposable
{

    private EmbeddedChannel channel;

    public RedisDecoderTest()
    {
        Setup();
    }

    public void Dispose()
    {
        Teardown();
    }

    private void Setup()
    {
        channel = NewChannel(false);
    }

    private static EmbeddedChannel NewChannel(bool decodeInlineCommands)
    {
        return new EmbeddedChannel(
                new RedisDecoder(decodeInlineCommands),
                new RedisBulkStringAggregator(),
                new RedisArrayAggregator(100, 1024));
    }

    private void Teardown()
    {
        Assert.False(channel.Finish());
    }

    [Fact]
    public void SplitEOLDoesNotInfiniteLoop()
    {
        Assert.False(channel.WriteInbound(ByteBufOf("$6\r\nfoobar\r")));
        Assert.True(channel.WriteInbound(ByteBufOf("\n")));

        IRedisMessage msg = channel.ReadInbound<IRedisMessage>();
        Assert.IsAssignableFrom<FullBulkStringRedisMessage>(msg);
        ReferenceCountUtil.Release(msg);
    }

    [Fact]
    public void ShouldNotDecodeInlineCommandByDefault()
    {
        Assert.Throws<DecoderException>(() =>
        {
            Assert.False(channel.WriteInbound(ByteBufOf("P")));
            Assert.False(channel.WriteInbound(ByteBufOf("I")));
            Assert.False(channel.WriteInbound(ByteBufOf("N")));
            Assert.False(channel.WriteInbound(ByteBufOf("G")));
            Assert.True(channel.WriteInbound(ByteBufOf("\r\n")));

            channel.ReadInbound();
        });
    }

    [Fact]
    public void ShouldDecodeInlineCommand()
    {
        channel = NewChannel(true);

        Assert.False(channel.WriteInbound(ByteBufOf("P")));
        Assert.False(channel.WriteInbound(ByteBufOf("I")));
        Assert.False(channel.WriteInbound(ByteBufOf("N")));
        Assert.False(channel.WriteInbound(ByteBufOf("G")));
        Assert.True(channel.WriteInbound(ByteBufOf("\r\n")));

        InlineCommandRedisMessage msg = channel.ReadInbound<InlineCommandRedisMessage>();

        Assert.Equal("PING", msg.Content());

        ReferenceCountUtil.Release(msg);
    }

    [Fact]
    public void ShouldDecodeSimpleString()
    {
        Assert.False(channel.WriteInbound(ByteBufOf("+")));
        Assert.False(channel.WriteInbound(ByteBufOf("O")));
        Assert.False(channel.WriteInbound(ByteBufOf("K")));
        Assert.True(channel.WriteInbound(ByteBufOf("\r\n")));

        SimpleStringRedisMessage msg = channel.ReadInbound<SimpleStringRedisMessage>();

        Assert.Equal("OK", msg.Content());

        ReferenceCountUtil.Release(msg);
    }

    [Fact]
    public void ShouldDecodeTwoSimpleStrings()
    {
        Assert.False(channel.WriteInbound(ByteBufOf("+")));
        Assert.False(channel.WriteInbound(ByteBufOf("O")));
        Assert.False(channel.WriteInbound(ByteBufOf("K")));
        Assert.True(channel.WriteInbound(ByteBufOf("\r\n+SEC")));
        Assert.True(channel.WriteInbound(ByteBufOf("OND\r\n")));

        SimpleStringRedisMessage msg1 = channel.ReadInbound<SimpleStringRedisMessage>();
        Assert.Equal("OK", msg1.Content());
        ReferenceCountUtil.Release(msg1);

        SimpleStringRedisMessage msg2 = channel.ReadInbound<SimpleStringRedisMessage>();
        Assert.Equal("SECOND", msg2.Content());
        ReferenceCountUtil.Release(msg2);
    }

    [Fact]
    public void ShouldDecodeError()
    {
        string content = "ERROR sample message";
        Assert.False(channel.WriteInbound(ByteBufOf("-")));
        Assert.False(channel.WriteInbound(ByteBufOf(content)));
        Assert.False(channel.WriteInbound(ByteBufOf("\r")));
        Assert.True(channel.WriteInbound(ByteBufOf("\n")));

        ErrorRedisMessage msg = channel.ReadInbound<ErrorRedisMessage>();

        Assert.Equal(content, msg.Content());

        ReferenceCountUtil.Release(msg);
    }

    [Fact]
    public void ShouldDecodeInteger()
    {
        long value = 1234L;
        byte[] content = BytesOf(value);
        Assert.False(channel.WriteInbound(ByteBufOf(":")));
        Assert.False(channel.WriteInbound(ByteBufOf(content)));
        Assert.True(channel.WriteInbound(ByteBufOf("\r\n")));

        IntegerRedisMessage msg = channel.ReadInbound<IntegerRedisMessage>();

        Assert.Equal(value, msg.Value());

        ReferenceCountUtil.Release(msg);
    }

    [Fact]
    public void ShouldDecodeBulkString()
    {
        string buf1 = "bulk\nst";
        string buf2 = "ring\ntest\n1234";
        byte[] content = BytesOf(buf1 + buf2);
        Assert.False(channel.WriteInbound(ByteBufOf("$")));
        Assert.False(channel.WriteInbound(ByteBufOf(content.Length.ToString(CultureInfo.InvariantCulture))));
        Assert.False(channel.WriteInbound(ByteBufOf("\r\n")));
        Assert.False(channel.WriteInbound(ByteBufOf(buf1)));
        Assert.False(channel.WriteInbound(ByteBufOf(buf2)));
        Assert.True(channel.WriteInbound(ByteBufOf("\r\n")));

        FullBulkStringRedisMessage msg = channel.ReadInbound<FullBulkStringRedisMessage>();

        Assert.Equal(content, BytesOf(msg.Content));

        ReferenceCountUtil.Release(msg);
    }

    [Fact]
    public void ShouldDecodeEmptyBulkString()
    {
        byte[] content = BytesOf("");
        Assert.False(channel.WriteInbound(ByteBufOf("$")));
        Assert.False(channel.WriteInbound(ByteBufOf(content.Length.ToString(CultureInfo.InvariantCulture))));
        Assert.False(channel.WriteInbound(ByteBufOf("\r\n")));
        Assert.False(channel.WriteInbound(ByteBufOf(content)));
        Assert.True(channel.WriteInbound(ByteBufOf("\r\n")));

        FullBulkStringRedisMessage msg = channel.ReadInbound<FullBulkStringRedisMessage>();

        Assert.Equal(content, BytesOf(msg.Content));

        ReferenceCountUtil.Release(msg);
    }

    [Fact]
    public void ShouldDecodeNullBulkString()
    {
        Assert.False(channel.WriteInbound(ByteBufOf("$")));
        Assert.False(channel.WriteInbound(ByteBufOf((-1).ToString(CultureInfo.InvariantCulture))));
        Assert.True(channel.WriteInbound(ByteBufOf("\r\n")));

        Assert.True(channel.WriteInbound(ByteBufOf("$")));
        Assert.True(channel.WriteInbound(ByteBufOf((-1).ToString(CultureInfo.InvariantCulture))));
        Assert.True(channel.WriteInbound(ByteBufOf("\r\n")));

        FullBulkStringRedisMessage msg1 = channel.ReadInbound<FullBulkStringRedisMessage>();
        Assert.True(msg1.IsNull());
        ReferenceCountUtil.Release(msg1);

        FullBulkStringRedisMessage msg2 = channel.ReadInbound<FullBulkStringRedisMessage>();
        Assert.True(msg2.IsNull());
        ReferenceCountUtil.Release(msg2);

        FullBulkStringRedisMessage msg3 = channel.ReadInbound<FullBulkStringRedisMessage>();
        Assert.Null(msg3);
    }

    [Fact]
    public void ShouldDecodeSimpleArray()
    {
        Assert.False(channel.WriteInbound(ByteBufOf("*3\r\n")));
        Assert.False(channel.WriteInbound(ByteBufOf(":1234\r\n")));
        Assert.False(channel.WriteInbound(ByteBufOf("+sim")));
        Assert.False(channel.WriteInbound(ByteBufOf("ple\r\n-err")));
        Assert.True(channel.WriteInbound(ByteBufOf("or\r\n")));

        ArrayRedisMessage msg = channel.ReadInbound<ArrayRedisMessage>();
        IList<IRedisMessage> children = msg.Children();

        Assert.Equal(3, msg.Children().Count);

        Assert.IsAssignableFrom<IntegerRedisMessage>(children[0]);
        Assert.Equal(1234L, ((IntegerRedisMessage) children[0]).Value());
        Assert.IsAssignableFrom<SimpleStringRedisMessage>(children[1]);
        Assert.Equal("simple", ((SimpleStringRedisMessage) children[1]).Content());
        Assert.IsAssignableFrom<ErrorRedisMessage>(children[2]);
        Assert.Equal("error", ((ErrorRedisMessage) children[2]).Content());

        ReferenceCountUtil.Release(msg);
    }

    [Fact]
    public void ShouldDecodeNestedArray()
    {
        ByteBuf buf = Unpooled.Buffer();
        buf.WriteBytes(ByteBufOf("*2\r\n"));
        buf.WriteBytes(ByteBufOf("*3\r\n:1\r\n:2\r\n:3\r\n"));
        buf.WriteBytes(ByteBufOf("*2\r\n+Foo\r\n-Bar\r\n"));
        Assert.True(channel.WriteInbound(buf));

        ArrayRedisMessage msg = channel.ReadInbound<ArrayRedisMessage>();
        IList<IRedisMessage> children = msg.Children();

        Assert.Equal(2, msg.Children().Count);

        ArrayRedisMessage intArray = (ArrayRedisMessage) children[0];
        ArrayRedisMessage strArray = (ArrayRedisMessage) children[1];

        Assert.Equal(3, intArray.Children().Count);
        Assert.Equal(1L, ((IntegerRedisMessage) intArray.Children()[0]).Value());
        Assert.Equal(2L, ((IntegerRedisMessage) intArray.Children()[1]).Value());
        Assert.Equal(3L, ((IntegerRedisMessage) intArray.Children()[2]).Value());

        Assert.Equal(2, strArray.Children().Count);
        Assert.Equal("Foo", ((SimpleStringRedisMessage) strArray.Children()[0]).Content());
        Assert.Equal("Bar", ((ErrorRedisMessage) strArray.Children()[1]).Content());

        ReferenceCountUtil.Release(msg);
    }

    // CLR: the AssertJ chain assertThatThrownBy(...).isInstanceOf(DecoderException.class).rootCause()
    // .isInstanceOf(CodecException.class).hasMessageContaining("100") is Assert.ThrowsAny<DecoderException> (isInstanceOf
    // accepts subtypes), Exception.GetBaseException() for the innermost cause, which "returns the Exception that is the
    // root cause of one or more subsequent exceptions" (https://learn.microsoft.com/en-us/dotnet/api/system.exception.getbaseexception),
    // Assert.IsAssignableFrom and Assert.Contains on the message.
    [Fact]
    public void ShouldErrorOnTooLargeArray()
    {
        // We defined the max aggregate array size to be 100
        DecoderException thrown = Assert.ThrowsAny<DecoderException>(() => channel.WriteInbound(ByteBufOf("*101\r\n")));
        Exception rootCause = thrown.GetBaseException();
        Assert.IsAssignableFrom<CodecException>(rootCause);
        Assert.Contains("100", rootCause.Message);
    }

    [Fact]
    public void ShouldErrorOnDoubleReleaseArrayReferenceCounted()
    {
        ByteBuf buf = Unpooled.Buffer();
        buf.WriteBytes(ByteBufOf("*2\r\n"));
        buf.WriteBytes(ByteBufOf("*3\r\n:1\r\n:2\r\n:3\r\n"));
        buf.WriteBytes(ByteBufOf("*2\r\n+Foo\r\n-Bar\r\n"));
        Assert.True(channel.WriteInbound(buf));

        ArrayRedisMessage msg = channel.ReadInbound<ArrayRedisMessage>();

        ReferenceCountUtil.Release(msg);
        Assert.Throws<IllegalReferenceCountException>(() => ReferenceCountUtil.Release(msg));
    }

    [Fact]
    public void ShouldErrorOnReleaseArrayChildReferenceCounted()
    {
        ByteBuf buf = Unpooled.Buffer();
        buf.WriteBytes(ByteBufOf("*2\r\n"));
        buf.WriteBytes(ByteBufOf("*3\r\n:1\r\n:2\r\n:3\r\n"));
        buf.WriteBytes(ByteBufOf("$3\r\nFoo\r\n"));
        Assert.True(channel.WriteInbound(buf));

        ArrayRedisMessage msg = channel.ReadInbound<ArrayRedisMessage>();

        IList<IRedisMessage> children = msg.Children();
        ReferenceCountUtil.Release(msg);
        Assert.Throws<IllegalReferenceCountException>(() => ReferenceCountUtil.Release(children[1]));
    }

    [Fact]
    public void ShouldErrorOnReleasecontentOfArrayChildReferenceCounted()
    {
        ByteBuf buf = Unpooled.Buffer();
        buf.WriteBytes(ByteBufOf("*2\r\n"));
        buf.WriteBytes(ByteBufOf("$3\r\nFoo\r\n$3\r\nBar\r\n"));
        Assert.True(channel.WriteInbound(buf));

        ArrayRedisMessage msg = channel.ReadInbound<ArrayRedisMessage>();

        IList<IRedisMessage> children = msg.Children();
        ByteBuf childBuf = ((FullBulkStringRedisMessage) children[0]).Content;
        ReferenceCountUtil.Release(msg);
        Assert.Throws<IllegalReferenceCountException>(() => ReferenceCountUtil.Release(childBuf));
    }

    [Fact]
    public void TestPredefinedMessagesNotEqual()
    {
        // both EMPTY_INSTANCE and NULL_INSTANCE have EMPTY_BUFFER as their 'data',
        // however we need to check that they are not equal between themselves.
        Assert.NotEqual(FullBulkStringRedisMessage.EMPTY_INSTANCE, FullBulkStringRedisMessage.NULL_INSTANCE);
        Assert.NotEqual(FullBulkStringRedisMessage.NULL_INSTANCE, FullBulkStringRedisMessage.EMPTY_INSTANCE);
    }

    [Fact]
    public void ShouldLimitIntegerTo64IntSigned()
    {
        ByteBuf buf = Unpooled.Buffer();
        buf.WriteByte('$');
        for (int i = 0; i <= RedisConstants.POSITIVE_LONG_MAX_LENGTH; i++)
        {
            buf.WriteByte('0');
        }
        Assert.False(channel.WriteInbound(buf));

        Assert.Throws<DecoderException>(() => channel.WriteInbound(ByteBufOf("1")));
    }

    [Fact]
    public void TestPositiveLongWithCr()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new RedisDecoder());
        ByteBuf buf = Unpooled.Buffer();
        buf.WriteByte('$');
        for (int i = 0; i < RedisConstants.POSITIVE_LONG_MAX_LENGTH; i++)
        {
            buf.WriteByte('0');
        }
        buf.WriteByte('\r');

        // 19 digits + \r = 20 bytes.
        // It's a valid incomplete RESP number waiting for \n.
        Assert.False(channel.WriteInbound(buf));

        ByteBuf buf2 = Unpooled.Buffer();
        buf2.WriteByte('\n');
        Assert.False(channel.WriteInbound(buf2));
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestGiantPayloadEndingWithCrBypassesLengthCheck()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new RedisDecoder());
        ByteBuf buf = Unpooled.Buffer();
        buf.WriteByte('$');
        for (int i = 0; i < RedisConstants.POSITIVE_LONG_MAX_LENGTH; i++)
        {
            buf.WriteByte('1');
        }
        Assert.False(channel.WriteInbound(buf));

        // We expect that sending 1000 more bytes will exceed the maximum valid length capacity,
        // regardless of whether the final byte is '\r'. It should throw a DecoderException.
        Assert.Throws<DecoderException>(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                ByteBuf chunk = Unpooled.Buffer();
                chunk.WriteByte('a');
                chunk.WriteByte('\r');
                channel.WriteInbound(chunk);
            }
        });
        Assert.False(channel.Finish());
    }
}
