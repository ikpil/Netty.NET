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
using System.Diagnostics;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.ByteBufUtil;
using static Netty.NET.Handler.Codec.Mqtt.MqttCodecUtil;
using static Netty.NET.Handler.Codec.Mqtt.MqttConstant;
using static Netty.NET.Handler.Codec.Mqtt.MqttProperties;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Encodes Mqtt messages into bytes following the protocol specification v3.1
 * as described here <a href="https://public.dhe.ibm.com/software/dw/webservices/ws-mqtt/mqtt-v3r1.html">MQTTV3.1</a>
 * or v5.0 as described here <a href="https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html">MQTTv5.0</a> -
 * depending on the version specified in the first CONNECT message that goes through the channel.
 */
// CLR: @ChannelHandler.Sharable is [Sharable] and MessageToMessageEncoder<MqttMessage> takes typeof(MqttMessage), as
// LengthFieldPrepender in codec-base; List<Object> out is IList<object> @out. Buffer API shapes follow the Buffer port
// (docs/buffer-porting.md): readableBytes()/writerIndex() are the ReadableBytes/WriterIndex properties, the byte[]
// writes are the ReadOnlySpan<byte> overloads (an array range is a span slice), and ByteBufAllocator is
// IByteBufAllocator. A switch over the MqttMessageType constants uses case guards (see MqttCodecUtil).
// RuntimeException is Exception and assert is Debug.Assert (docs/codec-base-porting.md; DatagramPacketEncoder).
[Sharable]
public sealed class MqttEncoder : MessageToMessageEncoder<MqttMessage>
{

    public static readonly MqttEncoder INSTANCE = new MqttEncoder();

    private MqttEncoder() : base(typeof(MqttMessage))
    {
    }

    protected override void Encode(IChannelHandlerContext ctx, MqttMessage msg, IList<object> @out)
    {
        @out.Add(DoEncode(ctx, msg));
    }

    /**
     * This is the main encoding method.
     * It's only visible for testing.
     *
     * @param message MQTT message to encode
     * @return ByteBuf with encoded bytes
     */
    internal static ByteBuf DoEncode(IChannelHandlerContext ctx,
                     MqttMessage message)
    {

        MqttMessageType messageType = message.FixedHeader().MessageType();
        switch (messageType)
        {
            case var _ when messageType == MqttMessageType.CONNECT:
                return EncodeConnectMessage(ctx, (MqttConnectMessage) message);

            case var _ when messageType == MqttMessageType.CONNACK:
                return EncodeConnAckMessage(ctx, (MqttConnAckMessage) message);

            case var _ when messageType == MqttMessageType.PUBLISH:
                return EncodePublishMessage(ctx, (MqttPublishMessage) message);

            case var _ when messageType == MqttMessageType.SUBSCRIBE:
                return EncodeSubscribeMessage(ctx, (MqttSubscribeMessage) message);

            case var _ when messageType == MqttMessageType.UNSUBSCRIBE:
                return EncodeUnsubscribeMessage(ctx,  (MqttUnsubscribeMessage) message);

            case var _ when messageType == MqttMessageType.SUBACK:
                return EncodeSubAckMessage(ctx, (MqttSubAckMessage) message);

            case var _ when messageType == MqttMessageType.UNSUBACK:
                if (message is MqttUnsubAckMessage)
                {
                    return EncodeUnsubAckMessage(ctx, (MqttUnsubAckMessage) message);
                }
                return EncodeMessageWithOnlySingleByteFixedHeaderAndMessageId(ctx.Alloc(), message);

            case var _ when messageType == MqttMessageType.PUBACK:
            case var _ when messageType == MqttMessageType.PUBREC:
            case var _ when messageType == MqttMessageType.PUBREL:
            case var _ when messageType == MqttMessageType.PUBCOMP:
                return EncodePubReplyMessage(ctx, message);

            case var _ when messageType == MqttMessageType.DISCONNECT:
            case var _ when messageType == MqttMessageType.AUTH:
                return EncodeReasonCodePlusPropertiesMessage(ctx, message);

            case var _ when messageType == MqttMessageType.PINGREQ:
            case var _ when messageType == MqttMessageType.PINGRESP:
                return EncodeMessageWithOnlySingleByteFixedHeader(ctx.Alloc(), message);

            default:
                throw new ArgumentException(
                        "Unknown message type: " +
                        message.FixedHeader().MessageType().Value().ToString(CultureInfo.InvariantCulture));
        }
    }

