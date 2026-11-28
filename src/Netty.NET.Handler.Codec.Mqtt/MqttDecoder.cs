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
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Mqtt.MqttCodecUtil;
using static Netty.NET.Handler.Codec.Mqtt.MqttConstant;
using static Netty.NET.Handler.Codec.Mqtt.MqttProperties;
using static Netty.NET.Handler.Codec.Mqtt.MqttSubscriptionOption;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Decodes Mqtt messages from bytes, following
 * the MQTT protocol specification
 * <a href="https://public.dhe.ibm.com/software/dw/webservices/ws-mqtt/mqtt-v3r1.html">v3.1</a>
 * or
 * <a href="https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html">v5.0</a>, depending on the
 * version specified in the CONNECT message that first goes through the channel.
 */
// CLR: Buffer API shapes follow the Buffer and codec-base ports : readerIndex() is the ReaderIndex property,
// readUnsignedByte() is ReadByte() (unsigned byte, owner decision), the byte[] reads are the Span overloads, and
// List<Object> out is IList<object> @out. A switch over the MqttMessageType constants uses case guards (see
// MqttCodecUtil). Signal is an Exception here but a java.lang.Error in the source, which catch (Exception) does
// not catch, so every catch (Exception) has a "when (cause is not Signal)" filter. Numbers in messages use the
// invariant culture, and null strings are written as "null" as Java string conversion does
// (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11).
public sealed class MqttDecoder : ReplayingDecoder<MqttDecoder.DecoderState>
{

    /**
     * States of the decoder.
     * We start at READ_FIXED_HEADER, followed by
     * READ_VARIABLE_HEADER and finally READ_PAYLOAD.
     */
    // CLR: package-private in the source; "The direct base class of a class type must be at least as accessible as the
    // class type itself" (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/restrictions-on-using-accessibility-levels),
    // so the type argument of the public ReplayingDecoder<DecoderState> base is public.
    public enum DecoderState
    {
        READ_FIXED_HEADER,
        READ_VARIABLE_HEADER,
        READ_PAYLOAD,
        BAD_MESSAGE,
    }

    private MqttFixedHeader mqttFixedHeader;
    private object variableHeader;
    private int bytesRemainingInVariablePart;

    private readonly int maxBytesInMessage;
    private readonly int maxClientIdLength;
    private readonly bool strictUtf8Validation;
    // Lazily-initialised UTF-8 decoder reused across calls in the same channel/decoder
    // instance. ReplayingDecoder is invoked from a single thread per channel, so a non
    // thread-safe CharsetDecoder is safe to cache here.
    // CLR: the CharsetDecoder with CodingErrorAction.REPORT is a UTF8Encoding whose throwOnInvalidBytes is true, which
    // throws "an ArgumentException" for an invalid byte sequence
    // (https://learn.microsoft.com/dotnet/api/system.text.utf8encoding.-ctor), as decode(ByteBuffer) throws a
    // CharacterCodingException (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/CharsetDecoder.html).
    private Encoding utf8Decoder;
    private int maxAllowedRemainingBytes;

    public MqttDecoder() : this(DEFAULT_MAX_BYTES_IN_MESSAGE, DEFAULT_MAX_CLIENT_ID_LENGTH, true)
    {
    }

    public MqttDecoder(int maxBytesInMessage) : this(maxBytesInMessage, DEFAULT_MAX_CLIENT_ID_LENGTH, true)
    {
    }

    public MqttDecoder(int maxBytesInMessage, int maxClientIdLength) : this(maxBytesInMessage, maxClientIdLength, true)
    {
    }

    /**
     * Creates a new {@link MqttDecoder}.
     *
     * @param maxBytesInMessage     the maximum number of bytes a decoded message may consume.
     * @param maxClientIdLength     the maximum length of the Client Identifier (CONNECT payload).
     * @param strictUtf8Validation  if {@code true} (default), every UTF-8 Encoded String is
     *                              validated according to MQTT 3.1.1 and MQTT 5.0
     *                              malformed UTF-8 sequences (including
     *                              surrogates and overlong forms) and an embedded U+0000 are
     *                              rejected as a Malformed Packet. If {@code false}, the legacy
     *                              behaviour is preserved, malformed bytes are silently replaced
     *                              with {@code U+FFFD} and U+0000 is accepted.
     */
    public MqttDecoder(int maxBytesInMessage, int maxClientIdLength, bool strictUtf8Validation)
        : base(DecoderState.READ_FIXED_HEADER)
    {
        this.maxBytesInMessage = ObjectUtil.CheckPositive(maxBytesInMessage, "maxBytesInMessage");
        this.maxClientIdLength = ObjectUtil.CheckPositive(maxClientIdLength, "maxClientIdLength");
        this.strictUtf8Validation = strictUtf8Validation;
    }

