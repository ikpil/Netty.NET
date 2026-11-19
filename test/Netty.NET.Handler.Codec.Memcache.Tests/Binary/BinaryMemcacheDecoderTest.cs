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
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Memcache;
using Netty.NET.Handler.Codec.Memcache.Binary;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Memcache.Tests.Binary;

/**
 * Verifies the correct functionality of the {@link AbstractBinaryMemcacheDecoder}.
 * <p/>
 * While technically there are both a {@link BinaryMemcacheRequestDecoder} and a {@link BinaryMemcacheResponseDecoder}
 * they implement the same basics and just differ in the type of headers returned.
 */
// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setup() runs from the constructor
// and the @AfterEach teardown() from Dispose() (https://xunit.net/docs/shared-context). CharsetUtil.UTF_8 is
// Encoding.UTF8 (docs/codec-base-porting.md), assertInstanceOf is Assert.IsAssignableFrom, which "verifies that an
// object is of the given type or a derived type" (https://raw.githubusercontent.com/xunit/assert.xunit/main/TypeAsserts.cs),
// and a @ParameterizedTest with @ValueSource(booleans) is a [Theory] with one [InlineData] per value. The byte[]
// initializers keep the bit patterns of the Java (byte) casts as System.Byte values, and their char literals are cast
// to byte because only int constants convert implicitly
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
public class BinaryMemcacheDecoderTest : IDisposable
{

    /**
     * Represents a GET request header with a key size of three.
     */
    private static readonly byte[] GET_REQUEST = {
        0x80, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x66, 0x6f, 0x6f
    };

    private static readonly byte[] SET_REQUEST_WITH_CONTENT = {
        0x80, 0x01, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0B, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x66, 0x6f, 0x6f, 0x01, 0x02, 0x03, 0x04, 0x05,
        0x06, 0x07, 0x08
    };

    private static readonly byte[] GET_RESPONSE_CHUNK_1 =  {
        0x81, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4e, 0x6f, 0x74, 0x20, 0x66, 0x6f, 0x75, 0x6e,
        0x64, 0x81, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4e, 0x6f, 0x74, 0x20, 0x66, 0x6f, 0x75,
    };

    private static readonly byte[] GET_RESPONSE_CHUNK_2 = {
            0x6e, 0x64, 0x81, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4e, 0x6f, 0x74, 0x20, 0x66, 0x6f,
            0x75, 0x6e, 0x64
    };

    private EmbeddedChannel channel;

    public BinaryMemcacheDecoderTest()
    {
        Setup();
    }

    public void Dispose()
    {
        Teardown();
    }

    private void Setup()
    {
        channel = new EmbeddedChannel(new BinaryMemcacheRequestDecoder());
    }

    private void Teardown()
    {
        channel.FinishAndReleaseAll();
    }

    /**
     * This tests a simple GET request with a key as the value.
     */
    [Fact]
    public void ShouldDecodeRequestWithSimpleValue()
    {
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(GET_REQUEST);
        channel.WriteInbound(incoming);

        IBinaryMemcacheRequest request = channel.ReadInbound<IBinaryMemcacheRequest>();

        Assert.NotNull(request);
        Assert.NotNull(request.Key());
        Assert.Null(request.Extras());

        Assert.Equal((short) 3, request.KeyLength());
        Assert.Equal((byte) 0, request.ExtrasLength());
        Assert.Equal(3, request.TotalBodyLength());

        request.Release();
        Assert.IsAssignableFrom<ILastMemcacheContent>(channel.ReadInbound());
    }