    private static ByteBuf EncodeConnectMessage(
            IChannelHandlerContext ctx,
            MqttConnectMessage message)
    {
        int payloadBufferSize = 0;

        MqttFixedHeader mqttFixedHeader = message.FixedHeader();
        MqttConnectVariableHeader variableHeader = message.VariableHeader();
        MqttConnectPayload payload = message.Payload();
        MqttVersion mqttVersion = MqttVersion.FromProtocolNameAndLevel(variableHeader.Name(),
                (byte) variableHeader.Version());
        SetMqttVersion(ctx, mqttVersion);

        // MQTT 3.1 and 3.1.1 require the Password Flag to be 0 when the User Name Flag is 0.
        if ((mqttVersion == MqttVersion.MQTT_3_1 || mqttVersion == MqttVersion.MQTT_3_1_1) &&
                !variableHeader.HasUserName() && variableHeader.HasPassword())
        {
            throw new EncoderException("Without a username, the password MUST be not set");
        }

        // Client id
        string clientIdentifier = payload.ClientIdentifier();
        if (!IsValidClientId(mqttVersion, DEFAULT_MAX_CLIENT_ID_LENGTH, clientIdentifier, false))
        {
            throw new MqttIdentifierRejectedException("invalid clientIdentifier");
        }
        int clientIdentifierBytes = Utf8Bytes(clientIdentifier);
        payloadBufferSize += 2 + clientIdentifierBytes;

        // Will topic and message
        string willTopic = payload.WillTopic();
        int willTopicBytes = NullableUtf8Bytes(willTopic);
        byte[] willMessage = payload.WillMessageInBytes();
        byte[] willMessageBytes = willMessage != null ? willMessage : EmptyArrays.EMPTY_BYTES;
        if (variableHeader.IsWillFlag())
        {
            if (!IsValidPublishTopicName(willTopic))
            {
                throw new MqttIdentifierRejectedException("invalid willTopic");
            }
            payloadBufferSize += 2 + willTopicBytes;
            payloadBufferSize += 2 + willMessageBytes.Length;
        }

        string userName = payload.UserName();
        int userNameBytes = NullableUtf8Bytes(userName);
        if (variableHeader.HasUserName())
        {
            if (!IsValidUserName(userName))
            {
                throw new MqttIdentifierRejectedException("invalid userName");
            }
            payloadBufferSize += 2 + userNameBytes;
        }

        byte[] password = payload.PasswordInBytes();
        byte[] passwordBytes = password != null ? password : EmptyArrays.EMPTY_BYTES;
        if (variableHeader.HasPassword())
        {
            payloadBufferSize += 2 + passwordBytes.Length;
        }

        // Fixed and variable header
        byte[] protocolNameBytes = mqttVersion.ProtocolNameBytes();
        ByteBuf propertiesBuf = EncodePropertiesIfNeeded(
                mqttVersion,
                ctx.Alloc(),
                message.VariableHeader().Properties());
        try
        {
            ByteBuf willPropertiesBuf;
            if (variableHeader.IsWillFlag())
            {
                willPropertiesBuf = EncodePropertiesIfNeeded(mqttVersion, ctx.Alloc(), payload.WillProperties());
                payloadBufferSize += willPropertiesBuf.ReadableBytes;
            }
            else
            {
                willPropertiesBuf = Unpooled.EmptyBuffer;
            }
            try
            {
                int variableHeaderBufferSize = 2 + protocolNameBytes.Length + 4 + propertiesBuf.ReadableBytes;

                int variablePartSize = variableHeaderBufferSize + payloadBufferSize;
                int fixedHeaderBufferSize = 1 + GetVariableLengthInt(variablePartSize);
                ByteBuf buf = ctx.Alloc().Buffer(fixedHeaderBufferSize + variablePartSize);
                buf.WriteByte(GetFixedHeaderByte1(mqttFixedHeader));
                WriteVariableLengthInt(buf, variablePartSize);

                buf.WriteShort(protocolNameBytes.Length);
                buf.WriteBytes(protocolNameBytes);

                buf.WriteByte(variableHeader.Version());
                buf.WriteByte(GetConnVariableHeaderFlag(variableHeader));
                buf.WriteShort(variableHeader.KeepAliveTimeSeconds());
                buf.WriteBytes(propertiesBuf);

                // Payload
                WriteExactUTF8String(buf, clientIdentifier, clientIdentifierBytes);
                if (variableHeader.IsWillFlag())
                {
                    buf.WriteBytes(willPropertiesBuf);
                    WriteExactUTF8String(buf, willTopic, willTopicBytes);
                    buf.WriteShort(willMessageBytes.Length);
                    buf.WriteBytes(willMessageBytes.AsSpan(0, willMessageBytes.Length));
                }
                if (variableHeader.HasUserName())
                {
                    WriteExactUTF8String(buf, userName, userNameBytes);
                }
                if (variableHeader.HasPassword())
                {
                    buf.WriteShort(passwordBytes.Length);
                    buf.WriteBytes(passwordBytes.AsSpan(0, passwordBytes.Length));
                }
                return buf;
            }
            finally
            {
                willPropertiesBuf.Release();
            }
        }
        finally
        {
            propertiesBuf.Release();
        }
    }

