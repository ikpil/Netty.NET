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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Mqtt.MqttProperties;
using static Netty.NET.Handler.Codec.Mqtt.MqttQoS;
using static Netty.NET.Handler.Codec.Mqtt.MqttSubscriptionOption.RetainedHandlingPolicy;
using static Netty.NET.Handler.Codec.Mqtt.Tests.MqttTestUtils;
using Assert = XunitAssertMessages.AssertM;

#pragma warning disable xUnit2013 // assertEquals(n, size()) keeps the source assertions.

namespace Netty.NET.Handler.Codec.Mqtt.Tests;

/**
 * Unit tests for MqttEncoder and MqttDecoder.
 */
// CLR: Mockito mocks are Moq mocks (when/thenReturn is Setup/Returns, the Answer is a Callback, verify/times is Verify
// with Times, clearInvocations is Invocations.Clear(), verifyNoMoreInteractions is VerifyNoOtherCalls), and the mocked
// ChannelHandlerContext is passed as ctx.Object. MockitoAnnotations.initMocks(this) has no counterpart because the
// mocks are created in the field initializers, as the source's mock(...) calls do. @BeforeEach setup() is called from
// the constructor and @AfterEach after() from Dispose(). The field and locals named out are @out; LinkedList is List<T>
// (a C# LinkedList<T> is no IList<T>). assertInstanceOf is Assert.IsAssignableFrom and AssertJ isNotInstanceOf is
// Assert.IsNotAssignableFrom; Consumer<MqttMessage> is Action<MqttMessage>; Throwable is Exception; CharsetUtil.UTF_8
// is Encoding.UTF8; char literals in byte[] initializers are cast to byte, which Java narrows implicitly. The
// @Deprecated packetId(short) builder overload and the willMessage()/password() and topicName() accessors are not
// ported (owner decision 2026-11-16): packetId((short) 1) is packetId(1), and validateConnectPayload asserts the byte[]
// replacements that the source also asserts. The tests call the protected decode(...) through the internal
// MqttDecoder.InvokeDecode forwarder, as Java reaches it through package access.
public class MqttCodecTest : IDisposable
{

    private const string CLIENT_ID = "RANDOM_TEST_CLIENT";
    private const string WILL_TOPIC = "/my_will";
    private const string WILL_MESSAGE = "gone";
    private const string USER_NAME = "happy_user";
    private const string PASSWORD = "123_or_no_pwd";
    private static readonly byte[] PASSWORD_BYTES = Encoding.UTF8.GetBytes(PASSWORD);

    private const int KEEP_ALIVE_SECONDS = 600;

    private static readonly IByteBufAllocator ALLOCATOR = new UnpooledByteBufAllocator(false);

    private readonly Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();

    private readonly Mock<IChannel> channel = new Mock<IChannel>();

    private readonly Mock<IAttribute<MqttVersion>> versionAttrMock = new Mock<IAttribute<MqttVersion>>();

    private readonly List<object> @out = new List<object>();

    private readonly MqttDecoder mqttDecoder = new MqttDecoder();

    public MqttCodecTest()
    {
        Setup();
    }

    private void Setup()
    {
        ctx.Setup(c => c.Channel).Returns(channel.Object);
        ctx.Setup(c => c.Alloc()).Returns(ALLOCATOR);
        ctx.Setup(c => c.FireChannelRead(It.IsAny<object>())).Callback((object msg) =>
        {
            @out.Add(msg);
        }).Returns(ctx.Object);
        channel.Setup(c => c.Attr(MqttCodecUtil.MQTT_VERSION_KEY)).Returns(versionAttrMock.Object);
    }

    public void Dispose()
    {
        After();
    }

    private void After()
    {
        ClearOut();
    }

    private void ClearOut()
    {
        foreach (object o in @out)
        {
            ReferenceCountUtil.Release(o);
        }
        @out.Clear();
    }

    [Fact]
    public void TestConnectMessageForMqtt31()
    {
        MqttConnectMessage message = CreateConnectMessage(MqttVersion.MQTT_3_1);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttConnectMessage decodedMessage = (MqttConnectMessage) @out[0];

        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateConnectVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
        ValidateConnectPayload(message.Payload(), decodedMessage.Payload());
    }

    [Fact]
    public void TestConnectMessageForMqtt311()
    {
        MqttConnectMessage message = CreateConnectMessage(MqttVersion.MQTT_3_1_1);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttConnectMessage decodedMessage = (MqttConnectMessage) @out[0];

        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateConnectVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
        ValidateConnectPayload(message.Payload(), decodedMessage.Payload());
    }

    [Fact]
    public void TestConnectMessageWithNonZeroReservedFlagForMqtt311()
    {
        MqttConnectMessage message = CreateConnectMessage(MqttVersion.MQTT_3_1_1);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        // Set the reserved flag in the CONNECT Packet to 1
        byteBuf.SetByte(9, byteBuf.GetByte(9) | 0x1);
        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttMessage decodedMessage = (MqttMessage) @out[0];
        Assert.True(decodedMessage.DecoderResult().IsFailure());
        Exception cause = decodedMessage.DecoderResult().Cause();
        Assert.IsAssignableFrom<DecoderException>(cause);
        Assert.Equal("non-zero reserved flag", cause.Message);
    }