    /**
     * This test makes sure that large content is emitted in chunks.
     */
    [Fact]
    public void ShouldDecodeRequestWithChunkedContent()
    {
        int smallBatchSize = 2;
        channel = new EmbeddedChannel(new BinaryMemcacheRequestDecoder(smallBatchSize));

        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(SET_REQUEST_WITH_CONTENT);
        channel.WriteInbound(incoming);

        IBinaryMemcacheRequest request = channel.ReadInbound<IBinaryMemcacheRequest>();

        Assert.NotNull(request);
        Assert.NotNull(request.Key());
        Assert.Null(request.Extras());

        Assert.Equal((short) 3, request.KeyLength());
        Assert.Equal((byte) 0, request.ExtrasLength());
        Assert.Equal(11, request.TotalBodyLength());

        request.Release();

        int expectedContentChunks = 4;
        for (int i = 1; i <= expectedContentChunks; i++)
        {
            IMemcacheContent content = channel.ReadInbound<IMemcacheContent>();
            if (i < expectedContentChunks)
            {
                Assert.IsAssignableFrom<IMemcacheContent>(content);
            }
            else
            {
                Assert.IsAssignableFrom<ILastMemcacheContent>(content);
            }
            Assert.Equal(2, content.Content.ReadableBytes);
            content.Release();
        }
        Assert.Null(channel.ReadInbound());
    }

    /**
     * This test makes sure that even when the decoder is confronted with various chunk
     * sizes in the middle of decoding, it can recover and decode all the time eventually.
     */
    [Fact]
    public void ShouldHandleNonUniformNetworkBatches()
    {
        ByteBuf incoming = Unpooled.CopiedBuffer(SET_REQUEST_WITH_CONTENT);
        while (incoming.IsReadable())
        {
            channel.WriteInbound(incoming.ReadBytes(5));
        }
        incoming.Release();

        IBinaryMemcacheRequest request = channel.ReadInbound<IBinaryMemcacheRequest>();

        Assert.NotNull(request);
        Assert.NotNull(request.Key());
        Assert.Null(request.Extras());

        request.Release();

        IMemcacheContent content1 = channel.ReadInbound<IMemcacheContent>();
        IMemcacheContent content2 = channel.ReadInbound<IMemcacheContent>();

        Assert.IsAssignableFrom<IMemcacheContent>(content1);
        Assert.IsAssignableFrom<ILastMemcacheContent>(content2);

        Assert.Equal(3, content1.Content.ReadableBytes);
        Assert.Equal(5, content2.Content.ReadableBytes);

        content1.Release();
        content2.Release();
    }

    /**
     * This test makes sure that even when more requests arrive in the same batch, they
     * get emitted as separate messages.
     */
    [Fact]
    public void ShouldHandleTwoMessagesInOneBatch()
    {
        channel.WriteInbound(Unpooled.Buffer().WriteBytes(GET_REQUEST).WriteBytes(GET_REQUEST));

        IBinaryMemcacheRequest request = channel.ReadInbound<IBinaryMemcacheRequest>();
        Assert.IsAssignableFrom<IBinaryMemcacheRequest>(request);
        Assert.NotNull(request);
        request.Release();

        object lastContent = channel.ReadInbound();
        Assert.IsAssignableFrom<ILastMemcacheContent>(lastContent);
        ((IReferenceCounted) lastContent).Release();

        request = channel.ReadInbound<IBinaryMemcacheRequest>();
        Assert.IsAssignableFrom<IBinaryMemcacheRequest>(request);
        Assert.NotNull(request);
        request.Release();

        lastContent = channel.ReadInbound();
        Assert.IsAssignableFrom<ILastMemcacheContent>(lastContent);
        ((IReferenceCounted) lastContent).Release();
    }