    private static int GetConnVariableHeaderFlag(MqttConnectVariableHeader variableHeader)
    {
        int flagByte = 0;
        if (variableHeader.HasUserName())
        {
            flagByte |= 0x80;
        }
        if (variableHeader.HasPassword())
        {
            flagByte |= 0x40;
        }
        if (variableHeader.IsWillRetain())
        {
            flagByte |= 0x20;
        }
        flagByte |= (variableHeader.WillQos() & 0x03) << 3;
        if (variableHeader.IsWillFlag())
        {
            flagByte |= 0x04;
        }
        if (variableHeader.IsCleanSession())
        {
            flagByte |= 0x02;
        }
        return flagByte;
    }

    private static ByteBuf EncodeConnAckMessage(
            IChannelHandlerContext ctx,
            MqttConnAckMessage message)
    {
        MqttVersion mqttVersion = GetMqttVersion(ctx);
        ByteBuf propertiesBuf = EncodePropertiesIfNeeded(mqttVersion,
                ctx.Alloc(),
                message.VariableHeader().Properties());

        try
        {
            ByteBuf buf = ctx.Alloc().Buffer(4 + propertiesBuf.ReadableBytes);
            buf.WriteByte(GetFixedHeaderByte1(message.FixedHeader()));
            WriteVariableLengthInt(buf, 2 + propertiesBuf.ReadableBytes);
            buf.WriteByte(message.VariableHeader().IsSessionPresent() ? 0x01 : 0x00);
            buf.WriteByte(message.VariableHeader().ConnectReturnCode().ByteValue());
            buf.WriteBytes(propertiesBuf);
            return buf;
        }
        finally
        {
            propertiesBuf.Release();
        }
    }