    [Fact]
    public void TestConnectMessageNonZeroReservedBit0Mqtt311()
    {
        MqttConnectMessage message = CreateConnectMessage(MqttVersion.MQTT_3_1_1);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);
        byte firstByte = byteBuf.GetByte(0);
        byteBuf.SetByte(0, (byte) (firstByte | 1)); // set bit 0 to 1
        List<object> @out = new List<object>();
        mqttDecoder.InvokeDecode(ctx.Object, byteBuf, @out);
        CheckForSingleDecoderException(@out);
    }

    [Fact]
    public void TestConnectMessageNonZeroReservedBit1Mqtt311()
    {
        MqttConnectMessage message = CreateConnectMessage(MqttVersion.MQTT_3_1_1);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);
        byte firstByte = byteBuf.GetByte(0);
        byteBuf.SetByte(0, (byte) (firstByte | 2)); // set bit 1 to 1
        List<object> @out = new List<object>();
        mqttDecoder.InvokeDecode(ctx.Object, byteBuf, @out);
        CheckForSingleDecoderException(@out);
    }

    [Fact]
    public void TestConnectMessageNonZeroReservedBit2Mqtt311()
    {
        MqttConnectMessage message = CreateConnectMessage(MqttVersion.MQTT_3_1_1);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);
        byte firstByte = byteBuf.GetByte(0);
        byteBuf.SetByte(0, (byte) (firstByte | 4)); // set bit 2 to 1
        List<object> @out = new List<object>();
        mqttDecoder.InvokeDecode(ctx.Object, byteBuf, @out);
        CheckForSingleDecoderException(@out);
    }

    [Fact]
    public void TestConnectMessageNonZeroReservedBit3Mqtt311()
    {
        MqttConnectMessage message = CreateConnectMessage(MqttVersion.MQTT_3_1_1);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);
        byte firstByte = byteBuf.GetByte(0);
        byteBuf.SetByte(0, (byte) (firstByte | 8)); // set bit 3 to 1
        List<object> @out = new List<object>();
        mqttDecoder.InvokeDecode(ctx.Object, byteBuf, @out);
        CheckForSingleDecoderException(@out);
    }

    // CLR: Arrays.toString(byte[]) prints the signed bytes "[a, b]" (see MqttConnectPayload.ArraysToString).
    [Fact]
    public void TestConnectMessageForPassword311()
    {
        Assert.False(CreateConnectMessage(MqttVersion.MQTT_3_1).ToString().Contains(ArraysToString(PASSWORD_BYTES)));
    }

    [Fact]
    public void TestConnectMessageForPassword5()
    {
        Assert.False(CreateConnectMessage(MqttVersion.MQTT_5).ToString().Contains(ArraysToString(PASSWORD_BYTES)));
    }

    [Fact]
    public void TestSubscribeMessageNonZeroReservedBit0Mqtt311()
    {
        MqttSubscribeMessage message = CreateSubscribeMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);
        byte firstByte = byteBuf.GetByte(0);
        byteBuf.SetByte(0, (byte) (firstByte | 1)); // set bit 1 to 0
        List<object> @out = new List<object>();
        mqttDecoder.InvokeDecode(ctx.Object, byteBuf, @out);
        CheckForSingleDecoderException(@out);
    }

    [Fact]
    public void TestSubscribeMessageZeroReservedBit1Mqtt311()
    {
        MqttSubscribeMessage message = CreateSubscribeMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);
        byte firstByte = byteBuf.GetByte(0);
        byteBuf.SetByte(0, (byte) (firstByte & ~2)); // set bit 1 to 0
        List<object> @out = new List<object>();
        mqttDecoder.InvokeDecode(ctx.Object, byteBuf, @out);
        CheckForSingleDecoderException(@out);
    }

    private void CheckForSingleDecoderException(IList<object> @out)
    {
        Assert.Equal(1, @out.Count);
        Assert.IsNotAssignableFrom<MqttConnectMessage>(@out[0]);
        MqttMessage result = (MqttMessage) @out[0];
        Assert.IsAssignableFrom<DecoderException>(result.DecoderResult().Cause());
    }

    [Fact]
    public void TestConnectMessagePasswordOnlyForMqtt31()
    {
        MqttConnectMessage message = CreateConnectMessage(
                MqttVersion.MQTT_3_1,
                null,
                PASSWORD,
                MqttProperties.NO_PROPERTIES,
                MqttProperties.NO_PROPERTIES);

        Assert.Throws<EncoderException>(() => MqttEncoder.DoEncode(ctx.Object, message));
    }

    [Fact]
    public void TestConnectMessagePasswordOnlyForMqtt311()
    {
        MqttConnectMessage message = CreateConnectMessage(
                MqttVersion.MQTT_3_1_1,
                null,
                PASSWORD,
                MqttProperties.NO_PROPERTIES,
                MqttProperties.NO_PROPERTIES);

        Assert.Throws<EncoderException>(() => MqttEncoder.DoEncode(ctx.Object, message));
    }

    [Fact]
    public void TestConnectMessagePasswordOnlyForMqtt5()
    {
        MqttConnectMessage message = CreateConnectMessage(
                MqttVersion.MQTT_5,
                null,
                PASSWORD,
                MqttProperties.NO_PROPERTIES,
                MqttProperties.NO_PROPERTIES);

        Assert.False(message.VariableHeader().HasUserName());
        Assert.True(message.VariableHeader().HasPassword());

        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttConnectMessage decodedMessage = (MqttConnectMessage) @out[0];

        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateConnectVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
        ValidateConnectPayload(message.Payload(), decodedMessage.Payload());
    }

    [Fact]
    public void TestConnAckMessage()
    {
        MqttConnAckMessage message = CreateConnAckMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttConnAckMessage decodedMessage = (MqttConnAckMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateConnAckVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
    }

    [Fact]
    public void TestPublishMessage()
    {
        MqttPublishMessage message = CreatePublishMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttPublishMessage decodedMessage = (MqttPublishMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidatePublishVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
        ValidatePublishPayload(message.Payload(), decodedMessage.Payload());
    }

    [Fact]
    public void TestPublishMessageIncompleteVariableHeaderDoesNotUseCumulationSizeForTooLongCheck()
    {
        // The leading PUBLISH is hand-crafted rather than going through MqttEncoder because the
        // bug under test only triggers when variable-header decoding asks for REPLAY mid-message,
        // which in turn requires a deliberately malformed packet (topic-name length prefix larger
        // than the bytes we actually supply). MqttEncoder only produces well-formed messages.
        const int maxBytesInMessage = 16;
        // bytes after the fixed header; < 128 so it fits in a 1-byte Variable Byte Integer.
        const int currentPacketRemainingLength = 10;
        // > the bytes we write below, so the decoder must REPLAY mid-variable-header.
        const int claimedTopicNameLength = 32;
        const int followingPingReqPackets = 3;
        EmbeddedChannel channel = new EmbeddedChannel(new MqttDecoder(maxBytesInMessage));
        ByteBuf byteBuf = ALLOCATOR.Buffer();
        // Leading PUBLISH packet (incomplete - missing most of the topic name):
        // Fixed header byte 1: PUBLISH (type 3), DUP=0, QoS=0, RETAIN=0.
        byteBuf.WriteByte(0x30);
        // Fixed header remaining-length, encoded as a Variable Byte Integer (single byte for values < 128).
        byteBuf.WriteByte(currentPacketRemainingLength);
        // Variable header: 2-byte topic-name length prefix.
        byteBuf.WriteShort(claimedTopicNameLength);
        // ... + only 8 of the 32 topic-name bytes the prefix claims (so the decoder will ask for REPLAY).
        byteBuf.WriteZero(currentPacketRemainingLength - 2);
        // Trailing PINGREQ packets - the cumulation bytes that the buggy size check used to look at.
        // Each PINGREQ is just a 2-byte fixed header: 0xC0 (type 12, flags 0) and remaining-length 0.
        for (int i = 0; i < followingPingReqPackets; i++)
        {
            byteBuf.WriteByte(0xC0);
            byteBuf.WriteByte(0);
        }

        try
        {
            Assert.True(channel.WriteInbound(byteBuf));
            MqttMessage decodedMessage = channel.ReadInbound<MqttMessage>();
            try
            {
                Assert.True(decodedMessage.DecoderResult().IsFailure());
                Assert.IsAssignableFrom<TooLongFrameException>(decodedMessage.DecoderResult().Cause());
            }
            finally
            {
                ReferenceCountUtil.Release(decodedMessage);
            }
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestPublishMessageIncompleteVariableHeaderStillFailsWhenCurrentPacketTooLarge()
    {
        // Same hand-crafting rationale as the test above: a malformed (incomplete-topic) PUBLISH
        // is needed so variable-header decoding asks for REPLAY, which is the code path under test.
        const int maxBytesInMessage = 16;
        // Declared packet size already exceeds the limit; the in-flight check must still flag it.
        const int currentPacketRemainingLength = maxBytesInMessage + 1;
        // > the bytes we write below, so the decoder still asks for REPLAY mid-variable-header.
        const int claimedTopicNameLength = 32;
        EmbeddedChannel channel = new EmbeddedChannel(new MqttDecoder(maxBytesInMessage));
        ByteBuf byteBuf = ALLOCATOR.Buffer();
        // Fixed header byte 1: PUBLISH (type 3), all flags 0.
        byteBuf.WriteByte(0x30);
        // Fixed header remaining-length Variable Byte Integer: 17 (still a single byte since < 128).
        byteBuf.WriteByte(currentPacketRemainingLength);
        // Variable header: 2-byte topic-name length prefix claiming 32 bytes.
        byteBuf.WriteShort(claimedTopicNameLength);
        // ... + 14 zero bytes - fewer than the 32 claimed, so the decoder will ask for REPLAY.
        byteBuf.WriteZero(maxBytesInMessage - 2);

        try
        {
            Assert.True(channel.WriteInbound(byteBuf));
            MqttMessage decodedMessage = channel.ReadInbound<MqttMessage>();
            try
            {
                Assert.True(decodedMessage.DecoderResult().IsFailure());
                Assert.IsAssignableFrom<TooLongFrameException>(decodedMessage.DecoderResult().Cause());
            }
            finally
            {
                ReferenceCountUtil.Release(decodedMessage);
            }
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestConnectMessageWithPropertiesLengthExceedingRemainingLengthIsRejected()
    {
        // Regression test: an MQTT 5 CONNECT whose fixed-header Remaining Length is small (and thus
        // passes the maxBytesInMessage check) must not be allowed to declare a Properties Length that
        // dwarfs it. Otherwise, the ReplayingDecoder would keep buffering network data toward the huge
        // Properties Length, bypassing maxBytesInMessage and exhausting memory. The decoder must reject
        // the frame instead of asking for REPLAY and buffering unboundedly.
        const int maxBytesInMessage = 8092;
        // Small Remaining Length, well under maxBytesInMessage, so the packet-size check passes.
        const int currentPacketRemainingLength = 16;
        EmbeddedChannel channel = new EmbeddedChannel(new MqttDecoder(maxBytesInMessage));
        ByteBuf byteBuf = ALLOCATOR.Buffer();
        // Fixed header byte 1: CONNECT (type 1), all flags 0.
        byteBuf.WriteByte(MqttMessageType.CONNECT.Value() << 4);
        // Fixed header remaining-length Variable Byte Integer: 16 (single byte since < 128).
        byteBuf.WriteByte(currentPacketRemainingLength);
        // Variable header: protocol name "MQTT".
        byteBuf.WriteShort(4);
        byteBuf.WriteBytes(Encoding.UTF8.GetBytes("MQTT"));
        // Protocol level 5 (MQTT 5), so properties are decoded.
        byteBuf.WriteByte(5);
        // Connect flags: none set.
        byteBuf.WriteByte(0);
        // Keep alive.
        byteBuf.WriteShort(60);
        // Properties Length Variable Byte Integer claiming 268,435,455 - the 4-byte VBI maximum,
        // vastly larger than the 16-byte Remaining Length declared above.
        byteBuf.WriteByte(0xFF);
        byteBuf.WriteByte(0xFF);
        byteBuf.WriteByte(0xFF);
        byteBuf.WriteByte(0x7F);

        try
        {
            Assert.True(channel.WriteInbound(byteBuf));
            MqttMessage decodedMessage = channel.ReadInbound<MqttMessage>();
            try
            {
                Assert.True(decodedMessage.DecoderResult().IsFailure());
                Assert.IsAssignableFrom<TooLongFrameException>(decodedMessage.DecoderResult().Cause());
            }
            finally
            {
                ReferenceCountUtil.Release(decodedMessage);
            }
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestPubAckMessage()
    {
        TestMessageWithOnlyFixedHeaderAndMessageIdVariableHeader(MqttMessageType.PUBACK);
    }

    [Fact]
    public void TestPubRecMessage()
    {
        TestMessageWithOnlyFixedHeaderAndMessageIdVariableHeader(MqttMessageType.PUBREC);
    }

    [Fact]
    public void TestPubRelMessage()
    {
        TestMessageWithOnlyFixedHeaderAndMessageIdVariableHeader(MqttMessageType.PUBREL);
    }

    [Fact]
    public void TestPubCompMessage()
    {
        TestMessageWithOnlyFixedHeaderAndMessageIdVariableHeader(MqttMessageType.PUBCOMP);
    }

    [Fact]
    public void TestSubscribeMessage()
    {
        MqttSubscribeMessage message = CreateSubscribeMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttSubscribeMessage decodedMessage = (MqttSubscribeMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateMessageIdVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
        ValidateSubscribePayload(message.Payload(), decodedMessage.Payload());
    }

    [Fact]
    public void TestSubAckMessage()
    {
        MqttSubAckMessage message = CreateSubAckMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttSubAckMessage decodedMessage = (MqttSubAckMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateMessageIdVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
        ValidateSubAckPayload(message.Payload(), decodedMessage.Payload());
    }

    [Fact]
    public void TestSubAckMessageWithFailureInPayload()
    {
        MqttFixedHeader mqttFixedHeader =
                new MqttFixedHeader(MqttMessageType.SUBACK, false, MqttQoS.AT_MOST_ONCE, false, 0);
        MqttMessageIdVariableHeader mqttMessageIdVariableHeader = MqttMessageIdVariableHeader.From(12345);
        MqttSubAckPayload mqttSubAckPayload = new MqttSubAckPayload(MqttQoS.FAILURE.Value());
        MqttSubAckMessage message =
                new MqttSubAckMessage(mqttFixedHeader, mqttMessageIdVariableHeader, mqttSubAckPayload);

        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttSubAckMessage decodedMessage = (MqttSubAckMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateMessageIdVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
        ValidateSubAckPayload(message.Payload(), decodedMessage.Payload());
        Assert.Equal(1, decodedMessage.Payload().GrantedQoSLevels().Count);
        Assert.Equal(MqttQoS.FAILURE, MqttQoS.ValueOf(decodedMessage.Payload().GrantedQoSLevels()[0]));
    }

    [Fact]
    public void TestUnSubscribeMessage()
    {
        MqttUnsubscribeMessage message = CreateUnsubscribeMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttUnsubscribeMessage decodedMessage = (MqttUnsubscribeMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateMessageIdVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
        ValidateUnsubscribePayload(message.Payload(), decodedMessage.Payload());
    }

    [Fact]
    public void TestUnsubAckMessage()
    {
        TestMessageWithOnlyFixedHeaderAndMessageIdVariableHeader(MqttMessageType.UNSUBACK);
    }

    [Fact]
    public void TestPingReqMessage()
    {
        TestMessageWithOnlyFixedHeader(MqttMessage.PINGREQ);
    }

    [Fact]
    public void TestPingRespMessage()
    {
        TestMessageWithOnlyFixedHeader(MqttMessage.PINGRESP);
    }

    [Fact]
    public void TestDisconnectMessage()
    {
        TestMessageWithOnlyFixedHeader(MqttMessage.DISCONNECT);
    }

    [Fact]
    public void TestPingReqWithNonZeroRemainingLengthIsRejected()
    {
        // Regression for https://github.com/netty/netty/issues/16851: PINGREQ is a 2-byte
        // fixed-header-only packet (0xC0 0x00). The bytes below claim Remaining Length 2,
        // which makes the trailing 0xD0 0x00 part of the same (malformed) PINGREQ frame
        // rather than a separate PINGRESP. The decoder must reject this as one invalid
        // message rather than silently accept two.
        EmbeddedChannel channel = new EmbeddedChannel(new MqttDecoder());
        ByteBuf byteBuf = channel.Alloc().Buffer();
        // Fixed header byte 1: PINGREQ (type 12), all flags 0.
        byteBuf.WriteByte(0xC0);
        // Remaining Length 2 - invalid per MQTT 3.1.1 / 5.0 spec (PINGREQ has no variable
        // header or payload, so Remaining Length must be 0).
        byteBuf.WriteByte(0x02);
        // Two leftover bytes still inside the malformed packet's frame:
        byteBuf.WriteByte(0xD0);
        byteBuf.WriteByte(0x00);

        try
        {
            Assert.True(channel.WriteInbound(byteBuf));
            MqttMessage first = channel.ReadInbound<MqttMessage>();
            try
            {
                Assert.True(first.DecoderResult().IsFailure(),
                        "expected a failed message for the malformed PINGREQ");
                Assert.IsAssignableFrom<DecoderException>(first.DecoderResult().Cause());
            }
            finally
            {
                ReferenceCountUtil.Release(first);
            }
            // No second message: the trailing bytes belong to the malformed frame.
            Assert.Null(channel.ReadInbound());
        }
        finally
        {
            Assert.False(channel.FinishAndReleaseAll());
        }
    }

    //All 0..F message type codes are valid in MQTT 5
    [Fact]
    public void TestUnknownMessageType()
    {

        MqttMessage message = CreateMessageWithFixedHeader(MqttMessageType.PINGREQ);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        // setting an invalid message type (15, reserved and forbidden by MQTT 3.1.1 spec)
        byteBuf.SetByte(0, 0xF0);
        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttMessage decodedMessage = (MqttMessage) @out[0];
        Assert.True(decodedMessage.DecoderResult().IsFailure());
        Exception cause = decodedMessage.DecoderResult().Cause();
        Assert.IsAssignableFrom<DecoderException>(cause);
        Assert.Equal("AUTH message requires at least MQTT 5", cause.Message);
    }

    private void VerifyMessageTooLargeException(
        ByteBuf byteBuf, Action<MqttMessage> successfulMessageDecodingChecks)
    {
        for (int i = 1; i < byteBuf.ReadableBytes; i++)
        {
            try
            {
                MqttDecoder decoder = new MqttDecoder(i);
                ByteBuf dup = byteBuf.RetainedDuplicate();
                decoder.ChannelRead(ctx.Object, dup);

                Assert.Equal(1, @out.Count);
                Assert.Equal(0, dup.ReadableBytes);
                MqttMessage decodedMessage = (MqttMessage) @out[0];
                ValidateDecoderExceptionTooLargeMessage(decodedMessage);
            }
            catch (Exception failure)
            {
                ThrowableUtil.AddSuppressed(failure, new Exception(
                    "Size limit: " + i + " bytes of " + byteBuf.ReadableBytes));
                throw;
            }
            finally
            {
                ClearOut();
            }
        }

        // Boundary check
        // CLR: the scope of a C# local is its whole block, so the source's second decoder/decodedMessage locals
        // after the loop would clash with the loop's (CS0136); they are boundaryDecoder/boundaryDecodedMessage.
        MqttDecoder boundaryDecoder = new MqttDecoder(byteBuf.ReadableBytes);
        boundaryDecoder.ChannelRead(ctx.Object, byteBuf);
        Assert.Equal(1, @out.Count);
        Assert.Equal(0, byteBuf.ReadableBytes);
        MqttMessage boundaryDecodedMessage = (MqttMessage) @out[0];
        Assert.True(boundaryDecodedMessage.DecoderResult().IsSuccess());
        successfulMessageDecodingChecks.Invoke(boundaryDecodedMessage);
    }

    [Fact]
    public void TestConnectMessageForMqtt31TooLarge()
    {
        MqttConnectMessage message = CreateConnectMessage(MqttVersion.MQTT_3_1);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        VerifyMessageTooLargeException(byteBuf, decodedMessage =>
        {
            ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
            ValidateConnectVariableHeader(message.VariableHeader(),
                (MqttConnectVariableHeader) decodedMessage.VariableHeader());
            ValidateConnectPayload(message.Payload(), ((MqttConnectMessage) decodedMessage).Payload());
        });
    }

    [Fact]
    public void TestConnectMessageForMqtt311TooLarge()
    {
        MqttConnectMessage message = CreateConnectMessage(MqttVersion.MQTT_3_1_1);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        VerifyMessageTooLargeException(byteBuf, decodedMessage =>
        {
            ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
            ValidateConnectVariableHeader(message.VariableHeader(),
                (MqttConnectVariableHeader) decodedMessage.VariableHeader());
            ValidateConnectPayload(message.Payload(), ((MqttConnectMessage) decodedMessage).Payload());
        });
    }

    [Fact]
    public void TestConnAckMessageTooLarge()
    {
        MqttConnAckMessage message = CreateConnAckMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        VerifyMessageTooLargeException(byteBuf, decodedMessage =>
        {
            ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
            ValidateConnAckVariableHeader(message.VariableHeader(),
                (MqttConnAckVariableHeader) decodedMessage.VariableHeader());
        });
    }

    [Fact]
    public void TestPublishMessageTooLarge()
    {
        MqttPublishMessage message = CreatePublishMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        VerifyMessageTooLargeException(byteBuf, decodedMessage =>
        {
            ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
            ValidatePublishVariableHeader(message.VariableHeader(),
                (MqttPublishVariableHeader) decodedMessage.VariableHeader());
            ValidatePublishPayload(message.Content, ((MqttPublishMessage) decodedMessage).Content);
        });
    }

    [Fact]
    public void TestSubscribeMessageTooLarge()
    {
        MqttSubscribeMessage message = CreateSubscribeMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        VerifyMessageTooLargeException(byteBuf, decodedMessage =>
        {
            ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
            ValidateMessageIdVariableHeader(message.VariableHeader(),
                (MqttMessageIdVariableHeader) decodedMessage.VariableHeader());
        });
    }

    [Fact]
    public void TestSubAckMessageTooLarge()
    {
        MqttSubAckMessage message = CreateSubAckMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        VerifyMessageTooLargeException(byteBuf, decodedMessage =>
        {
            ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
            ValidateMessageIdVariableHeader(message.VariableHeader(),
                (MqttMessageIdVariableHeader) decodedMessage.VariableHeader());
            ValidateSubAckPayload(message.Payload(), ((MqttSubAckMessage) decodedMessage).Payload());
        });
    }

    [Fact]
    public void TestUnSubscribeMessageTooLarge()
    {
        MqttUnsubscribeMessage message = CreateUnsubscribeMessage();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        VerifyMessageTooLargeException(byteBuf, decodedMessage =>
        {
            ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
            ValidateMessageIdVariableHeader(message.VariableHeader(),
                (MqttMessageIdVariableHeader) decodedMessage.VariableHeader());
            ValidateUnsubscribePayload(message.Payload(), ((MqttUnsubscribeMessage) decodedMessage).Payload());
        });
    }

    [Fact]
    public void TestConnectMessageForMqtt5()
    {
        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.IntegerProperty(SESSION_EXPIRY_INTERVAL, 10));
        props.Add(new MqttProperties.StringProperty(AUTHENTICATION_METHOD, "Plain"));
        MqttProperties willProps = new MqttProperties();
        willProps.Add(new MqttProperties.IntegerProperty(WILL_DELAY_INTERVAL, 100));
        MqttConnectMessage message =
                CreateConnectMessage(MqttVersion.MQTT_5, USER_NAME, PASSWORD, props, willProps);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttConnectMessage decodedMessage = (MqttConnectMessage) @out[0];

        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateConnectVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
        ValidateConnectPayload(message.Payload(), decodedMessage.Payload());
    }

    [Fact]
    public void TestConnAckMessageForMqtt5()
    {
        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.IntegerProperty(SESSION_EXPIRY_INTERVAL, 10));
        props.Add(new MqttProperties.IntegerProperty(MAXIMUM_QOS, 1));
        props.Add(new MqttProperties.IntegerProperty(MAXIMUM_PACKET_SIZE, 1000));
        MqttConnAckMessage message = CreateConnAckMessage(props);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttConnAckMessage decodedMessage = (MqttConnAckMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateConnAckVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
    }

    [Fact]
    public void TestPublishMessageForMqtt5()
    {
        versionAttrMock.Setup(a => a.Get()).Returns(MqttVersion.MQTT_5);
        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.IntegerProperty(SUBSCRIPTION_IDENTIFIER, 10));
        props.Add(new MqttProperties.IntegerProperty(SUBSCRIPTION_IDENTIFIER, 20));
        props.Add(new MqttProperties.IntegerProperty(PAYLOAD_FORMAT_INDICATOR, 6));
        props.Add(new MqttProperties.UserProperty("isSecret", "true"));
        props.Add(new MqttProperties.UserProperty("tag", "firstTag"));
        props.Add(new MqttProperties.UserProperty("tag", "secondTag"));
        Assert.Equal(2,
                props.GetProperties(SUBSCRIPTION_IDENTIFIER).Count);
        Assert.Equal(3,
                props.GetProperties(USER_PROPERTY).Count);
        Assert.Equal(3,
                ((MqttProperties.UserProperties) props.GetProperty(USER_PROPERTY)).value.Count);
        MqttPublishMessage message = CreatePublishMessage(props);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttPublishMessage decodedMessage = (MqttPublishMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidatePublishVariableHeader(message.VariableHeader(), decodedMessage.VariableHeader());
        ValidatePublishPayload(message.Payload(), decodedMessage.Payload());
    }

    // See https://github.com/netty/netty MQTT 5 properties-length decoding.
    // When the MQTT 5 Property Length is >= 128 (so it is encoded as a multi-byte Variable Byte
    // Integer) AND the last property is a single-byte value property (e.g. PAYLOAD_FORMAT_INDICATOR),
    // decodeProperties() used to stop reading one Variable-Byte-Integer-length worth of content bytes
    // too early. Those unread property bytes (here 0x01 0x01) then leaked into the PUBLISH payload.
    [Fact]
    public void TestPublishMqtt5MultiBytePropertiesLengthWithTrailingSingleByteProperty()
    {
        const string expectedPayload = "{\"complete\":1}";
        EmbeddedChannel channel = new EmbeddedChannel(new MqttDecoder());
        try
        {
            // The MQTT version is read from a channel attribute set while decoding CONNECT. Without a
            // preceding MQTT 5 CONNECT, getMqttVersion() defaults to 3.1.1 and properties are never
            // decoded, so the PUBLISH must follow a CONNECT with protocol level 5 on the same channel.
            Assert.True(channel.WriteInbound(NewMqtt5Connect()));
            ReferenceCountUtil.Release(channel.ReadInbound());

            // Build a properties block > 127 bytes (so its length needs a 2-byte VBI) using several
            // User Properties, with a single-byte PAYLOAD_FORMAT_INDICATOR as the very last property.
            ByteBuf properties = ALLOCATOR.Buffer();
            int i = 0;
            while (properties.ReadableBytes < 130)
            {
                properties.WriteByte(USER_PROPERTY);
                WriteMqttUtf8String(properties, "key" + i);
                WriteMqttUtf8String(properties, "value-with-some-padding-" + i);
                i++;
            }
            properties.WriteByte(PAYLOAD_FORMAT_INDICATOR); // 0x01
            properties.WriteByte(0x01);                      // its single-byte value
            Assert.True(properties.ReadableBytes >= 128); // 2-byte VBI trigger condition

            Assert.True(channel.WriteInbound(NewPublish("topic", properties, expectedPayload)));

            MqttPublishMessage decoded = channel.ReadInbound<MqttPublishMessage>();
            try
            {
                Assert.False(decoded.DecoderResult().IsFailure(), decoded.DecoderResult().Cause()?.ToString() ?? "null");
                // The payload must be exactly the JSON, without the leaked 0x01 0x01 property bytes in front.
                Assert.Equal(expectedPayload, decoded.Payload().ToString(Encoding.UTF8));
                // ... and the PAYLOAD_FORMAT_INDICATOR must have been decoded as a property.
                Assert.NotNull(decoded.VariableHeader().Properties().GetProperty(PAYLOAD_FORMAT_INDICATOR));
            }
            finally
            {
                ReferenceCountUtil.Release(decoded);
            }
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    // Control case: a properties block < 128 bytes (single-byte VBI length) with the single-byte
    // PAYLOAD_FORMAT_INDICATOR NOT last. This layout decodes correctly both before and after the fix.
    [Fact]
    public void TestPublishMqtt5SingleBytePropertiesLengthIsUnaffected()
    {
        const string expectedPayload = "{\"complete\":1}";
        EmbeddedChannel channel = new EmbeddedChannel(new MqttDecoder());
        try
        {
            Assert.True(channel.WriteInbound(NewMqtt5Connect()));
            ReferenceCountUtil.Release(channel.ReadInbound());

            ByteBuf properties = ALLOCATOR.Buffer();
            properties.WriteByte(PAYLOAD_FORMAT_INDICATOR); // 0x01, placed first (not last)
            properties.WriteByte(0x01);
            properties.WriteByte(USER_PROPERTY);
            WriteMqttUtf8String(properties, "tag");
            WriteMqttUtf8String(properties, "value");
            Assert.True(properties.ReadableBytes < 128); // single-byte VBI length

            Assert.True(channel.WriteInbound(NewPublish("topic", properties, expectedPayload)));

            MqttPublishMessage decoded = channel.ReadInbound<MqttPublishMessage>();
            try
            {
                Assert.False(decoded.DecoderResult().IsFailure(), decoded.DecoderResult().Cause()?.ToString() ?? "null");
                Assert.Equal(expectedPayload, decoded.Payload().ToString(Encoding.UTF8));
                Assert.NotNull(decoded.VariableHeader().Properties().GetProperty(PAYLOAD_FORMAT_INDICATOR));
            }
            finally
            {
                ReferenceCountUtil.Release(decoded);
            }
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestPublishMqtt5PropertyLengthOverreadIsRejected()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new MqttDecoder());
        try
        {
            Assert.True(channel.WriteInbound(NewMqtt5Connect()));
            ReferenceCountUtil.Release(channel.ReadInbound());

            // 30 0a 00 01 61 02 03 00 03 78 79 7a
            ByteBuf malformed = channel.Alloc().Buffer();
            malformed.WriteByte(0x30);      // PUBLISH, DUP=0, QoS=0, RETAIN=0
            malformed.WriteByte(0x0a);      // Remaining Length = 10
            WriteMqttUtf8String(malformed, "a"); // topic name "a"
            malformed.WriteByte(0x02);      // Property Length = 2 (too short for what follows)
            malformed.WriteByte(CONTENT_TYPE);   // 0x03: starts a Content Type string property
            malformed.WriteByte(0x00);      // string length prefix, msb -- Properties section ends here
            malformed.WriteByte(0x03);      // string length prefix lsb -- actually outside Property Length
            malformed.WriteByte('x');
            malformed.WriteByte('y');
            malformed.WriteByte('z');

            Assert.True(channel.WriteInbound(malformed));

            MqttMessage decoded = channel.ReadInbound<MqttMessage>();
            try
            {
                Assert.True(decoded.DecoderResult().IsFailure());
                Assert.IsAssignableFrom<DecoderException>(decoded.DecoderResult().Cause());
            }
            finally
            {
                ReferenceCountUtil.Release(decoded);
            }
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestPublishMqtt5PropertyLengthCoveringFullStringIsAccepted()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new MqttDecoder());
        try
        {
            Assert.True(channel.WriteInbound(NewMqtt5Connect()));
            ReferenceCountUtil.Release(channel.ReadInbound());

            // 30 0a 00 01 61 06 03 00 03 78 79 7a
            ByteBuf wellFormed = channel.Alloc().Buffer();
            wellFormed.WriteByte(0x30);
            wellFormed.WriteByte(0x0a);
            WriteMqttUtf8String(wellFormed, "a");
            wellFormed.WriteByte(0x06);      // Property Length = 6, covers the full Content Type string
            wellFormed.WriteByte(CONTENT_TYPE);
            WriteMqttUtf8String(wellFormed, "xyz");

            Assert.True(channel.WriteInbound(wellFormed));

            MqttPublishMessage decoded = channel.ReadInbound<MqttPublishMessage>();
            try
            {
                Assert.False(decoded.DecoderResult().IsFailure());
                Assert.Equal("a", decoded.VariableHeader().TopicName());
                Assert.Equal("xyz",
                    ((MqttProperties.StringProperty) decoded.VariableHeader()
                        .Properties().GetProperty(CONTENT_TYPE)).value);
                Assert.Equal(0, decoded.Payload().ReadableBytes);
            }
            finally
            {
                ReferenceCountUtil.Release(decoded);
            }
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    // Minimal well-formed MQTT 5 CONNECT (empty client id) whose only purpose is to make the decoder
    // record MQTT 5 for the channel so subsequent packets have their properties decoded.
    private static ByteBuf NewMqtt5Connect()
    {
        ByteBuf variablePart = ALLOCATOR.Buffer();
        WriteMqttUtf8String(variablePart, "MQTT"); // protocol name
        variablePart.WriteByte(5);                 // protocol level = MQTT 5
        variablePart.WriteByte(0x02);              // connect flags: clean start
        variablePart.WriteShort(0);                // keep alive
        variablePart.WriteByte(0);                 // CONNECT properties length = 0
        WriteMqttUtf8String(variablePart, "");     // empty client id (allowed for MQTT 5)

        ByteBuf connect = ALLOCATOR.Buffer();
        connect.WriteByte(0x10);                   // CONNECT, flags 0
        WriteVariableByteInteger(connect, variablePart.ReadableBytes);
        connect.WriteBytes(variablePart);
        variablePart.Release();
        return connect;
    }

    // Builds a QoS 0 PUBLISH from a raw properties block and a UTF-8 payload. Releases propertiesContent.
    private static ByteBuf NewPublish(string topic, ByteBuf propertiesContent, string payload)
    {
        ByteBuf variablePart = ALLOCATOR.Buffer();
        WriteMqttUtf8String(variablePart, topic);  // topic name (QoS 0 -> no packet id)
        WriteVariableByteInteger(variablePart, propertiesContent.ReadableBytes); // property length
        variablePart.WriteBytes(propertiesContent);
        propertiesContent.Release();
        variablePart.WriteBytes(Encoding.UTF8.GetBytes(payload));

        ByteBuf publish = ALLOCATOR.Buffer();
        publish.WriteByte(0x30);                    // PUBLISH, DUP=0, QoS=0, RETAIN=0
        WriteVariableByteInteger(publish, variablePart.ReadableBytes);
        publish.WriteBytes(variablePart);
        variablePart.Release();
        return publish;
    }

    private static void WriteMqttUtf8String(ByteBuf buffer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        buffer.WriteShort(bytes.Length);
        buffer.WriteBytes(bytes);
    }

    private static void WriteVariableByteInteger(ByteBuf buffer, int value)
    {
        do
        {
            int digit = value & 0x7F;
            value >>>= 7;
            if (value > 0)
            {
                digit |= 0x80;
            }
            buffer.WriteByte(digit);
        } while (value > 0);
    }

    [Fact]
    public void TestPubAckMessageForMqtt5()
    {
        versionAttrMock.Setup(a => a.Get()).Returns(MqttVersion.MQTT_5);
        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.IntegerProperty(PAYLOAD_FORMAT_INDICATOR, 6));
        //0x87 - Not Authorized
        MqttMessage message = CreatePubAckMessage((byte) 0x87, props);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttMessage decodedMessage = (MqttMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidatePubReplyVariableHeader((MqttPubReplyMessageVariableHeader) message.VariableHeader(),
                (MqttPubReplyMessageVariableHeader) decodedMessage.VariableHeader());
    }

    [Fact]
    public void TestPubAckMessageSkipCodeForMqtt5()
    {
        //Code 0 (Success) and no properties - skip encoding code and properties
        MqttMessage message = CreatePubAckMessage((byte) 0, MqttProperties.NO_PROPERTIES);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttMessage decodedMessage = (MqttMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidatePubReplyVariableHeader((MqttPubReplyMessageVariableHeader) message.VariableHeader(),
                (MqttPubReplyMessageVariableHeader) decodedMessage.VariableHeader());
    }

    [Fact]
    public void TestPubAckMessageWithUserPropertyAndSuccessForMqtt5()
    {
        versionAttrMock.Setup(a => a.Get()).Returns(MqttVersion.MQTT_5);

        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.UserProperty("traceId", "abc"));
        MqttMessage message = CreatePubAckMessage((byte) 0, props);
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttMessage decodedMessage = (MqttMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidatePubReplyVariableHeader((MqttPubReplyMessageVariableHeader) message.VariableHeader(),
                (MqttPubReplyMessageVariableHeader) decodedMessage.VariableHeader());
    }

    [Fact]
    public void TestSubAckMessageForMqtt5()
    {
        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.IntegerProperty(PAYLOAD_FORMAT_INDICATOR, 6));
        MqttSubAckMessage message = CreateSubAckMessage(new int[] {1, 2, 0, 0x87 /* not authorized */});
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttSubAckMessage decodedMessage = (MqttSubAckMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidatePacketIdAndPropertiesVariableHeader(
                (MqttMessageIdAndPropertiesVariableHeader) message.VariableHeader(),
                (MqttMessageIdAndPropertiesVariableHeader) decodedMessage.VariableHeader());
        ValidateSubAckPayload(message.Payload(), decodedMessage.Payload());
        Assert.Equal(new int[] {1, 2, 0, 0x80},
                decodedMessage.Payload().GrantedQoSLevels().ToArray());
    }

    [Fact]
    public void TestSubscribeMessageForMqtt5()
    {
        versionAttrMock.Setup(a => a.Get()).Returns(MqttVersion.MQTT_5);

        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.IntegerProperty(PAYLOAD_FORMAT_INDICATOR, 6));
        MqttSubscribeMessage message = MqttMessageBuilders.Subscribe()
                .MessageId((short) 1)
                .Properties(props)
                .AddSubscription("/topic", new MqttSubscriptionOption(AT_LEAST_ONCE,
                        true,
                        true,
                        SEND_AT_SUBSCRIBE_IF_NOT_YET_EXISTS))
                .Build();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);
        MqttSubscribeMessage decodedMessage = (MqttSubscribeMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        MqttMessageIdAndPropertiesVariableHeader expectedHeader =
                (MqttMessageIdAndPropertiesVariableHeader) message.VariableHeader();
        MqttMessageIdAndPropertiesVariableHeader actualHeader =
                (MqttMessageIdAndPropertiesVariableHeader) decodedMessage.VariableHeader();
        ValidatePacketIdAndPropertiesVariableHeader(expectedHeader, actualHeader);
        ValidateSubscribePayload(message.Payload(), decodedMessage.Payload());
    }

    [Fact]
    public void TestSubscribeMessageMqtt5EncodeAsMqtt3()
    {
        versionAttrMock.Setup(a => a.Get()).Returns(MqttVersion.MQTT_3_1_1);

        //Set parameters only available in MQTT5 to see if they're dropped when encoding as MQTT3
        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.IntegerProperty(PAYLOAD_FORMAT_INDICATOR, 6));
        MqttSubscribeMessage message = MqttMessageBuilders.Subscribe()
                .MessageId((short) 1)
                .Properties(props)
                .AddSubscription("/topic", new MqttSubscriptionOption(AT_LEAST_ONCE,
                        true,
                        true,
                        SEND_AT_SUBSCRIBE_IF_NOT_YET_EXISTS))
                .Build();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);
        MqttSubscribeMessage decodedMessage = (MqttSubscribeMessage) @out[0];

        MqttSubscribeMessage expectedMessage = MqttMessageBuilders.Subscribe()
                .MessageId((short) 1)
                .AddSubscription("/topic", MqttSubscriptionOption.OnlyFromQos(AT_LEAST_ONCE))
                .Build();
        ValidateFixedHeaders(expectedMessage.FixedHeader(), decodedMessage.FixedHeader());
        MqttMessageIdAndPropertiesVariableHeader expectedHeader =
                (MqttMessageIdAndPropertiesVariableHeader) expectedMessage.VariableHeader();
        MqttMessageIdAndPropertiesVariableHeader actualHeader =
                (MqttMessageIdAndPropertiesVariableHeader) decodedMessage.VariableHeader();
        ValidatePacketIdAndPropertiesVariableHeader(expectedHeader, actualHeader);
        ValidateSubscribePayload(expectedMessage.Payload(), decodedMessage.Payload());
    }

    [Fact]
    public void TestUnsubAckMessageForMqtt5()
    {
        versionAttrMock.Setup(a => a.Get()).Returns(MqttVersion.MQTT_5);

        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.IntegerProperty(PAYLOAD_FORMAT_INDICATOR, 6));
        MqttUnsubAckMessage message = MqttMessageBuilders.UnsubAck()
                .PacketId(1)
                .Properties(props)
                .AddReasonCode((short) 0x83)
                .Build();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttUnsubAckMessage decodedMessage = (MqttUnsubAckMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidatePacketIdAndPropertiesVariableHeader(
                (MqttMessageIdAndPropertiesVariableHeader) message.VariableHeader(),
                (MqttMessageIdAndPropertiesVariableHeader) decodedMessage.VariableHeader());
        Assert.Equal(message.Payload().UnsubscribeReasonCodes(),
                decodedMessage.Payload().UnsubscribeReasonCodes());
    }

    [Fact]
    public void TestUnsubAckMessageForMqtt311DropsReasonCodes()
    {
        // MQTT 3.1.1 UNSUBACK has no properties and no payload. Even when a caller populates
        // properties / reason codes (for example via MqttMessageBuilders), the encoder must
        // strip them so the wire format remains valid for 3.1.1 (Remaining Length must be 2).
        versionAttrMock.Setup(a => a.Get()).Returns(MqttVersion.MQTT_3_1_1);

        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.IntegerProperty(PAYLOAD_FORMAT_INDICATOR, 6));
        MqttUnsubAckMessage message = MqttMessageBuilders.UnsubAck()
                .PacketId(1)
                .Properties(props)
                .AddReasonCode((short) 0x83)
                .Build();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        // Fixed header (1 byte) + Remaining Length (0x02) + Packet Identifier (2 bytes) = 4 bytes.
        Assert.Equal(4, byteBuf.ReadableBytes);
        Assert.Equal(MqttMessageType.UNSUBACK.Value() << 4, byteBuf.GetByte(0) & 0xF0);
        Assert.Equal(2, byteBuf.GetByte(1));
        Assert.Equal(1, byteBuf.GetUnsignedShort(2));

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttUnsubAckMessage decodedMessage = (MqttUnsubAckMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateMessageIdVariableHeader(
                message.VariableHeader(),
                decodedMessage.VariableHeader());
        Assert.True(decodedMessage.Payload() == null
                || decodedMessage.Payload().UnsubscribeReasonCodes().Count == 0);
    }

    [Fact]
    public void TestDisconnectMessageForMqtt5()
    {
        versionAttrMock.Setup(a => a.Get()).Returns(MqttVersion.MQTT_5);

        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.IntegerProperty(SESSION_EXPIRY_INTERVAL, 6));
        MqttMessage message = MqttMessageBuilders.Disconnect()
                .ReasonCode((byte) 0x96) // Message rate too high
                .Properties(props)
                .Build();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);
        MqttMessage decodedMessage = (MqttMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateReasonCodeAndPropertiesVariableHeader(
                (MqttReasonCodeAndPropertiesVariableHeader) message.VariableHeader(),
                (MqttReasonCodeAndPropertiesVariableHeader) decodedMessage.VariableHeader());
    }

    [Fact]
    public void TestDisconnectMessageSkipCodeForMqtt5()
    {
        //code 0 and no properties - skip encoding code and properties
        MqttMessage message = MqttMessageBuilders.Disconnect()
                .ReasonCode((byte) 0) // ok
                .Properties(MqttProperties.NO_PROPERTIES)
                .Build();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);
        MqttMessage decodedMessage = (MqttMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateReasonCodeAndPropertiesVariableHeader(
                (MqttReasonCodeAndPropertiesVariableHeader) message.VariableHeader(),
                (MqttReasonCodeAndPropertiesVariableHeader) decodedMessage.VariableHeader());
    }

    [Fact]
    public void TestDisconnectMessageWithUserPropertyAndSuccessForMqtt5()
    {
        versionAttrMock.Setup(a => a.Get()).Returns(MqttVersion.MQTT_5);

        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.UserProperty("traceId", "abc"));
        MqttMessage message = MqttMessageBuilders.Disconnect()
                .ReasonCode((byte) 0)
                .Properties(props)
                .Build();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);
        MqttMessage decodedMessage = (MqttMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateReasonCodeAndPropertiesVariableHeader(
                (MqttReasonCodeAndPropertiesVariableHeader) message.VariableHeader(),
                (MqttReasonCodeAndPropertiesVariableHeader) decodedMessage.VariableHeader());
    }

    [Fact]
    public void TestAuthMessageForMqtt5()
    {
        versionAttrMock.Setup(a => a.Get()).Returns(MqttVersion.MQTT_5);

        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.BinaryProperty(AUTHENTICATION_DATA, Encoding.UTF8.GetBytes("secret")));
        MqttMessage message = MqttMessageBuilders.Auth()
                .ReasonCode((byte) 0x18) // Continue authentication
                .Properties(props)
                .Build();
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);
        MqttMessage decodedMessage = (MqttMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateReasonCodeAndPropertiesVariableHeader(
                (MqttReasonCodeAndPropertiesVariableHeader) message.VariableHeader(),
                (MqttReasonCodeAndPropertiesVariableHeader) decodedMessage.VariableHeader());
    }

    [Fact]
    public void TestMqttVersionDetection()
    {
        versionAttrMock.Invocations.Clear();
        //Encode CONNECT message so that encoder would initialize its version
        MqttConnectMessage connectMessage = CreateConnectMessage(MqttVersion.MQTT_5);
        ByteBuf connectByteBuf = MqttEncoder.DoEncode(ctx.Object, connectMessage);

        versionAttrMock.Verify(a => a.Set(MqttVersion.MQTT_5), Times.Once());
        versionAttrMock.Invocations.Clear();

        mqttDecoder.ChannelRead(ctx.Object, connectByteBuf);

        versionAttrMock.Verify(a => a.Set(MqttVersion.MQTT_5), Times.Once());

        Assert.Equal(@out.Count, 1, @out.Count.ToString(CultureInfo.InvariantCulture));

        MqttConnectMessage decodedConnectMessage = (MqttConnectMessage) @out[0];

        ValidateFixedHeaders(connectMessage.FixedHeader(), decodedConnectMessage.FixedHeader());
        ValidateConnectVariableHeader(connectMessage.VariableHeader(), decodedConnectMessage.VariableHeader());
        ValidateConnectPayload(connectMessage.Payload(), decodedConnectMessage.Payload());

        versionAttrMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void TestUnknownMessagePayload()
    {
        MqttMessage message = CreatePubAckMessage(MqttReasonCodes.PubAck.SUCCESS.ByteValue(), null);

        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);
        byteBuf.WriteBytes(Encoding.UTF8.GetBytes("whatever"));

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(2, @out.Count);

        MqttMessage decodedMessage = (MqttMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidatePubReplyVariableHeader((MqttPubReplyMessageVariableHeader) message.VariableHeader(),
                                       (MqttPubReplyMessageVariableHeader) decodedMessage.VariableHeader());
        Assert.Null(decodedMessage.Payload());

        MqttMessage failedMessage = (MqttMessage) @out[1];
        Assert.Null(failedMessage.FixedHeader());
        Assert.Null(failedMessage.VariableHeader());
        Assert.Null(failedMessage.Payload());
        Assert.True(failedMessage.DecoderResult().IsFailure());
    }

    private void TestMessageWithOnlyFixedHeader(MqttMessage message)
    {
        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(1, @out.Count);

        MqttMessage decodedMessage = (MqttMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
    }

    private void TestMessageWithOnlyFixedHeaderAndMessageIdVariableHeader(MqttMessageType messageType)
    {
        MqttMessage message = CreateMessageWithFixedHeaderAndMessageIdVariableHeader(messageType);

        ByteBuf byteBuf = MqttEncoder.DoEncode(ctx.Object, message);

        mqttDecoder.ChannelRead(ctx.Object, byteBuf);

        Assert.Equal(@out.Count, 1, @out.Count.ToString(CultureInfo.InvariantCulture));

        MqttMessage decodedMessage = (MqttMessage) @out[0];
        ValidateFixedHeaders(message.FixedHeader(), decodedMessage.FixedHeader());
        ValidateMessageIdVariableHeader(
                (MqttMessageIdVariableHeader) message.VariableHeader(),
                (MqttMessageIdVariableHeader) decodedMessage.VariableHeader());
    }

    // Factory methods of different MQTT
    // Message types to help testing

    private static MqttMessage CreateMessageWithFixedHeader(MqttMessageType messageType)
    {
        return new MqttMessage(new MqttFixedHeader(messageType, false, MqttQoS.AT_MOST_ONCE, false, 0));
    }

    private static MqttMessage CreateMessageWithFixedHeaderAndMessageIdVariableHeader(MqttMessageType messageType)
    {
        MqttFixedHeader mqttFixedHeader =
                new MqttFixedHeader(
                        messageType,
                        false,
                        messageType == MqttMessageType.PUBREL ? MqttQoS.AT_LEAST_ONCE : MqttQoS.AT_MOST_ONCE,
                        false,
                        0);
        MqttMessageIdVariableHeader mqttMessageIdVariableHeader = MqttMessageIdVariableHeader.From(12345);
        return new MqttMessage(mqttFixedHeader, mqttMessageIdVariableHeader);
    }

    private static MqttConnectMessage CreateConnectMessage(MqttVersion mqttVersion)
    {
        return CreateConnectMessage(mqttVersion,
                USER_NAME,
                PASSWORD,
                MqttProperties.NO_PROPERTIES,
                MqttProperties.NO_PROPERTIES);
    }

    private static MqttConnectMessage CreateConnectMessage(MqttVersion mqttVersion,
                                                           string username,
                                                           string password,
                                                           MqttProperties properties,
                                                           MqttProperties willProperties)
    {
        return MqttMessageBuilders.Connect()
                .ClientId(CLIENT_ID)
                .ProtocolVersion(mqttVersion)
                .Username(username)
                .Password(Encoding.UTF8.GetBytes(password))
                .Properties(properties)
                .WillRetain(true)
                .WillQoS(MqttQoS.AT_LEAST_ONCE)
                .WillFlag(true)
                .WillTopic(WILL_TOPIC)
                .WillMessage(Encoding.UTF8.GetBytes(WILL_MESSAGE))
                .WillProperties(willProperties)
                .CleanSession(true)
                .KeepAlive(KEEP_ALIVE_SECONDS)
                .Build();
    }

    private static MqttConnAckMessage CreateConnAckMessage()
    {
        return CreateConnAckMessage(MqttProperties.NO_PROPERTIES);
    }

    private static MqttConnAckMessage CreateConnAckMessage(MqttProperties properties)
    {
        return MqttMessageBuilders.ConnAck()
                .ReturnCode(MqttConnectReturnCode.CONNECTION_ACCEPTED)
                .Properties(properties)
                .SessionPresent(true)
                .Build();
    }

    private static MqttPublishMessage CreatePublishMessage()
    {
        return CreatePublishMessage(MqttProperties.NO_PROPERTIES);
    }

    private static MqttPublishMessage CreatePublishMessage(MqttProperties properties)
    {
        MqttFixedHeader mqttFixedHeader =
                new MqttFixedHeader(MqttMessageType.PUBLISH, false, MqttQoS.AT_LEAST_ONCE, true, 0);
        MqttPublishVariableHeader mqttPublishVariableHeader = new MqttPublishVariableHeader("/abc",
                1234,
                properties);
        ByteBuf payload = ALLOCATOR.Buffer();
        payload.WriteBytes(Encoding.UTF8.GetBytes("whatever"));
        return new MqttPublishMessage(mqttFixedHeader, mqttPublishVariableHeader, payload);
    }

    private static MqttSubscribeMessage CreateSubscribeMessage()
    {
        MqttFixedHeader mqttFixedHeader =
                new MqttFixedHeader(MqttMessageType.SUBSCRIBE, false, MqttQoS.AT_LEAST_ONCE, false, 0);
        MqttMessageIdVariableHeader mqttMessageIdVariableHeader = MqttMessageIdVariableHeader.From(12345);

        List<MqttTopicSubscription> topicSubscriptions = new List<MqttTopicSubscription>();
        topicSubscriptions.Add(new MqttTopicSubscription("/abc", MqttQoS.AT_LEAST_ONCE));
        topicSubscriptions.Add(new MqttTopicSubscription("/def", MqttQoS.AT_LEAST_ONCE));
        topicSubscriptions.Add(new MqttTopicSubscription("/xyz", MqttQoS.EXACTLY_ONCE));

        MqttSubscribePayload mqttSubscribePayload = new MqttSubscribePayload(topicSubscriptions);
        return new MqttSubscribeMessage(mqttFixedHeader, mqttMessageIdVariableHeader, mqttSubscribePayload);
    }

    private static MqttSubAckMessage CreateSubAckMessage()
    {
        return CreateSubAckMessage(new int[] {1, 2, 0});
    }

    private static MqttSubAckMessage CreateSubAckMessage(int[] reasonCodes)
    {
        MqttFixedHeader mqttFixedHeader =
                new MqttFixedHeader(MqttMessageType.SUBACK, false, MqttQoS.AT_MOST_ONCE, false, 0);
        MqttMessageIdVariableHeader mqttMessageIdVariableHeader = MqttMessageIdVariableHeader.From(12345);
        MqttSubAckPayload mqttSubAckPayload = new MqttSubAckPayload(reasonCodes);
        return new MqttSubAckMessage(mqttFixedHeader, mqttMessageIdVariableHeader, mqttSubAckPayload);
    }

    private static MqttUnsubscribeMessage CreateUnsubscribeMessage()
    {
        MqttFixedHeader mqttFixedHeader =
                new MqttFixedHeader(MqttMessageType.UNSUBSCRIBE, false, MqttQoS.AT_LEAST_ONCE, false, 0);
        MqttMessageIdVariableHeader mqttMessageIdVariableHeader = MqttMessageIdVariableHeader.From(12345);

        List<string> topics = new List<string>();
        topics.Add("/abc");
        topics.Add("/def");
        topics.Add("/xyz");

        MqttUnsubscribePayload mqttUnsubscribePayload = new MqttUnsubscribePayload(topics);
        return new MqttUnsubscribeMessage(mqttFixedHeader, mqttMessageIdVariableHeader, mqttUnsubscribePayload);
    }

    private MqttMessage CreatePubAckMessage(byte reasonCode, MqttProperties properties)
    {
        return MqttMessageBuilders.PubAck()
                .PacketId(1)
                .ReasonCode(reasonCode)
                .Properties(properties)
                .Build();
    }

    // Helper methods to compare expected and actual
    // MQTT messages

    private static void ValidateFixedHeaders(MqttFixedHeader expected, MqttFixedHeader actual)
    {
        Assert.Equal(expected.MessageType(), actual.MessageType());
        Assert.Equal(expected.QosLevel(), actual.QosLevel());
    }

    private static void ValidateConnectVariableHeader(
            MqttConnectVariableHeader expected,
            MqttConnectVariableHeader actual)
    {
        Assert.Equal(expected.Name(), actual.Name());
        Assert.Equal(expected.KeepAliveTimeSeconds(), actual.KeepAliveTimeSeconds());
        Assert.Equal(expected.Version(), actual.Version());
        Assert.Equal(expected.Version(), actual.Version());
        ValidateProperties(expected.Properties(), actual.Properties());
        Assert.Equal(expected.WillQos(), actual.WillQos());

        Assert.Equal(expected.HasUserName(), actual.HasUserName());
        Assert.Equal(expected.HasPassword(), actual.HasPassword());
        Assert.Equal(expected.IsCleanSession(), actual.IsCleanSession());
        Assert.Equal(expected.IsWillFlag(), actual.IsWillFlag());
        Assert.Equal(expected.IsWillRetain(), actual.IsWillRetain());
    }

    private static void ValidateConnectPayload(MqttConnectPayload expected, MqttConnectPayload actual)
    {
        Assert.Equal(expected.ClientIdentifier(), actual.ClientIdentifier());
        Assert.Equal(expected.UserName(), actual.UserName());
        // CLR: assertEquals(expected.password(), actual.password()) used the not-ported deprecated accessor; its
        // replacement passwordInBytes() is asserted next.
        Assert.Equal(expected.PasswordInBytes(), actual.PasswordInBytes());
        // CLR: assertEquals(expected.willMessage(), actual.willMessage()) used the not-ported deprecated accessor; its
        // replacement willMessageInBytes() is asserted next.
        Assert.Equal(expected.WillMessageInBytes(), actual.WillMessageInBytes());
        Assert.Equal(expected.WillTopic(), actual.WillTopic());
        ValidateProperties(expected.WillProperties(), actual.WillProperties());
    }

    private static void ValidateConnAckVariableHeader(
            MqttConnAckVariableHeader expected,
            MqttConnAckVariableHeader actual)
    {
        Assert.Equal(expected.ConnectReturnCode(), actual.ConnectReturnCode());
    }

    private static void ValidatePublishVariableHeader(
            MqttPublishVariableHeader expected,
            MqttPublishVariableHeader actual)
    {
        Assert.Equal(expected.TopicName(), actual.TopicName());
        Assert.Equal(expected.PacketId(), actual.PacketId());
        ValidateProperties(expected.Properties(), actual.Properties());
    }

    private static void ValidatePublishPayload(ByteBuf expected, ByteBuf actual)
    {
        Assert.Equal(0, expected.CompareTo(actual));
    }

    private static void ValidateMessageIdVariableHeader(
            MqttMessageIdVariableHeader expected,
            MqttMessageIdVariableHeader actual)
    {
        Assert.Equal(expected.MessageId(), actual.MessageId());
    }

    private static void ValidateSubAckPayload(MqttSubAckPayload expected, MqttSubAckPayload actual)
    {
        Assert.Equal(expected.ReasonCodes().ToArray(), actual.ReasonCodes().ToArray());
        Assert.Equal(expected.GrantedQoSLevels().ToArray(), actual.GrantedQoSLevels().ToArray());
    }

   private static void ValidateDecoderExceptionTooLargeMessage(MqttMessage message)
   {
        Assert.Null(message.Payload());
        Assert.True(message.DecoderResult().IsFailure());
        Exception cause = message.DecoderResult().Cause();
        Assert.IsAssignableFrom<TooLongFrameException>(cause);
    }

    private static void ValidatePubReplyVariableHeader(
            MqttPubReplyMessageVariableHeader expected,
            MqttPubReplyMessageVariableHeader actual)
    {
        Assert.Equal(expected.MessageId(), actual.MessageId());
        Assert.Equal(expected.ReasonCode(), actual.ReasonCode());

        MqttProperties expectedProps = expected.Properties();
        MqttProperties actualProps = actual.Properties();
        ValidateProperties(expectedProps, actualProps);
    }

    private void ValidatePacketIdAndPropertiesVariableHeader(MqttMessageIdAndPropertiesVariableHeader expected,
                                                              MqttMessageIdAndPropertiesVariableHeader actual)
    {
        Assert.Equal(expected.MessageId(), actual.MessageId());
        MqttProperties expectedProps = expected.Properties();
        MqttProperties actualProps = actual.Properties();
        ValidateProperties(expectedProps, actualProps);
    }

    private void ValidateReasonCodeAndPropertiesVariableHeader(MqttReasonCodeAndPropertiesVariableHeader expected,
                                                             MqttReasonCodeAndPropertiesVariableHeader actual)
    {
        Assert.Equal(expected.ReasonCode(), actual.ReasonCode());
        MqttProperties expectedProps = expected.Properties();
        MqttProperties actualProps = actual.Properties();
        ValidateProperties(expectedProps, actualProps);
    }

    /**
     * Builds a minimal MQTT 3.1.1 CONNECT packet whose ClientId field contains the supplied
     * raw bytes (length prefix is computed automatically). Protocol = "MQTT", level = 4,
     * clean-session flag set, keepalive 60.
     */
    private static ByteBuf BuildConnectWithClientIdBytes(byte[] clientIdBytes)
    {
        ByteBuf buf = Unpooled.Buffer();
        // variable header (10 bytes) + ClientId field (2 + N bytes); test packets stay small,
        // so a single-byte Remaining Length is sufficient.
        int remainingLength = 10 + 2 + clientIdBytes.Length;
        buf.WriteByte(0x10); // CONNECT
        buf.WriteByte(remainingLength);
        // Variable header
        buf.WriteShort(4);
        buf.WriteBytes(new byte[] {(byte) 'M', (byte) 'Q', (byte) 'T', (byte) 'T'});
        buf.WriteByte(0x04); // protocol level (MQTT 3.1.1)
        buf.WriteByte(0x02); // clean session
        buf.WriteShort(60);  // keep alive
        // Payload: ClientId
        buf.WriteShort(clientIdBytes.Length);
        buf.WriteBytes(clientIdBytes);
        return buf;
    }

    private static MqttMessage DecodeUtf8TestPacket(MqttDecoder decoder, ByteBuf @in)
    {
        EmbeddedChannel channel = new EmbeddedChannel(decoder);
        try
        {
            channel.WriteInbound(@in);
            return channel.ReadInbound<MqttMessage>();
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    private static void AssertMalformedUtf8(MqttMessage msg)
    {
        Assert.NotNull(msg);
        Assert.True(msg.DecoderResult().IsFailure(), "expected decoder failure but got: " + msg);
        Assert.IsAssignableFrom<DecoderException>(msg.DecoderResult().Cause());
    }

    [Fact]
    public void InvalidTwoByteUtf8SequenceIsRejectedByDefault()
    {
        // 0xC3 must be followed by a 10xxxxxx continuation byte; 0x28 is not.
        ByteBuf packet = BuildConnectWithClientIdBytes(new byte[] {(byte) 0xC3, 0x28});
        AssertMalformedUtf8(DecodeUtf8TestPacket(new MqttDecoder(), packet));
    }

    [Fact]
    public void TruncatedMultibyteUtf8SequenceIsRejected()
    {
        // Lone 0xC3 with no continuation byte at all (string ends mid-sequence).
        ByteBuf packet = BuildConnectWithClientIdBytes(new byte[] {(byte) 0xC3});
        AssertMalformedUtf8(DecodeUtf8TestPacket(new MqttDecoder(), packet));
    }

    [Fact]
    public void OverlongNullUtf8EncodingIsRejected()
    {
        // 0xC0 0x80 is the (invalid) Modified-UTF-8 overlong encoding of U+0000.
        ByteBuf packet = BuildConnectWithClientIdBytes(new byte[] {(byte) 0xC0, (byte) 0x80});
        AssertMalformedUtf8(DecodeUtf8TestPacket(new MqttDecoder(), packet));
    }

    [Fact]
    public void IsolatedHighSurrogateUtf8IsRejected()
    {
        // 0xED 0xA0 0x80 = U+D800, an unpaired UTF-16 high surrogate; not valid UTF-8.
        ByteBuf packet = BuildConnectWithClientIdBytes(new byte[] {(byte) 0xED, (byte) 0xA0, (byte) 0x80});
        AssertMalformedUtf8(DecodeUtf8TestPacket(new MqttDecoder(), packet));
    }

    [Fact]
    public void FiveByteOverlongUtf8SequenceIsRejected()
    {
        // 0xF8 starts a 5-byte sequence which is not allowed by RFC 3629.
        ByteBuf packet = BuildConnectWithClientIdBytes(
                new byte[] {(byte) 0xF8, (byte) 0x88, (byte) 0x80, (byte) 0x80, (byte) 0x80});
        AssertMalformedUtf8(DecodeUtf8TestPacket(new MqttDecoder(), packet));
    }

    [Fact]
    public void EmbeddedNullCharacterInUtf8StringIsRejected()
    {
        // U+0000 must cause Malformed Packet.
        ByteBuf packet = BuildConnectWithClientIdBytes(new byte[] {(byte) 'a', 0x00, (byte) 'b'});
        AssertMalformedUtf8(DecodeUtf8TestPacket(new MqttDecoder(), packet));
    }

    [Fact]
    public void WellFormedMultibyteUtf8IsAccepted()
    {
        byte[] cid = {(byte) 'h', (byte) 'e', (byte) 'l', (byte) 'l', (byte) 'o'};
        ByteBuf packet = BuildConnectWithClientIdBytes(cid);
        MqttMessage msg = DecodeUtf8TestPacket(new MqttDecoder(), packet);
        Assert.NotNull(msg);
        try
        {
            Assert.True(msg.DecoderResult().IsSuccess(),
                    "expected success but got: " + (msg.DecoderResult().Cause()?.ToString() ?? "null"));
            Assert.IsAssignableFrom<MqttConnectMessage>(msg);
            Assert.Equal("hello", ((MqttConnectMessage) msg).Payload().ClientIdentifier());
        }
        finally
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    [Fact]
    public void EmptyClientIdIsAcceptedUnderStrictUtf8()
    {
        ByteBuf packet = BuildConnectWithClientIdBytes(new byte[0]);
        MqttMessage msg = DecodeUtf8TestPacket(new MqttDecoder(), packet);
        Assert.NotNull(msg);
        try
        {
            Assert.True(msg.DecoderResult().IsSuccess());
        }
        finally
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    [Fact]
    public void LegacyModeAcceptsMalformedUtf8AsReplacementChar()
    {
        ByteBuf packet = BuildConnectWithClientIdBytes(new byte[] {(byte) 0xC3, 0x28});
        MqttDecoder lenient = new MqttDecoder(8 * 1024 * 1024, 23, false);
        MqttMessage msg = DecodeUtf8TestPacket(lenient, packet);
        Assert.NotNull(msg);
        try
        {
            Assert.True(msg.DecoderResult().IsSuccess(),
                    "lenient mode should accept malformed UTF-8");
            Assert.IsAssignableFrom<MqttConnectMessage>(msg);
            string cid = ((MqttConnectMessage) msg).Payload().ClientIdentifier();
            Assert.NotNull(cid);
            // java.lang.String inserts U+FFFD for malformed input. Exact length is JDK
            // implementation defined; just make sure decoding did not throw.
            Assert.True(cid.IndexOf('�') >= 0 || cid.Length > 0,
                    "expected replacement char or non-empty result");
        }
        finally
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    [Fact]
    public void LegacyModeAcceptsEmbeddedNullCharacter()
    {
        ByteBuf packet = BuildConnectWithClientIdBytes(new byte[] {(byte) 'a', 0x00, (byte) 'b'});
        MqttDecoder lenient = new MqttDecoder(8 * 1024 * 1024, 23, false);
        MqttMessage msg = DecodeUtf8TestPacket(lenient, packet);
        Assert.NotNull(msg);
        try
        {
            Assert.True(msg.DecoderResult().IsSuccess());
            Assert.Equal("a\u0000b", ((MqttConnectMessage) msg).Payload().ClientIdentifier());
        }
        finally
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    // CLR: @EnumSource(MqttVersion.class) is [MemberData] over MqttVersion.Values().
    public static IEnumerable<object[]> MqttVersionValues()
    {
        return MqttVersion.Values().Select(version => new object[] { version });
    }

    [Theory]
    [MemberData(nameof(MqttVersionValues))]
    public void EncodeConnectMessageWithNulInClientIdIsRejected(MqttVersion version)
    {
        MqttConnectMessage message = MqttMessageBuilders.Connect()
                .ClientId("client\u0000id")
                .ProtocolVersion(version)
                .CleanSession(true)
                .KeepAlive(KEEP_ALIVE_SECONDS)
                .Build();
        Assert.Throws<MqttIdentifierRejectedException>(() => MqttEncoder.DoEncode(ctx.Object, message));
    }

    [Theory]
    [MemberData(nameof(MqttVersionValues))]
    public void EncodeConnectMessageWithNulInWillTopicIsRejected(MqttVersion version)
    {
        MqttConnectMessage message = MqttMessageBuilders.Connect()
                .ClientId(CLIENT_ID)
                .ProtocolVersion(version)
                .WillFlag(true)
                .WillQoS(MqttQoS.AT_LEAST_ONCE)
                .WillTopic("will\u0000topic")
                .WillMessage(Encoding.UTF8.GetBytes(WILL_MESSAGE))
                .CleanSession(true)
                .KeepAlive(KEEP_ALIVE_SECONDS)
                .Build();
        Assert.Throws<MqttIdentifierRejectedException>(() => MqttEncoder.DoEncode(ctx.Object, message));
    }

    [Theory]
    [MemberData(nameof(MqttVersionValues))]
    public void EncodeConnectMessageWithNulInUserNameIsRejected(MqttVersion version)
    {
        MqttConnectMessage message = MqttMessageBuilders.Connect()
                .ClientId(CLIENT_ID)
                .ProtocolVersion(version)
                .Username("user\u0000name")
                .Password(PASSWORD_BYTES)
                .CleanSession(true)
                .KeepAlive(KEEP_ALIVE_SECONDS)
                .Build();
        Assert.Throws<MqttIdentifierRejectedException>(() => MqttEncoder.DoEncode(ctx.Object, message));
    }

    [Fact]
    public void EncodePublishMessageWithNulInTopicIsRejected()
    {
        MqttFixedHeader fixedHeader =
                new MqttFixedHeader(MqttMessageType.PUBLISH, false, MqttQoS.AT_LEAST_ONCE, false, 0);
        MqttPublishVariableHeader variableHeader = new MqttPublishVariableHeader("home/\u0000/sensor", 1);
        ByteBuf payload = ALLOCATOR.Buffer();
        payload.WriteBytes(Encoding.UTF8.GetBytes("data"));
        MqttPublishMessage message = new MqttPublishMessage(fixedHeader, variableHeader, payload);
        try
        {
            Assert.Throws<MqttIdentifierRejectedException>(() => MqttEncoder.DoEncode(ctx.Object, message));
        }
        finally
        {
            payload.Release();
        }
    }

    // CLR: Arrays.toString(byte[]) ("[a, b]" of the signed bytes,
    // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Arrays.html#toString(byte%5B%5D)).
    private static string ArraysToString(byte[] a)
    {
        return "[" + string.Join(", ", a.Select(b => ((sbyte) b).ToString(CultureInfo.InvariantCulture))) + "]";
    }
}