    // CLR: Java substring(beginIndex, endIndex) is Substring(startIndex, length)
    // (https://learn.microsoft.com/en-us/dotnet/api/system.string.substring,
    // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html#substring(int,int)).
    [Fact]
    public void ShouldDecodeSeparatedValues()
    {
        string msgBody = "Not found";
        channel = new EmbeddedChannel(new BinaryMemcacheResponseDecoder());

        channel.WriteInbound(Unpooled.Buffer().WriteBytes(GET_RESPONSE_CHUNK_1));
        channel.WriteInbound(Unpooled.Buffer().WriteBytes(GET_RESPONSE_CHUNK_2));

        // First message
        IBinaryMemcacheResponse response = channel.ReadInbound<IBinaryMemcacheResponse>();
        Assert.Equal(BinaryMemcacheResponseStatus.KEY_ENOENT, response.Status());
        Assert.Equal(msgBody.Length, response.TotalBodyLength());
        response.Release();

        // First message first content chunk
        IMemcacheContent content = channel.ReadInbound<IMemcacheContent>();
        Assert.IsAssignableFrom<ILastMemcacheContent>(content);
        Assert.Equal(msgBody, content.Content.ToString(Encoding.UTF8));
        content.Release();

        // Second message
        response = channel.ReadInbound<IBinaryMemcacheResponse>();
        Assert.Equal(BinaryMemcacheResponseStatus.KEY_ENOENT, response.Status());
        Assert.Equal(msgBody.Length, response.TotalBodyLength());
        response.Release();

        // Second message first content chunk
        content = channel.ReadInbound<IMemcacheContent>();
        Assert.IsAssignableFrom<IMemcacheContent>(content);
        Assert.Equal(msgBody.Substring(0, 7), content.Content.ToString(Encoding.UTF8));
        content.Release();

        // Second message second content chunk
        content = channel.ReadInbound<IMemcacheContent>();
        Assert.IsAssignableFrom<ILastMemcacheContent>(content);
        Assert.Equal(msgBody.Substring(7, 9 - 7), content.Content.ToString(Encoding.UTF8));
        content.Release();

        // Third message
        response = channel.ReadInbound<IBinaryMemcacheResponse>();
        Assert.Equal(BinaryMemcacheResponseStatus.KEY_ENOENT, response.Status());
        Assert.Equal(msgBody.Length, response.TotalBodyLength());
        response.Release();

        // Third message first content chunk
        content = channel.ReadInbound<IMemcacheContent>();
        Assert.IsAssignableFrom<ILastMemcacheContent>(content);
        Assert.Equal(msgBody, content.Content.ToString(Encoding.UTF8));
        content.Release();
    }

    [Fact]
    public void ShouldRetainCurrentMessageWhenSendingItOut()
    {
        channel = new EmbeddedChannel(
                new BinaryMemcacheRequestEncoder(),
                new BinaryMemcacheRequestDecoder());

        ByteBuf key = Unpooled.CopiedBuffer("Netty", Encoding.UTF8);
        ByteBuf extras = Unpooled.CopiedBuffer("extras", Encoding.UTF8);
        IBinaryMemcacheRequest request = new DefaultBinaryMemcacheRequest(key, extras);

        Assert.True(channel.WriteOutbound(request));
        for (;;)
        {
            ByteBuf buffer = channel.ReadOutbound<ByteBuf>();
            if (buffer == null)
            {
                break;
            }
            channel.WriteInbound(buffer);
        }
        IBinaryMemcacheRequest read = channel.ReadInbound<IBinaryMemcacheRequest>();
        read.Release();
        // tearDown will call "channel.finish()"
    }

    /**
     * Builds a header, where {@code keyLength}, {@code extrasLength} and {@code totalBodyLength} are written as the
     * raw (unsigned) protocol values. Requests and responses share this layout and only differ in the magic byte
     * and in how the two bytes at offset 6 are interpreted.
     */
    private static ByteBuf Header(bool request, int keyLength, int extrasLength, int totalBodyLength)
    {
        return Unpooled.Buffer()
            .WriteByte(request ? 0x80 : 0x81)   // magic
            .WriteByte(0x00)                    // opcode
            .WriteShort(keyLength)
            .WriteByte(extrasLength)
            .WriteByte(0x00)                    // dataType
            .WriteShort(0x0000)                 // reserved (request) / status (response)
            .WriteInt(totalBodyLength)
            .WriteInt(0x00000000)               // opaque
            .WriteLong(0L);                     // cas
    }

    /**
     * A message with no key, no extras and a four byte value, used to verify whether the decoder is still in sync
     * with the stream after the message preceding it.
     */
    private static ByteBuf FollowUpMessage(bool request)
    {
        return Header(request, 0, 0, 4).WriteBytes(new byte[] { (byte) 'A', (byte) 'B', (byte) 'C', (byte) 'D' });
    }

    private void ResetChannel(bool request)
    {
        channel = new EmbeddedChannel(request
            ? new BinaryMemcacheRequestDecoder() : new BinaryMemcacheResponseDecoder());
    }