    // CLR: the source calls the @Deprecated MqttTopicSubscription.topicName(), which is not ported; its replacement
    // topicFilter() ("use topicFilter") returns the same field.
    private static ByteBuf EncodeSubscribeMessage(
            IChannelHandlerContext ctx,
            MqttSubscribeMessage message)
    {
        MqttVersion mqttVersion = GetMqttVersion(ctx);
        ByteBuf propertiesBuf = EncodePropertiesIfNeeded(mqttVersion,
                ctx.Alloc(),
                message.IdAndPropertiesVariableHeader().Properties());

        try
        {
            int variableHeaderBufferSize = 2 + propertiesBuf.ReadableBytes;
            int payloadBufferSize = 0;

            MqttFixedHeader mqttFixedHeader = message.FixedHeader();
            MqttMessageIdVariableHeader variableHeader = message.VariableHeader();
            MqttSubscribePayload payload = message.Payload();

            foreach (MqttTopicSubscription topic in payload.TopicSubscriptions())
            {
                string topicName = topic.TopicFilter();
                int topicNameBytes = Utf8Bytes(topicName);
                payloadBufferSize += 2 + topicNameBytes;
                payloadBufferSize += 1;
            }

            int variablePartSize = variableHeaderBufferSize + payloadBufferSize;
            int fixedHeaderBufferSize = 1 + GetVariableLengthInt(variablePartSize);

            ByteBuf buf = ctx.Alloc().Buffer(fixedHeaderBufferSize + variablePartSize);
            buf.WriteByte(GetFixedHeaderByte1(mqttFixedHeader));
            WriteVariableLengthInt(buf, variablePartSize);

            // Variable Header
            int messageId = variableHeader.MessageId();
            buf.WriteShort(messageId);
            buf.WriteBytes(propertiesBuf);

            // Payload
            foreach (MqttTopicSubscription topic in payload.TopicSubscriptions())
            {
                WriteEagerUTF8String(buf, topic.TopicFilter());
                if (mqttVersion == MqttVersion.MQTT_3_1_1 || mqttVersion == MqttVersion.MQTT_3_1)
                {
                    buf.WriteByte(topic.QualityOfService().Value());
                }
                else
                {
                    MqttSubscriptionOption option = topic.Option();

                    int optionEncoded = option.RetainHandling().Value() << 4;
                    if (option.IsRetainAsPublished())
                    {
                        optionEncoded |= 0x08;
                    }
                    if (option.IsNoLocal())
                    {
                        optionEncoded |= 0x04;
                    }
                    optionEncoded |= option.Qos().Value();

                    buf.WriteByte(optionEncoded);
                }
            }

            return buf;
        }
        finally
        {
            propertiesBuf.Release();
        }
    }

    private static ByteBuf EncodeUnsubscribeMessage(
            IChannelHandlerContext ctx,
            MqttUnsubscribeMessage message)
    {
        MqttVersion mqttVersion = GetMqttVersion(ctx);
        ByteBuf propertiesBuf = EncodePropertiesIfNeeded(mqttVersion,
                ctx.Alloc(),
                message.IdAndPropertiesVariableHeader().Properties());

        try
        {
            int variableHeaderBufferSize = 2 + propertiesBuf.ReadableBytes;
            int payloadBufferSize = 0;

            MqttFixedHeader mqttFixedHeader = message.FixedHeader();
            MqttMessageIdVariableHeader variableHeader = message.VariableHeader();
            MqttUnsubscribePayload payload = message.Payload();

            foreach (string topicName in payload.Topics())
            {
                int topicNameBytes = Utf8Bytes(topicName);
                payloadBufferSize += 2 + topicNameBytes;
            }

            int variablePartSize = variableHeaderBufferSize + payloadBufferSize;
            int fixedHeaderBufferSize = 1 + GetVariableLengthInt(variablePartSize);

            ByteBuf buf = ctx.Alloc().Buffer(fixedHeaderBufferSize + variablePartSize);
            buf.WriteByte(GetFixedHeaderByte1(mqttFixedHeader));
            WriteVariableLengthInt(buf, variablePartSize);

            // Variable Header
            int messageId = variableHeader.MessageId();
            buf.WriteShort(messageId);
            buf.WriteBytes(propertiesBuf);

            // Payload
            foreach (string topicName in payload.Topics())
            {
                WriteEagerUTF8String(buf, topicName);
            }

            return buf;
        }
        finally
        {
            propertiesBuf.Release();
        }
    }

    private static ByteBuf EncodeSubAckMessage(
            IChannelHandlerContext ctx,
            MqttSubAckMessage message)
    {
        MqttVersion mqttVersion = GetMqttVersion(ctx);
        ByteBuf propertiesBuf = EncodePropertiesIfNeeded(mqttVersion,
                ctx.Alloc(),
                message.IdAndPropertiesVariableHeader().Properties());
        try
        {
            int variableHeaderBufferSize = 2 + propertiesBuf.ReadableBytes;
            int payloadBufferSize = message.Payload().GrantedQoSLevels().Count;
            int variablePartSize = variableHeaderBufferSize + payloadBufferSize;
            int fixedHeaderBufferSize = 1 + GetVariableLengthInt(variablePartSize);
            ByteBuf buf = ctx.Alloc().Buffer(fixedHeaderBufferSize + variablePartSize);
            buf.WriteByte(GetFixedHeaderByte1(message.FixedHeader()));
            WriteVariableLengthInt(buf, variablePartSize);
            buf.WriteShort(message.VariableHeader().MessageId());
            buf.WriteBytes(propertiesBuf);
            foreach (int code in message.Payload().ReasonCodes())
            {
                buf.WriteByte(code);
            }

            return buf;
        }
        finally
        {
            propertiesBuf.Release();
        }
    }