    // CLR: the Java fall-through between the states is goto case after each try statement, because "control can't
    // fall through from one switch section to the next" and goto passes control to another section
    // (https://learn.microsoft.com/dotnet/csharp/language-reference/statements/selection-statements). Error is
    // UnreachableException (https://learn.microsoft.com/dotnet/api/system.diagnostics.unreachableexception), as codec-base
    // maps the Error of LengthFieldPrepender.
    protected override void Decode(IChannelHandlerContext ctx, ByteBuf buffer, IList<object> @out)
    {
        switch (State())
        {
            case DecoderState.READ_FIXED_HEADER:
                try
            {
                maxAllowedRemainingBytes = maxBytesInMessage;
                int startOfFixed = buffer.ReaderIndex;
                mqttFixedHeader = DecodeFixedHeader(ctx, buffer, maxAllowedRemainingBytes);
                maxAllowedRemainingBytes -= buffer.ReaderIndex - startOfFixed;
                bytesRemainingInVariablePart = mqttFixedHeader.RemainingLength();
                Checkpoint(DecoderState.READ_VARIABLE_HEADER);
                // fall through
            }
            catch (Exception cause) when (cause is not Signal)
            {
                @out.Add(InvalidMessage(cause));
                return;
            }

            goto case DecoderState.READ_VARIABLE_HEADER;

            case DecoderState.READ_VARIABLE_HEADER:
                try
            {
                int bytesRemainingBeforeVariableHeader = bytesRemainingInVariablePart;
                bool bailOut = false;

                try
                {
                    variableHeader = DecodeVariableHeader(ctx, buffer, mqttFixedHeader, maxAllowedRemainingBytes);
                }
                catch (Signal)
                {
                    if (bytesRemainingBeforeVariableHeader > maxBytesInMessage)
                    {
                        // We couldn't parse the complete message, and it's already too large.
                        // Swallow the Signal (we don't need more data) and instead bail out
                        // and throw the TooLongFrameException below.
                        bailOut = true;
                    }
                    else
                    {
                        // Ask for REPLAY if the current message is within maxBytesInMessage.
                        // CLR: throw; rethrows the caught Signal instance, as "throw signal" does.
                        throw;
                    }
                }

                if (bailOut || bytesRemainingBeforeVariableHeader > maxBytesInMessage)
                {
                    buffer.SkipBytes(ActualReadableBytes());
                    throw new TooLongFrameException("message length exceeds " +
                            maxBytesInMessage.ToString(CultureInfo.InvariantCulture) + ": " +
                            bytesRemainingBeforeVariableHeader.ToString(CultureInfo.InvariantCulture));
                }

                Checkpoint(DecoderState.READ_PAYLOAD);
                // fall through
            }
            catch (Exception cause) when (cause is not Signal)
            {
                @out.Add(InvalidMessage(cause));
                return;
            }

            goto case DecoderState.READ_PAYLOAD;

            case DecoderState.READ_PAYLOAD:
                try
            {
                object decodedPayload =
                    DecodePayload(
                        buffer,
                        mqttFixedHeader.MessageType(),
                        maxClientIdLength,
                        variableHeader,
                        maxAllowedRemainingBytes);
                Checkpoint(DecoderState.READ_FIXED_HEADER);
                MqttMessage message = MqttMessageFactory.NewMessage(
                    mqttFixedHeader, variableHeader, decodedPayload);
                mqttFixedHeader = null;
                variableHeader = null;
                @out.Add(message);
                break;
            }
            catch (Exception cause) when (cause is not Signal)
            {
                @out.Add(InvalidMessage(cause));
                return;
            }

            case DecoderState.BAD_MESSAGE:
                // Keep discarding until disconnection.
                buffer.SkipBytes(ActualReadableBytes());
                break;

            default:
                // Shouldn't reach here.
                throw new UnreachableException("Unexpected mqtt decoder state: " + State());
        }
    }

    // CLR: the tests call the protected decode(...) through Java package access; C# protected access is limited to
    // derived classes and an override cannot change its accessibility
    // (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/override), so this internal forwarder
    // serves the friend test assembly, like MessageToMessageDecoder.InvokeDecode in codec-base.
    internal void InvokeDecode(IChannelHandlerContext ctx, ByteBuf buffer, IList<object> @out)
    {
        Decode(ctx, buffer, @out);
    }

    private MqttMessage InvalidMessage(Exception cause)
    {
      Checkpoint(DecoderState.BAD_MESSAGE);

      return MqttMessageFactory.NewInvalidMessage(mqttFixedHeader, variableHeader, cause);
    }

    private void CheckMaxMessageLengthRemaining(int maxAllowedRemainingBytes)
    {
        if (maxAllowedRemainingBytes < 0)
        {
            throw new TooLongFrameException("message length exceeds " +
                    maxBytesInMessage.ToString(CultureInfo.InvariantCulture) + " bytes");
        }
    }

    /**
     * Decodes the fixed header. It's one byte for the flags and then variable
     * bytes for the remaining length.
     *
     * @see
     * https://docs.oasis-open.org/mqtt/mqtt/v3.1.1/errata01/os/mqtt-v3.1.1-errata01-os-complete.html#_Toc442180841
     *
     * @param buffer the buffer to decode from
     * @return the fixed header
     */
    private MqttFixedHeader DecodeFixedHeader(IChannelHandlerContext ctx, ByteBuf buffer,
                                                     int maxAllowedRemainingBytes)
    {
        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - 1);

        short b1 = buffer.ReadByte();
        maxAllowedRemainingBytes--;

        MqttMessageType messageType = MqttMessageType.ValueOf(b1 >> 4);
        bool dupFlag = (b1 & 0x08) == 0x08;
        int qosLevel = (b1 & 0x06) >> 1;
        bool retain = (b1 & 0x01) != 0;

        switch (messageType)
        {
            case var _ when messageType == MqttMessageType.PUBLISH:
                if (qosLevel == 3)
                {
                    throw new DecoderException("Illegal QOS Level in fixed header of PUBLISH message ("
                            + qosLevel.ToString(CultureInfo.InvariantCulture) + ')');
                }

                break;

            case var _ when messageType == MqttMessageType.PUBREL:
            case var _ when messageType == MqttMessageType.SUBSCRIBE:
            case var _ when messageType == MqttMessageType.UNSUBSCRIBE:
                if (dupFlag)
                {
                    throw new DecoderException("Illegal BIT 3 in fixed header of " + messageType
                            + " message, must be 0, found 1");
                }
                if (qosLevel != 1)
                {
                    throw new DecoderException("Illegal QOS Level in fixed header of " + messageType
                            + " message, must be 1, found " + qosLevel.ToString(CultureInfo.InvariantCulture));
                }
                if (retain)
                {
                    throw new DecoderException("Illegal BIT 0 in fixed header of " + messageType
                            + " message, must be 0, found 1");
                }

                break;

            case var _ when messageType == MqttMessageType.AUTH:
            case var _ when messageType == MqttMessageType.CONNACK:
            case var _ when messageType == MqttMessageType.CONNECT:
            case var _ when messageType == MqttMessageType.DISCONNECT:
            case var _ when messageType == MqttMessageType.PINGREQ:
            case var _ when messageType == MqttMessageType.PINGRESP:
            case var _ when messageType == MqttMessageType.PUBACK:
            case var _ when messageType == MqttMessageType.PUBCOMP:
            case var _ when messageType == MqttMessageType.PUBREC:
            case var _ when messageType == MqttMessageType.SUBACK:
            case var _ when messageType == MqttMessageType.UNSUBACK:
                if (dupFlag)
                {
                    throw new DecoderException("Illegal BIT 3 in fixed header of " + messageType
                            + " message, must be 0, found 1");
                }
                if (qosLevel != 0)
                {
                    throw new DecoderException("Illegal BIT 2 or 1 in fixed header of " + messageType
                            + " message, must be 0, found " + qosLevel.ToString(CultureInfo.InvariantCulture));
                }
                if (retain)
                {
                    throw new DecoderException("Illegal BIT 0 in fixed header of " + messageType
                            + " message, must be 0, found 1");
                }

                break;
            default:
                throw new DecoderException("Unknown message type, do not know how to validate fixed header");
        }