    private void AssertFollowUpMessageDecodedInSync()
    {
        IBinaryMemcacheMessage message = channel.ReadInbound<IBinaryMemcacheMessage>();
        Assert.NotNull(message);
        Assert.True(message.DecoderResult().IsSuccess());
        Assert.Equal(0, message.KeyLength());
        Assert.Equal(0, message.ExtrasLength());
        Assert.Equal(4, message.TotalBodyLength());
        message.Release();

        IMemcacheContent content = channel.ReadInbound<IMemcacheContent>();
        Assert.IsAssignableFrom<ILastMemcacheContent>(content);
        Assert.Equal("ABCD", content.Content.ToString(Encoding.UTF8));
        content.Release();

        Assert.Null(channel.ReadInbound());
    }

    private void AssertRejectedWithoutExposingBody()
    {
        IBinaryMemcacheMessage message = channel.ReadInbound<IBinaryMemcacheMessage>();
        Assert.NotNull(message);
        Assert.True(message.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<CorruptedFrameException>(message.DecoderResult().Cause());
        // A rejected header must not hand any part of the stream to the application. Before the length fields were
        // validated, the key of such a message could contain the bytes of the message following it, including its
        // opaque, which correlates a response to the request that asked for it.
        Assert.Equal(0, message.Key() == null ? 0 : message.Key().ReadableBytes);
        Assert.Equal(0, message.Extras() == null ? 0 : message.Extras().ReadableBytes);
        message.Release();
    }

    /**
     * The {@code extrasLength} header field is an unsigned 8 bit value, so it has to be interpreted as such rather
     * than as a signed {@code byte}. Otherwise the extras of a message with a length of {@code 0x80} or above are
     * not consumed and the following message is silently read as part of this message's value.
     */
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldDecodeExtrasWithLengthAboveSignedByteRange(bool request)
    {
        ResetChannel(request);

        ByteBuf frame = Header(request, 0, 0x80, 128).WriteBytes(new byte[128]);
        channel.WriteInbound(Unpooled.Buffer().WriteBytes(frame).WriteBytes(FollowUpMessage(request)));
        frame.Release();

        IBinaryMemcacheMessage message = channel.ReadInbound<IBinaryMemcacheMessage>();
        Assert.NotNull(message);
        Assert.True(message.DecoderResult().IsSuccess());
        Assert.NotNull(message.Extras());
        Assert.Equal(128, message.Extras().ReadableBytes);
        Assert.Equal(128, message.ExtrasLength() & 0xFF);
        Assert.Equal(128, message.TotalBodyLength());
        message.Release();

        // The whole body is extras, so there is no value.
        Assert.IsAssignableFrom<ILastMemcacheContent>(channel.ReadInbound());

        AssertFollowUpMessageDecodedInSync();
    }

    /**
     * The {@code keyLength} header field is an unsigned 16 bit value, so it has to be interpreted as such rather
     * than as a signed {@code short}. Otherwise the key of a message with a length of {@code 0x8000} or above is
     * not consumed and the following message is silently read as part of this message's value.
     */
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldDecodeKeyWithLengthAboveSignedShortRange(bool request)
    {
        ResetChannel(request);

        ByteBuf frame = Header(request, 0x8000, 0, 32768).WriteBytes(new byte[32768]);
        channel.WriteInbound(Unpooled.Buffer().WriteBytes(frame).WriteBytes(FollowUpMessage(request)));
        frame.Release();

        IBinaryMemcacheMessage message = channel.ReadInbound<IBinaryMemcacheMessage>();
        Assert.NotNull(message);
        Assert.True(message.DecoderResult().IsSuccess());
        Assert.NotNull(message.Key());
        Assert.Equal(32768, message.Key().ReadableBytes);
        Assert.Equal(32768, message.KeyLength() & 0xFFFF);
        Assert.Equal(32768, message.TotalBodyLength());
        message.Release();

        // The whole body is the key, so there is no value.
        Assert.IsAssignableFrom<ILastMemcacheContent>(channel.ReadInbound());

        AssertFollowUpMessageDecodedInSync();
    }

    /**
     * The protocol defines {@code totalBodyLength} as the length of extras + key + value, so a header where the
     * extras and the key alone are longer than the total body can not be framed and has to be rejected. Note that
     * this needs no oversized length at all: decoding a 20 byte key out of a 4 byte body reads past the end of the
     * message and desynchronizes the stream.
     */
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldRejectExtrasAndKeyLongerThanTotalBody(bool request)
    {
        ResetChannel(request);

        ByteBuf frame = Header(request, 20, 0, 4).WriteBytes(new byte[] { (byte) 'w', (byte) 'x', (byte) 'y', (byte) 'z' });
        channel.WriteInbound(Unpooled.Buffer().WriteBytes(frame).WriteBytes(FollowUpMessage(request)));
        frame.Release();

        AssertRejectedWithoutExposingBody();
    }

    /**
     * {@code totalBodyLength} is an unsigned 32 bit value, so it can exceed what an {@code int} can represent. Such
     * a message has to be rejected rather than treated as having a negative body length, which would leave the body
     * on the wire to be decoded as the next header.
     */
    // CLR: the Java int literal 0x80000000 is Integer.MIN_VALUE; the C# literal is a uint, so it is narrowed with
    // unchecked((int) ...) (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldRejectTotalBodyLengthAboveIntegerMaxValue(bool request)
    {
        ResetChannel(request);

        ByteBuf frame = Header(request, 0, 0, unchecked((int) 0x80000000)).WriteBytes(new byte[] { (byte) 'w', (byte) 'x', (byte) 'y', (byte) 'z' });
        channel.WriteInbound(Unpooled.Buffer().WriteBytes(frame).WriteBytes(FollowUpMessage(request)));
        frame.Release();

        AssertRejectedWithoutExposingBody();
    }

    /**
     * A {@code totalBodyLength} close to {@link Integer#MAX_VALUE} is representable and has to be accepted, with
     * the value length derived from it staying positive. Subtracting a signed, negative {@code keyLength} from such
     * a total body length overflowed instead, so the value was treated as empty and the message was terminated
     * before its body had been read.
     */
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldNotOverflowValueLengthForLargeTotalBodyLength(bool request)
    {
        ResetChannel(request);

        // 0x7FFFFFFF - (-32768) overflows, while 0x7FFFFFFF - 32768 does not. The body is far too large to supply,
        // so feed the key plus the first few value bytes and observe that the value is still being read.
        ByteBuf frame = Header(request, 0x8000, 0, int.MaxValue)
            .WriteBytes(new byte[32768])
            .WriteBytes(new byte[] { (byte) 'v', (byte) 'a', (byte) 'l', (byte) 'u', (byte) 'e' });
        channel.WriteInbound(frame);

        IBinaryMemcacheMessage message = channel.ReadInbound<IBinaryMemcacheMessage>();
        Assert.NotNull(message);
        Assert.True(message.DecoderResult().IsSuccess());
        Assert.NotNull(message.Key());
        Assert.Equal(32768, message.Key().ReadableBytes);
        message.Release();

        IMemcacheContent content = channel.ReadInbound<IMemcacheContent>();
        Assert.NotNull(content);
        Assert.Equal("value", content.Content.ToString(Encoding.UTF8));
        // Not the last chunk: the vast majority of the value is still outstanding.
        Assert.False(content is ILastMemcacheContent);
        content.Release();

        Assert.Null(channel.ReadInbound());
    }

    /**
     * A framing violation can not be recovered from, because there is no way to tell which of the length fields
     * lied. Decoding therefore has to stop permanently rather than attempt to resynchronize: everything that
     * arrives after the rejected header, including otherwise well formed messages, must be discarded.
     */
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldStopDecodingPermanentlyAfterRejectingAHeader(bool request)
    {
        ResetChannel(request);

        ByteBuf frame = Header(request, 20, 0, 4).WriteBytes(new byte[] { (byte) 'w', (byte) 'x', (byte) 'y', (byte) 'z' });
        channel.WriteInbound(frame);

        AssertRejectedWithoutExposingBody();
        Assert.Null(channel.ReadInbound());

        // Several perfectly valid messages arriving afterwards, in separate reads, must all be dropped.
        for (int i = 0; i < 3; i++)
        {
            channel.WriteInbound(FollowUpMessage(request));
            Assert.Null(channel.ReadInbound());
        }
    }
}