    private static ByteBuf EncodeUnsubAckMessage(
            IChannelHandlerContext ctx,
            MqttUnsubAckMessage message)
    {
        if (message.VariableHeader() is  MqttMessageIdAndPropertiesVariableHeader)
        {
            MqttVersion mqttVersion = GetMqttVersion(ctx);
            // Reason Codes were introduced in MQTT 5.0 only. MQTT 3.1.1 (and 3.1) UNSUBACK packets
            // have no payload, so reason codes must be suppressed for older protocol versions
            // even when the caller populated them via MqttMessageBuilders.
            bool writeReasonCodes = mqttVersion == MqttVersion.MQTT_5;
            ByteBuf propertiesBuf = EncodePropertiesIfNeeded(mqttVersion,
                    ctx.Alloc(),
                    message.IdAndPropertiesVariableHeader().Properties());
            try
            {
                int variableHeaderBufferSize = 2 + propertiesBuf.ReadableBytes;
                MqttUnsubAckPayload payload = message.Payload();
                int payloadBufferSize = writeReasonCodes && payload != null
                        ? payload.UnsubscribeReasonCodes().Count : 0;
                int variablePartSize = variableHeaderBufferSize + payloadBufferSize;
                int fixedHeaderBufferSize = 1 + GetVariableLengthInt(variablePartSize);
                ByteBuf buf = ctx.Alloc().Buffer(fixedHeaderBufferSize + variablePartSize);
                buf.WriteByte(GetFixedHeaderByte1(message.FixedHeader()));
                WriteVariableLengthInt(buf, variablePartSize);
                buf.WriteShort(message.VariableHeader().MessageId());
                buf.WriteBytes(propertiesBuf);

                if (writeReasonCodes && payload != null)
                {
                    foreach (short reasonCode in payload.UnsubscribeReasonCodes())
                    {
                        buf.WriteByte(reasonCode);
                    }
                }

                return buf;
            }
            finally
            {
                propertiesBuf.Release();
            }
        }
        else
        {
            return EncodeMessageWithOnlySingleByteFixedHeaderAndMessageId(ctx.Alloc(), message);
        }
    }

    private static ByteBuf EncodePublishMessage(
            IChannelHandlerContext ctx,
            MqttPublishMessage message)
    {
        MqttVersion mqttVersion = GetMqttVersion(ctx);
        MqttFixedHeader mqttFixedHeader = message.FixedHeader();
        MqttPublishVariableHeader variableHeader = message.VariableHeader();
        ByteBuf payload = message.Payload().Duplicate();

        string topicName = variableHeader.TopicName();
        if (!IsValidPublishTopicName(topicName))
        {
            throw new MqttIdentifierRejectedException("invalid topicName");
        }
        int topicNameBytes = Utf8Bytes(topicName);

        ByteBuf propertiesBuf = EncodePropertiesIfNeeded(mqttVersion,
                ctx.Alloc(),
                message.VariableHeader().Properties());

        try
        {
            bool qosLevelGreaterZero = mqttFixedHeader.QosLevel().Value() > 0;
            int variableHeaderBufferSize = 2 + topicNameBytes +
                    (qosLevelGreaterZero ? 2 : 0) + propertiesBuf.ReadableBytes;
            int payloadBufferSize = payload.ReadableBytes;
            int variablePartSize = variableHeaderBufferSize + payloadBufferSize;
            int fixedHeaderBufferSize = 1 + GetVariableLengthInt(variablePartSize);

            ByteBuf buf = ctx.Alloc().Buffer(fixedHeaderBufferSize + variablePartSize);
            buf.WriteByte(GetFixedHeaderByte1(mqttFixedHeader));
            WriteVariableLengthInt(buf, variablePartSize);
            WriteExactUTF8String(buf, topicName, topicNameBytes);
            if (qosLevelGreaterZero)
            {
                buf.WriteShort(variableHeader.PacketId());
            }
            buf.WriteBytes(propertiesBuf);
            buf.WriteBytes(payload);

            return buf;
        }
        finally
        {
            propertiesBuf.Release();
        }
    }