        int remainingLength = ParseRemainingLength(buffer, messageType, maxAllowedRemainingBytes);
        MqttFixedHeader decodedFixedHeader =
                new MqttFixedHeader(messageType, dupFlag, MqttQoS.ValueOf(qosLevel), retain, remainingLength);
        return ValidateFixedHeader(ctx, ResetUnusedFields(decodedFixedHeader));
    }

    private int ParseRemainingLength(ByteBuf buffer, MqttMessageType messageType, int maxAllowedRemainingBytes)
    {
        int remainingLength = 0;
        int multiplier = 1;

        for (int i = 0; i < 4; i++)
        {
            CheckMaxMessageLengthRemaining(--maxAllowedRemainingBytes);

            short digit = buffer.ReadByte();
            remainingLength += (digit & 127) * multiplier;

            if ((digit & 128) == 0)
            {
                return remainingLength;
            }

            multiplier *= 128;
        }

        // MQTT protocol limits Remaining Length to 4 bytes
        throw new DecoderException("remaining length exceeds 4 digits (" + messageType + ')');
    }

    /**
     * Decodes the variable header (if any)
     *
     * @param buffer                   the buffer to decode from
     * @param mqttFixedHeader          MqttFixedHeader of the same message
     * @param maxAllowedRemainingBytes the maximum number of bytes permitted to remain
     * @return the variable header
     */
    private object DecodeVariableHeader(IChannelHandlerContext ctx, ByteBuf buffer, MqttFixedHeader mqttFixedHeader,
                                        int maxAllowedRemainingBytes)
    {
        MqttMessageType messageType = mqttFixedHeader.MessageType();

        switch (messageType)
        {
            case var _ when messageType == MqttMessageType.CONNECT:
                return DecodeConnectionVariableHeader(ctx, buffer, maxAllowedRemainingBytes);

            case var _ when messageType == MqttMessageType.CONNACK:
                return DecodeConnAckVariableHeader(ctx, buffer, maxAllowedRemainingBytes);

            case var _ when messageType == MqttMessageType.UNSUBSCRIBE:
            case var _ when messageType == MqttMessageType.SUBSCRIBE:
            case var _ when messageType == MqttMessageType.SUBACK:
            case var _ when messageType == MqttMessageType.UNSUBACK:
                return DecodeMessageIdAndPropertiesVariableHeader(ctx, buffer, maxAllowedRemainingBytes);

            case var _ when messageType == MqttMessageType.PUBACK:
            case var _ when messageType == MqttMessageType.PUBREC:
            case var _ when messageType == MqttMessageType.PUBCOMP:
            case var _ when messageType == MqttMessageType.PUBREL:
                return DecodePubReplyMessage(buffer, maxAllowedRemainingBytes);

            case var _ when messageType == MqttMessageType.PUBLISH:
                return DecodePublishVariableHeader(ctx, buffer, mqttFixedHeader, maxAllowedRemainingBytes);

            case var _ when messageType == MqttMessageType.DISCONNECT:
            case var _ when messageType == MqttMessageType.AUTH:
                return DecodeReasonCodeAndPropertiesVariableHeader(buffer, maxAllowedRemainingBytes);

            case var _ when messageType == MqttMessageType.PINGREQ:
            case var _ when messageType == MqttMessageType.PINGRESP:
                // Empty variable header
                return null;
            default:
                //shouldn't reach here
                throw new DecoderException("Unknown message type: " + mqttFixedHeader.MessageType());
        }
    }

    private MqttConnectVariableHeader DecodeConnectionVariableHeader(
        IChannelHandlerContext ctx, ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        Result<string> protoString = DecodeString(buffer, maxAllowedRemainingBytes);
        int numberOfBytesConsumed = protoString.numberOfBytesConsumed;

        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - numberOfBytesConsumed - 1);

        byte protocolLevel = buffer.ReadByte();
        numberOfBytesConsumed += 1;

        MqttVersion version = MqttVersion.FromProtocolNameAndLevel(protoString.value, protocolLevel);
        MqttCodecUtil.SetMqttVersion(ctx, version);

        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - numberOfBytesConsumed - 1);

        int b1 = buffer.ReadByte();
        numberOfBytesConsumed += 1;

        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - numberOfBytesConsumed - 2);

        int keepAlive = DecodeMsbLsb(buffer);
        numberOfBytesConsumed += 2;

        bool hasUserName = (b1 & 0x80) == 0x80;
        bool hasPassword = (b1 & 0x40) == 0x40;
        bool willRetain = (b1 & 0x20) == 0x20;
        int willQos = (b1 & 0x18) >> 3;
        bool willFlag = (b1 & 0x04) == 0x04;
        bool cleanSession = (b1 & 0x02) == 0x02;

        if (version == MqttVersion.MQTT_3_1_1 || version == MqttVersion.MQTT_5)
        {
            bool zeroReservedFlag = (b1 & 0x01) == 0x0;
            if (!zeroReservedFlag)
            {
                // MQTT v3.1.1: The Server MUST validate that the reserved flag in the CONNECT Control Packet is
                // set to zero and disconnect the Client if it is not zero.
                // See https://docs.oasis-open.org/mqtt/mqtt/v3.1.1/os/mqtt-v3.1.1-os.html#_Toc385349230
                throw new DecoderException("non-zero reserved flag");
            }
        }

        MqttProperties properties;

        if (version == MqttVersion.MQTT_5)
        {
            Result<MqttProperties> propertiesResult = DecodeProperties(buffer,
                maxAllowedRemainingBytes - numberOfBytesConsumed);
            properties = propertiesResult.value;
            numberOfBytesConsumed += propertiesResult.numberOfBytesConsumed;
        }
        else
        {
            properties = MqttProperties.NO_PROPERTIES;
        }

        bytesRemainingInVariablePart -= numberOfBytesConsumed;
        this.maxAllowedRemainingBytes -= numberOfBytesConsumed;
        return new MqttConnectVariableHeader(
                version.ProtocolName(),
                version.ProtocolLevel(),
                hasUserName,
                hasPassword,
                willRetain,
                willQos,
                willFlag,
                cleanSession,
                keepAlive,
                properties);
    }

    private MqttConnAckVariableHeader DecodeConnAckVariableHeader(
            IChannelHandlerContext ctx,
            ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        MqttVersion mqttVersion = MqttCodecUtil.GetMqttVersion(ctx);
        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes -= 2);

        bool sessionPresent = (buffer.ReadByte() & 0x01) == 0x01;
        byte returnCode = buffer.ReadByte();

        MqttProperties properties;

        if (mqttVersion == MqttVersion.MQTT_5)
        {
            Result<MqttProperties> propertiesResult = DecodeProperties(buffer, maxAllowedRemainingBytes);
            properties = propertiesResult.value;
            bytesRemainingInVariablePart -= 2 + propertiesResult.numberOfBytesConsumed;
            maxAllowedRemainingBytes -= propertiesResult.numberOfBytesConsumed;
        }
        else
        {
            properties = MqttProperties.NO_PROPERTIES;
            bytesRemainingInVariablePart -= 2;
        }

        this.maxAllowedRemainingBytes = maxAllowedRemainingBytes;

        return new MqttConnAckVariableHeader(MqttConnectReturnCode.ValueOf(returnCode), sessionPresent, properties);
    }

    private MqttMessageIdAndPropertiesVariableHeader DecodeMessageIdAndPropertiesVariableHeader(
            IChannelHandlerContext ctx,
            ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        MqttVersion mqttVersion = MqttCodecUtil.GetMqttVersion(ctx);
        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes -= 2);

        int packetId = DecodeMessageId(buffer);

        if (mqttVersion == MqttVersion.MQTT_5)
        {
            Result<MqttProperties> properties = DecodeProperties(buffer, maxAllowedRemainingBytes);
            bytesRemainingInVariablePart -= 2 + properties.numberOfBytesConsumed;
            this.maxAllowedRemainingBytes = maxAllowedRemainingBytes - properties.numberOfBytesConsumed;
            return new MqttMessageIdAndPropertiesVariableHeader(packetId, properties.value);
        }
        else
        {
            bytesRemainingInVariablePart -= 2;
            this.maxAllowedRemainingBytes = maxAllowedRemainingBytes;
            return new MqttMessageIdAndPropertiesVariableHeader(packetId,
                                                                MqttProperties.NO_PROPERTIES);
        }
    }

    private MqttPubReplyMessageVariableHeader DecodePubReplyMessage(ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        const int packetIdNumberOfBytesConsumed = 2;
        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes -= packetIdNumberOfBytesConsumed);

        int packetId = DecodeMessageId(buffer);

        if (bytesRemainingInVariablePart > 3)
        {
            CheckMaxMessageLengthRemaining(--maxAllowedRemainingBytes);

            byte reasonCode = buffer.ReadByte();
            Result<MqttProperties> properties = DecodeProperties(buffer, maxAllowedRemainingBytes);
            bytesRemainingInVariablePart -= packetIdNumberOfBytesConsumed + 1 + properties.numberOfBytesConsumed;
            this.maxAllowedRemainingBytes = maxAllowedRemainingBytes - properties.numberOfBytesConsumed;
            return new MqttPubReplyMessageVariableHeader(packetId,
                    reasonCode,
                    properties.value);
        }
        else if (bytesRemainingInVariablePart > 2)
        {
            CheckMaxMessageLengthRemaining(--maxAllowedRemainingBytes);

            byte reasonCode = buffer.ReadByte();
            bytesRemainingInVariablePart -= packetIdNumberOfBytesConsumed + 1;
            this.maxAllowedRemainingBytes = maxAllowedRemainingBytes;
            return new MqttPubReplyMessageVariableHeader(packetId,
                    reasonCode,
                    MqttProperties.NO_PROPERTIES);
        }
        else
        {
            bytesRemainingInVariablePart -= packetIdNumberOfBytesConsumed;
            this.maxAllowedRemainingBytes = maxAllowedRemainingBytes;
            return new MqttPubReplyMessageVariableHeader(packetId,
                    (byte) 0,
                    MqttProperties.NO_PROPERTIES);
        }
    }

    private MqttReasonCodeAndPropertiesVariableHeader DecodeReasonCodeAndPropertiesVariableHeader(
            ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        byte reasonCode;
        MqttProperties properties;

        if (bytesRemainingInVariablePart > 1)
        {
            CheckMaxMessageLengthRemaining(--maxAllowedRemainingBytes);

            reasonCode = buffer.ReadByte();
            Result<MqttProperties> propertiesResult = DecodeProperties(buffer, maxAllowedRemainingBytes);
            properties = propertiesResult.value;
            bytesRemainingInVariablePart -= 1 + propertiesResult.numberOfBytesConsumed;
            this.maxAllowedRemainingBytes = maxAllowedRemainingBytes - propertiesResult.numberOfBytesConsumed;
        }
        else if (bytesRemainingInVariablePart > 0)
        {
            CheckMaxMessageLengthRemaining(--maxAllowedRemainingBytes);

            reasonCode = buffer.ReadByte();
            properties = MqttProperties.NO_PROPERTIES;
            --bytesRemainingInVariablePart;
            this.maxAllowedRemainingBytes = maxAllowedRemainingBytes;
        }
        else
        {
            reasonCode = 0;
            properties = MqttProperties.NO_PROPERTIES;
        }

        return new MqttReasonCodeAndPropertiesVariableHeader(reasonCode, properties);
    }

    private MqttPublishVariableHeader DecodePublishVariableHeader(
            IChannelHandlerContext ctx,
            ByteBuf buffer,
            MqttFixedHeader mqttFixedHeader, int maxAllowedRemainingBytes)
    {
        MqttVersion mqttVersion = MqttCodecUtil.GetMqttVersion(ctx);
        Result<string> decodedTopic = DecodeString(buffer, maxAllowedRemainingBytes);
        if (!IsValidPublishTopicName(decodedTopic.value))
        {
            throw new DecoderException("invalid publish topic name: " + (decodedTopic.value ?? "null") +
                    " (contains wildcards)");
        }

        int numberOfBytesConsumed = decodedTopic.numberOfBytesConsumed;
        maxAllowedRemainingBytes -= decodedTopic.numberOfBytesConsumed;

        int messageId = -1;

        if (mqttFixedHeader.QosLevel().Value() > 0)
        {
            CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes -= 2);

            messageId = DecodeMessageId(buffer);
            numberOfBytesConsumed += 2;
        }

        MqttProperties properties;

        if (mqttVersion == MqttVersion.MQTT_5)
        {
            Result<MqttProperties> propertiesResult = DecodeProperties(buffer, maxAllowedRemainingBytes);
            properties = propertiesResult.value;
            numberOfBytesConsumed += propertiesResult.numberOfBytesConsumed;
            maxAllowedRemainingBytes -= propertiesResult.numberOfBytesConsumed;
        }
        else
        {
            properties = MqttProperties.NO_PROPERTIES;
        }

        bytesRemainingInVariablePart -= numberOfBytesConsumed;
        this.maxAllowedRemainingBytes = maxAllowedRemainingBytes;
        return new MqttPublishVariableHeader(decodedTopic.value, messageId, properties);
    }

    /**
     * @return messageId with numberOfBytesConsumed is 2
     */
    private static int DecodeMessageId(ByteBuf buffer)
    {
        int messageId = DecodeMsbLsb(buffer);
        if (!IsValidMessageId(messageId))
        {
            throw new DecoderException("invalid messageId: " + messageId.ToString(CultureInfo.InvariantCulture));
        }

        return messageId;
    }

    /**
     * Decodes the payload.
     *
     * @param buffer the buffer to decode from
     * @param messageType  type of the message being decoded
     * @param variableHeader variable header of the same message
     * @return the payload
     */
    private object DecodePayload(
            ByteBuf buffer,
            MqttMessageType messageType,
            int maxClientIdLength,
            object variableHeader,
            int maxAllowedRemainingBytes)
    {
        switch (messageType)
        {
            case var _ when messageType == MqttMessageType.CONNECT:
                return DecodeConnectionPayload(buffer, maxClientIdLength, (MqttConnectVariableHeader) variableHeader,
                    maxAllowedRemainingBytes);

            case var _ when messageType == MqttMessageType.SUBSCRIBE:
                return DecodeSubscribePayload(buffer, maxAllowedRemainingBytes);

            case var _ when messageType == MqttMessageType.SUBACK:
                return DecodeSubackPayload(buffer, maxAllowedRemainingBytes);

            case var _ when messageType == MqttMessageType.UNSUBSCRIBE:
                return DecodeUnsubscribePayload(buffer, maxAllowedRemainingBytes);

            case var _ when messageType == MqttMessageType.UNSUBACK:
                return DecodeUnsubAckPayload(buffer, maxAllowedRemainingBytes);

            case var _ when messageType == MqttMessageType.PUBLISH:
                return DecodePublishPayload(buffer, maxAllowedRemainingBytes);

            default:
                // No payload for this message type. If the fixed header's Remaining Length
                // claimed bytes beyond what the variable header consumed (e.g. a PINGREQ
                // with non-zero Remaining Length), the frame is malformed.
                // See https://github.com/netty/netty/issues/16851
                ValidateNoBytesRemain(0);
                return null;
        }
    }

    private MqttConnectPayload DecodeConnectionPayload(
            ByteBuf buffer,
            int maxClientIdLength,
            MqttConnectVariableHeader mqttConnectVariableHeader,
            int maxAllowedRemainingBytes)
    {
        Result<string> decodedClientId = DecodeString(buffer, maxAllowedRemainingBytes);
        string decodedClientIdValue = decodedClientId.value;
        MqttVersion mqttVersion = MqttVersion.FromProtocolNameAndLevel(mqttConnectVariableHeader.Name(),
                (byte) mqttConnectVariableHeader.Version());
        if (!IsValidClientId(mqttVersion, maxClientIdLength, decodedClientIdValue, !strictUtf8Validation))
        {
            throw new MqttIdentifierRejectedException("invalid clientIdentifier");
        }

        int numberOfBytesConsumed = decodedClientId.numberOfBytesConsumed;
        maxAllowedRemainingBytes -= decodedClientId.numberOfBytesConsumed;

        Result<string> decodedWillTopic = null;
        byte[] decodedWillMessage = null;

        MqttProperties willProperties;

        if (mqttConnectVariableHeader.IsWillFlag())
        {
            if (mqttVersion == MqttVersion.MQTT_5)
            {
                Result<MqttProperties> propertiesResult = DecodeProperties(buffer, maxAllowedRemainingBytes);
                willProperties = propertiesResult.value;
                numberOfBytesConsumed += propertiesResult.numberOfBytesConsumed;
                maxAllowedRemainingBytes -= propertiesResult.numberOfBytesConsumed;
            }
            else
            {
                willProperties = MqttProperties.NO_PROPERTIES;
            }

            decodedWillTopic = DecodeString(buffer, 0, 32767, maxAllowedRemainingBytes);
            numberOfBytesConsumed += decodedWillTopic.numberOfBytesConsumed;
            maxAllowedRemainingBytes -= decodedWillTopic.numberOfBytesConsumed;
            decodedWillMessage = DecodeByteArray(buffer, maxAllowedRemainingBytes);
            numberOfBytesConsumed += decodedWillMessage.Length + 2;
            maxAllowedRemainingBytes -= decodedWillMessage.Length + 2;
        }
        else
        {
            willProperties = MqttProperties.NO_PROPERTIES;
        }

        Result<string> decodedUserName = null;
        byte[] decodedPassword = null;

        if (mqttConnectVariableHeader.HasUserName())
        {
            decodedUserName = DecodeString(buffer, maxAllowedRemainingBytes);
            numberOfBytesConsumed += decodedUserName.numberOfBytesConsumed;
            maxAllowedRemainingBytes -= decodedUserName.numberOfBytesConsumed;
        }

        if (mqttConnectVariableHeader.HasPassword())
        {
            decodedPassword = DecodeByteArray(buffer, maxAllowedRemainingBytes);
            numberOfBytesConsumed += decodedPassword.Length + 2;
            maxAllowedRemainingBytes -= decodedPassword.Length + 2;
        }

        ValidateNoBytesRemain(numberOfBytesConsumed);
        this.maxAllowedRemainingBytes = maxAllowedRemainingBytes;
        return new MqttConnectPayload(
                        decodedClientId.value,
                        willProperties,
                        decodedWillTopic != null ? decodedWillTopic.value : null,
                        decodedWillMessage,
                        decodedUserName != null ? decodedUserName.value : null,
                        decodedPassword);
    }

    private MqttSubscribePayload DecodeSubscribePayload(
            ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        List<MqttTopicSubscription> subscribeTopics = new List<MqttTopicSubscription>();
        int numberOfBytesConsumed = 0;

        while (numberOfBytesConsumed < bytesRemainingInVariablePart)
        {
            Result<string> decodedTopicName = DecodeString(buffer, maxAllowedRemainingBytes);
            numberOfBytesConsumed += decodedTopicName.numberOfBytesConsumed;
            maxAllowedRemainingBytes -= decodedTopicName.numberOfBytesConsumed;
            //See 3.8.3.1 Subscription Options of MQTT 5.0 specification for optionByte details
            CheckMaxMessageLengthRemaining(--maxAllowedRemainingBytes);

            short optionByte = buffer.ReadByte();

            MqttQoS qos = MqttQoS.ValueOf(optionByte & 0x03);
            bool noLocal = ((optionByte & 0x04) >> 2) == 1;
            bool retainAsPublished = ((optionByte & 0x08) >> 3) == 1;
            RetainedHandlingPolicy retainHandling = RetainedHandlingPolicy.ValueOf((optionByte & 0x30) >> 4);

            MqttSubscriptionOption subscriptionOption = new MqttSubscriptionOption(qos,
                    noLocal,
                    retainAsPublished,
                    retainHandling);

            numberOfBytesConsumed++;
            subscribeTopics.Add(new MqttTopicSubscription(decodedTopicName.value, subscriptionOption));
        }

        ValidateNoBytesRemain(numberOfBytesConsumed);
        this.maxAllowedRemainingBytes = maxAllowedRemainingBytes;
        return new MqttSubscribePayload(subscribeTopics);
    }

    private MqttSubAckPayload DecodeSubackPayload(
            ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        int bytesRemainingInVariablePart = this.bytesRemainingInVariablePart;
        List<int> grantedQos = new List<int>(bytesRemainingInVariablePart);
        int numberOfBytesConsumed = 0;

        while (numberOfBytesConsumed < bytesRemainingInVariablePart)
        {
            CheckMaxMessageLengthRemaining(--maxAllowedRemainingBytes);

            int reasonCode = buffer.ReadByte();
            numberOfBytesConsumed++;
            grantedQos.Add(reasonCode);
        }

        ValidateNoBytesRemain(numberOfBytesConsumed);
        this.maxAllowedRemainingBytes = maxAllowedRemainingBytes;
        return new MqttSubAckPayload(grantedQos);
    }

    private MqttUnsubAckPayload DecodeUnsubAckPayload(
        ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        int bytesRemainingInVariablePart = this.bytesRemainingInVariablePart;
        List<short> reasonCodes = new List<short>(bytesRemainingInVariablePart);
        int numberOfBytesConsumed = 0;

        while (numberOfBytesConsumed < bytesRemainingInVariablePart)
        {
            CheckMaxMessageLengthRemaining(--maxAllowedRemainingBytes);

            short reasonCode = buffer.ReadByte();
            numberOfBytesConsumed++;
            reasonCodes.Add(reasonCode);
        }

        ValidateNoBytesRemain(numberOfBytesConsumed);
        this.maxAllowedRemainingBytes = maxAllowedRemainingBytes;
        return new MqttUnsubAckPayload(reasonCodes);
    }

    private MqttUnsubscribePayload DecodeUnsubscribePayload(
            ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        List<string> unsubscribeTopics = new List<string>();
        int numberOfBytesConsumed = 0;

        while (numberOfBytesConsumed < bytesRemainingInVariablePart)
        {
            Result<string> decodedTopicName = DecodeString(buffer, maxAllowedRemainingBytes);
            numberOfBytesConsumed += decodedTopicName.numberOfBytesConsumed;
            maxAllowedRemainingBytes -= decodedTopicName.numberOfBytesConsumed;
            unsubscribeTopics.Add(decodedTopicName.value);
        }

        ValidateNoBytesRemain(numberOfBytesConsumed);
        this.maxAllowedRemainingBytes = maxAllowedRemainingBytes;
        return new MqttUnsubscribePayload(unsubscribeTopics);
    }

    private ByteBuf DecodePublishPayload(ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - bytesRemainingInVariablePart);

        ByteBuf payload = buffer.ReadRetainedSlice(bytesRemainingInVariablePart); // May throw REPLAY
        this.maxAllowedRemainingBytes = maxAllowedRemainingBytes - bytesRemainingInVariablePart;
        return payload;
    }

    private void ValidateNoBytesRemain(int numberOfBytesConsumed)
    {
        bytesRemainingInVariablePart -= numberOfBytesConsumed;
        if (bytesRemainingInVariablePart != 0)
        {
            throw new DecoderException(
                    "non-zero remaining payload bytes: " +
                    bytesRemainingInVariablePart.ToString(CultureInfo.InvariantCulture) + " (" +
                    mqttFixedHeader.MessageType() + ')');
        }
    }

    private Result<string> DecodeString(ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        return DecodeString(buffer, 0, int.MaxValue, maxAllowedRemainingBytes);
    }

    private Result<string> DecodeString(ByteBuf buffer, int minBytes, int maxBytes, int maxAllowedRemainingBytes)
    {
        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes -= 2);

        int size = DecodeMsbLsb(buffer);
        int numberOfBytesConsumed = 2;
        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - size);

        if (size < minBytes || size > maxBytes)
        {
            buffer.SkipBytes(size);
            numberOfBytesConsumed += size;
            return new Result<string>(null, numberOfBytesConsumed);
        }

        string s;

        if (strictUtf8Validation)
        {
            s = ReadStrictUtf8(buffer, size);
        }
        else
        {
            s = buffer.ReadString(size, Encoding.UTF8);
        }

        numberOfBytesConsumed += size;
        return new Result<string>(s, numberOfBytesConsumed);
    }

    /**
     * Reads {@code length} bytes from {@code buffer} and decodes them as a strictly validated
     * UTF-8 Encoded String per MQTT 3.1.1 and MQTT 5.0.
     * Throws a {@link DecoderException} if the sequence is malformed or contains U+0000.
     */
    // CLR: ByteBuf.nioBufferCount() has no counterpart. The decoder throws ArgumentException where the source
    // catches CharacterCodingException, and Encoding.GetString keeps no state between calls, so
    // CharsetDecoder.reset() has no counterpart.
    private string ReadStrictUtf8(ByteBuf buffer, int length)
    {
        if (length == 0)
        {
            return "";
        }

        int readerIndex = buffer.ReaderIndex;
        ReadOnlySequence<byte> nioBuf = buffer.AsReadOnlySequence(readerIndex, length);
        if (!nioBuf.IsSingleSegment)
        {
            // Composite/multi-component buffer: copy out to ensure a contiguous view for the
            // CharsetDecoder. Strict UTF-8 validation requires examining all bytes anyway.
            byte[] tmp = new byte[length];
            buffer.GetBytes(readerIndex, tmp);
            nioBuf = new ReadOnlySequence<byte>(tmp);
        }

        if (utf8Decoder == null)
        {
            utf8Decoder = new UTF8Encoding(false, true);
        }

        string s;

        try
        {
            s = utf8Decoder.GetString(nioBuf.FirstSpan);
        }
        catch (ArgumentException e)
        {
            buffer.SkipBytes(length);
            throw new DecoderException("invalid UTF-8 string in MQTT packet", e);
        }

        buffer.SkipBytes(length);
        // The UTF-8 Encoded String MUST NOT include an encoding
        // of the null character U+0000. If received, this is a Malformed Packet.
        if (s.IndexOf('\u0000') >= 0)
        {
            throw new DecoderException("MQTT UTF-8 Encoded String must not contain U+0000");
        }

        return s;
    }

    /**
     *
     * @return the decoded byte[], numberOfBytesConsumed = byte[].length + 2
     */
    private byte[] DecodeByteArray(ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes -= 2);

        int size = DecodeMsbLsb(buffer);
        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - size);

        byte[] bytes = new byte[size];
        buffer.ReadBytes(bytes);
        return bytes;
    }

    // packing utils to reduce the amount of garbage while decoding ints
    private static long PackInts(int a, int b)
    {
        return (((long) a) << 32) | (b & 0xFFFFFFFFL);
    }

    private static int UnpackA(long ints)
    {
        return (int) (ints >> 32);
    }

    private static int UnpackB(long ints)
    {
        return (int) ints;
    }

    /**
     *  numberOfBytesConsumed = 2. return decoded result.
     */
    private static int DecodeMsbLsb(ByteBuf buffer)
    {
        int min = 0;
        int max = 65535;
        // CLR: readUnsignedByte() returns the unsigned value as a Java short and ReadByte() as a byte (owner decision);
        // a short operand of | is sign-extended (warning CS0675), so the locals are byte, which widen without sign.
        byte msbSize = buffer.ReadByte();
        byte lsbSize = buffer.ReadByte();
        int result = msbSize << 8 | lsbSize;
        if (result < min || result > max)
        {
            result = -1;
        }

        return result;
    }

    /**
     * See 1.5.5 Variable Byte Integer section of MQTT 5.0 specification for encoding/decoding rules
     *
     * @param buffer the buffer to decode from
     * @return result pack with a = decoded integer, b = numberOfBytesConsumed. Need to unpack to read them.
     * @throws DecoderException if bad MQTT protocol limits Remaining Length
     */
    private long DecodeVariableByteInteger(ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        int remainingLength = 0;
        int multiplier = 1;

        for (int i = 0; i < 4; i++)
        {
            CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - (i + 1));

            short digit = buffer.ReadByte();
            remainingLength += (digit & 127) * multiplier;

            if ((digit & 128) == 0)
            {
                return PackInts(remainingLength, i + 1);
            }

            multiplier *= 128;
        }

        throw new DecoderException("MQTT protocol limits Remaining Length to 4 bytes");
    }

    // CLR: the decoder reads the private fields of this nested class; Java permits that within the top level class
    // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-6.html#jls-6.6.1), while a C# private member is accessible
    // "only within the body of the class" that declares it
    // (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/private), so the members are internal.
    private sealed class Result<T>
    {

        internal readonly T value;
        internal readonly int numberOfBytesConsumed;

        internal Result(T value, int numberOfBytesConsumed)
        {
            this.value = value;
            this.numberOfBytesConsumed = numberOfBytesConsumed;
        }
    }

    private Result<MqttProperties> DecodeProperties(ByteBuf buffer, int maxAllowedRemainingBytes)
    {
        long propertiesLength = DecodeVariableByteInteger(buffer, maxAllowedRemainingBytes);
        int totalPropertiesLength = UnpackA(propertiesLength);
        // Number of bytes used by the Property Length Variable Byte Integer itself. These bytes are
        // part of the value returned by this method, but they must not be counted against
        // totalPropertiesLength, which only covers the properties content that follows the length.
        int propertiesLengthNumBytes = UnpackB(propertiesLength);
        int numberOfBytesConsumed = UnpackB(propertiesLength);
        CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - numberOfBytesConsumed);

        if (totalPropertiesLength > 0)
        {
            CheckMaxMessageLengthRemaining(
                maxAllowedRemainingBytes - numberOfBytesConsumed - totalPropertiesLength);

            // Force an early REPLAY when the buffer does not yet have the full properties block,
            // so we don't repeatedly parse partial properties as data arrives. A direct
            // buffer.readableBytes() check is unusable here because ReplayingDecoderByteBuf
            // returns Integer.MAX_VALUE - readerIndex; touching the last byte via getByte()
            // routes through ReplayingDecoderByteBuf.checkIndex(), which throws REPLAY if the
            // buffer's writerIndex hasn't reached that position yet.
            buffer.GetByte(buffer.ReaderIndex + totalPropertiesLength - 1);
        }

        MqttProperties decodedProperties = new MqttProperties();

        while (numberOfBytesConsumed - propertiesLengthNumBytes < totalPropertiesLength)
        {
            long propertyId = DecodeVariableByteInteger(buffer, maxAllowedRemainingBytes - numberOfBytesConsumed);
            int propertyIdValue = UnpackA(propertyId);
            numberOfBytesConsumed += UnpackB(propertyId);
            CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - numberOfBytesConsumed);

            switch (propertyIdValue)
            {
                case PAYLOAD_FORMAT_INDICATOR:
                case REQUEST_PROBLEM_INFORMATION:
                case REQUEST_RESPONSE_INFORMATION:
                case MAXIMUM_QOS:
                case RETAIN_AVAILABLE:
                case WILDCARD_SUBSCRIPTION_AVAILABLE:
                case SUBSCRIPTION_IDENTIFIER_AVAILABLE:
                case SHARED_SUBSCRIPTION_AVAILABLE:
                    CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - numberOfBytesConsumed - 1);

                    int b1 = buffer.ReadByte();
                    numberOfBytesConsumed++;
                    decodedProperties.Add(new IntegerProperty(propertyIdValue, b1));
                    break;
                case SERVER_KEEP_ALIVE:
                case RECEIVE_MAXIMUM:
                case TOPIC_ALIAS_MAXIMUM:
                case TOPIC_ALIAS:
                    CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - numberOfBytesConsumed - 2);

                    int int2BytesResult = DecodeMsbLsb(buffer);
                    numberOfBytesConsumed += 2;
                    decodedProperties.Add(new IntegerProperty(propertyIdValue, int2BytesResult));
                    break;
                case PUBLICATION_EXPIRY_INTERVAL:
                case SESSION_EXPIRY_INTERVAL:
                case WILL_DELAY_INTERVAL:
                case MAXIMUM_PACKET_SIZE:
                    CheckMaxMessageLengthRemaining(maxAllowedRemainingBytes - numberOfBytesConsumed - 4);

                    int maxPacketSize = buffer.ReadInt();
                    numberOfBytesConsumed += 4;
                    decodedProperties.Add(new IntegerProperty(propertyIdValue, maxPacketSize));
                    break;
                case SUBSCRIPTION_IDENTIFIER:
                    long vbIntegerResult = DecodeVariableByteInteger(buffer,
                        maxAllowedRemainingBytes - numberOfBytesConsumed);
                    numberOfBytesConsumed += UnpackB(vbIntegerResult);
                    decodedProperties.Add(new IntegerProperty(propertyIdValue, UnpackA(vbIntegerResult)));
                    break;
                case CONTENT_TYPE:
                case RESPONSE_TOPIC:
                case ASSIGNED_CLIENT_IDENTIFIER:
                case AUTHENTICATION_METHOD:
                case RESPONSE_INFORMATION:
                case SERVER_REFERENCE:
                case REASON_STRING:
                    Result<string> stringResult = DecodeString(buffer,
                        maxAllowedRemainingBytes - numberOfBytesConsumed);
                    numberOfBytesConsumed += stringResult.numberOfBytesConsumed;
                    decodedProperties.Add(new MqttProperties.StringProperty(propertyIdValue, stringResult.value));
                    break;
                case USER_PROPERTY:
                    Result<string> keyResult = DecodeString(buffer,
                        maxAllowedRemainingBytes - numberOfBytesConsumed);
                    Result<string> valueResult = DecodeString(buffer,
                        maxAllowedRemainingBytes - numberOfBytesConsumed - keyResult.numberOfBytesConsumed);
                    numberOfBytesConsumed += keyResult.numberOfBytesConsumed;
                    numberOfBytesConsumed += valueResult.numberOfBytesConsumed;
                    decodedProperties.Add(new MqttProperties.UserProperty(keyResult.value, valueResult.value));
                    break;
                case CORRELATION_DATA:
                case AUTHENTICATION_DATA:
                    byte[] binaryDataResult = DecodeByteArray(buffer,
                        maxAllowedRemainingBytes - numberOfBytesConsumed);
                    numberOfBytesConsumed += binaryDataResult.Length + 2;
                    decodedProperties.Add(new MqttProperties.BinaryProperty(propertyIdValue, binaryDataResult));
                    break;
                default:
                    //shouldn't reach here
                    throw new DecoderException("Unknown property type: " +
                            propertyIdValue.ToString(CultureInfo.InvariantCulture));
            }

            if (numberOfBytesConsumed - propertiesLengthNumBytes > totalPropertiesLength)
            {
                // The typed value of the last decoded property read past the declared Property
                // Length, e.g. a Content Type string whose length prefix claims more bytes than
                // are left in the Properties field. MQTT 5.0 section 2.2.2.2 states a Property
                // whose value is not of the specified data type is a Malformed Packet, so reject
                // it instead of letting the read spill into the following packet section.
                throw new DecoderException(
                    "MQTT5 property overran declared Property Length: propertyId=" +
                    propertyIdValue.ToString(CultureInfo.InvariantCulture));
            }
        }

        return new Result<MqttProperties>(decodedProperties, numberOfBytesConsumed);
    }
}