    private static ByteBuf EncodePubReplyMessage(IChannelHandlerContext ctx,
                                          MqttMessage message)
    {
        if (message.VariableHeader() is MqttPubReplyMessageVariableHeader)
        {
            MqttFixedHeader mqttFixedHeader = message.FixedHeader();
            MqttPubReplyMessageVariableHeader variableHeader =
                    (MqttPubReplyMessageVariableHeader) message.VariableHeader();
            int msgId = variableHeader.MessageId();

            ByteBuf propertiesBuf;
            bool includeReasonCode;
            int variableHeaderBufferSize;
            MqttVersion mqttVersion = GetMqttVersion(ctx);
            if (mqttVersion == MqttVersion.MQTT_5 &&
                    (variableHeader.ReasonCode() != MqttPubReplyMessageVariableHeader.REASON_CODE_OK ||
                            !variableHeader.Properties().IsEmpty()))
            {
                propertiesBuf = EncodeProperties(ctx.Alloc(), variableHeader.Properties());
                includeReasonCode = true;
                variableHeaderBufferSize = 3 + propertiesBuf.ReadableBytes;
            }
            else
            {
                propertiesBuf = Unpooled.EmptyBuffer;
                includeReasonCode = false;
                variableHeaderBufferSize = 2;
            }

            try
            {
                int fixedHeaderBufferSize = 1 + GetVariableLengthInt(variableHeaderBufferSize);
                ByteBuf buf = ctx.Alloc().Buffer(fixedHeaderBufferSize + variableHeaderBufferSize);
                buf.WriteByte(GetFixedHeaderByte1(mqttFixedHeader));
                WriteVariableLengthInt(buf, variableHeaderBufferSize);
                buf.WriteShort(msgId);
                if (includeReasonCode)
                {
                    buf.WriteByte(variableHeader.ReasonCode());
                }
                buf.WriteBytes(propertiesBuf);

                return buf;
            }
            finally
            {
                propertiesBuf.Release();
            }
        }
        else
        {
            return EncodeMessageWithOnlySingleByteFixedHeaderAndMessageId(ctx.Alloc(), message);
        }
    }

    private static ByteBuf EncodeMessageWithOnlySingleByteFixedHeaderAndMessageId(
            IByteBufAllocator byteBufAllocator,
            MqttMessage message)
    {
        MqttFixedHeader mqttFixedHeader = message.FixedHeader();
        MqttMessageIdVariableHeader variableHeader = (MqttMessageIdVariableHeader) message.VariableHeader();
        int msgId = variableHeader.MessageId();

        int variableHeaderBufferSize = 2; // variable part only has a message id
        int fixedHeaderBufferSize = 1 + GetVariableLengthInt(variableHeaderBufferSize);
        ByteBuf buf = byteBufAllocator.Buffer(fixedHeaderBufferSize + variableHeaderBufferSize);
        buf.WriteByte(GetFixedHeaderByte1(mqttFixedHeader));
        WriteVariableLengthInt(buf, variableHeaderBufferSize);
        buf.WriteShort(msgId);

        return buf;
    }

    private static ByteBuf EncodeReasonCodePlusPropertiesMessage(
            IChannelHandlerContext ctx,
            MqttMessage message)
    {
        if (message.VariableHeader() is MqttReasonCodeAndPropertiesVariableHeader)
        {
            MqttVersion mqttVersion = GetMqttVersion(ctx);
            MqttFixedHeader mqttFixedHeader = message.FixedHeader();
            MqttReasonCodeAndPropertiesVariableHeader variableHeader =
                    (MqttReasonCodeAndPropertiesVariableHeader) message.VariableHeader();

            ByteBuf propertiesBuf;
            bool includeReasonCode;
            int variableHeaderBufferSize;
            if (mqttVersion == MqttVersion.MQTT_5 &&
                    (variableHeader.ReasonCode() != MqttReasonCodeAndPropertiesVariableHeader.REASON_CODE_OK ||
                            !variableHeader.Properties().IsEmpty()))
            {
                propertiesBuf = EncodeProperties(ctx.Alloc(), variableHeader.Properties());
                includeReasonCode = true;
                variableHeaderBufferSize = 1 + propertiesBuf.ReadableBytes;
            }
            else
            {
                propertiesBuf = Unpooled.EmptyBuffer;
                includeReasonCode = false;
                variableHeaderBufferSize = 0;
            }

            try
            {
                int fixedHeaderBufferSize = 1 + GetVariableLengthInt(variableHeaderBufferSize);
                ByteBuf buf = ctx.Alloc().Buffer(fixedHeaderBufferSize + variableHeaderBufferSize);
                buf.WriteByte(GetFixedHeaderByte1(mqttFixedHeader));
                WriteVariableLengthInt(buf, variableHeaderBufferSize);
                if (includeReasonCode)
                {
                    buf.WriteByte(variableHeader.ReasonCode());
                }
                buf.WriteBytes(propertiesBuf);

                return buf;
            }
            finally
            {
                propertiesBuf.Release();
            }
        }
        else
        {
            return EncodeMessageWithOnlySingleByteFixedHeader(ctx.Alloc(), message);
        }
    }

    private static ByteBuf EncodeMessageWithOnlySingleByteFixedHeader(
            IByteBufAllocator byteBufAllocator,
            MqttMessage message)
    {
        MqttFixedHeader mqttFixedHeader = message.FixedHeader();
        ByteBuf buf = byteBufAllocator.Buffer(2);
        buf.WriteByte(GetFixedHeaderByte1(mqttFixedHeader));
        buf.WriteByte(0);

        return buf;
    }

    private static ByteBuf EncodePropertiesIfNeeded(MqttVersion mqttVersion,
                                             IByteBufAllocator byteBufAllocator,
                                             MqttProperties mqttProperties)
    {
        if (mqttVersion == MqttVersion.MQTT_5)
        {
            return EncodeProperties(byteBufAllocator, mqttProperties);
        }
        return Unpooled.EmptyBuffer;
    }

    // CLR: Integer.byteValue()/shortValue() are the unchecked (byte)/(short) narrowing casts of the int value.
    private static ByteBuf EncodeProperties(IByteBufAllocator byteBufAllocator,
                                            MqttProperties mqttProperties)
    {
        ByteBuf propertiesHeaderBuf = byteBufAllocator.Buffer();
        // encode also the Properties part
        try
        {
            ByteBuf propertiesBuf = byteBufAllocator.Buffer();
            try
            {
                foreach (MqttProperties.MqttProperty property in mqttProperties.ListAll())
                {
                    int propertyId = property.propertyId;
                    switch (propertyId)
                    {
                        case PAYLOAD_FORMAT_INDICATOR:
                        case REQUEST_PROBLEM_INFORMATION:
                        case REQUEST_RESPONSE_INFORMATION:
                        case MAXIMUM_QOS:
                        case RETAIN_AVAILABLE:
                        case WILDCARD_SUBSCRIPTION_AVAILABLE:
                        case SUBSCRIPTION_IDENTIFIER_AVAILABLE:
                        case SHARED_SUBSCRIPTION_AVAILABLE:
                            WriteVariableLengthInt(propertiesBuf, propertyId);
                            byte bytePropValue = (byte) ((MqttProperties.IntegerProperty) property).value;
                            propertiesBuf.WriteByte(bytePropValue);
                            break;
                        case SERVER_KEEP_ALIVE:
                        case RECEIVE_MAXIMUM:
                        case TOPIC_ALIAS_MAXIMUM:
                        case TOPIC_ALIAS:
                            WriteVariableLengthInt(propertiesBuf, propertyId);
                            short twoBytesInPropValue =
                                    (short) ((MqttProperties.IntegerProperty) property).value;
                            propertiesBuf.WriteShort(twoBytesInPropValue);
                            break;
                        case PUBLICATION_EXPIRY_INTERVAL:
                        case SESSION_EXPIRY_INTERVAL:
                        case WILL_DELAY_INTERVAL:
                        case MAXIMUM_PACKET_SIZE:
                            WriteVariableLengthInt(propertiesBuf, propertyId);
                            int fourBytesIntPropValue = ((MqttProperties.IntegerProperty) property).value;
                            propertiesBuf.WriteInt(fourBytesIntPropValue);
                            break;
                        case SUBSCRIPTION_IDENTIFIER:
                            WriteVariableLengthInt(propertiesBuf, propertyId);
                            int vbi = ((MqttProperties.IntegerProperty) property).value;
                            WriteVariableLengthInt(propertiesBuf, vbi);
                            break;
                        case CONTENT_TYPE:
                        case RESPONSE_TOPIC:
                        case ASSIGNED_CLIENT_IDENTIFIER:
                        case AUTHENTICATION_METHOD:
                        case RESPONSE_INFORMATION:
                        case SERVER_REFERENCE:
                        case REASON_STRING:
                            WriteVariableLengthInt(propertiesBuf, propertyId);
                            WriteEagerUTF8String(propertiesBuf, ((MqttProperties.StringProperty) property).value);
                            break;
                        case USER_PROPERTY:
                            List<MqttProperties.StringPair> pairs =
                                    ((MqttProperties.UserProperties) property).value;
                            foreach (MqttProperties.StringPair pair in pairs)
                            {
                                WriteVariableLengthInt(propertiesBuf, propertyId);
                                WriteEagerUTF8String(propertiesBuf, pair.key);
                                WriteEagerUTF8String(propertiesBuf, pair.value);
                            }
                            break;
                        case CORRELATION_DATA:
                        case AUTHENTICATION_DATA:
                            WriteVariableLengthInt(propertiesBuf, propertyId);
                            byte[] binaryPropValue = ((MqttProperties.BinaryProperty) property).value;
                            propertiesBuf.WriteShort(binaryPropValue.Length);
                            propertiesBuf.WriteBytes(binaryPropValue.AsSpan(0, binaryPropValue.Length));
                            break;
                        default:
                            //shouldn't reach here
                            throw new EncoderException("Unknown property type: " +
                                    propertyId.ToString(CultureInfo.InvariantCulture));
                    }
                }
                WriteVariableLengthInt(propertiesHeaderBuf, propertiesBuf.ReadableBytes);
                propertiesHeaderBuf.WriteBytes(propertiesBuf);

                return propertiesHeaderBuf;
            }
            finally
            {
                propertiesBuf.Release();
            }
        }
        catch (Exception)
        {
            propertiesHeaderBuf.Release();
            throw;
        }
    }

    private static int GetFixedHeaderByte1(MqttFixedHeader header)
    {
        int ret = 0;
        ret |= header.MessageType().Value() << 4;
        if (header.IsDup())
        {
            ret |= 0x08;
        }
        ret |= header.QosLevel().Value() << 1;
        if (header.IsRetain())
        {
            ret |= 0x01;
        }
        return ret;
    }

    private static void WriteVariableLengthInt(ByteBuf buf, int num)
    {
        do
        {
            int digit = num & 0x7F;
            num >>>= 7;
            if (num > 0)
            {
                digit |= 0x80;
            }
            buf.WriteByte(digit);
        } while (num > 0);
    }

    private static int NullableUtf8Bytes(string s)
    {
        return s == null? 0 : Utf8Bytes(s);
    }

    private static int NullableMaxUtf8Bytes(string s)
    {
        return s == null? 0 : Utf8MaxBytes(s);
    }

    private static void WriteExactUTF8String(ByteBuf buf, string s, int utf8Length)
    {
        buf.EnsureWritable(utf8Length + 2);
        buf.WriteShort(utf8Length);
        if (utf8Length > 0)
        {
            int writtenUtf8Length = ReserveAndWriteUtf8(buf, s, utf8Length);
            Debug.Assert(writtenUtf8Length == utf8Length);
        }
    }

    private static void WriteEagerUTF8String(ByteBuf buf, string s)
    {
        int maxUtf8Length = NullableMaxUtf8Bytes(s);
        buf.EnsureWritable(maxUtf8Length + 2);
        int writerIndex = buf.WriterIndex;
        int startUtf8String = writerIndex + 2;
        buf.WriterIndex = startUtf8String;
        int utf8Length = s != null? ReserveAndWriteUtf8(buf, s, maxUtf8Length) : 0;
        buf.SetShort(writerIndex, utf8Length);
    }

    private static int GetVariableLengthInt(int num)
    {
        if (num < 128)
        {
            return 1;
        }
        if (num < 16_384) { // 128 * 128
            return 2;
        }
        if (num < 2_097_152) { // 128 * 128 * 128
            return 3;
        }
        return 4;
    }

}
