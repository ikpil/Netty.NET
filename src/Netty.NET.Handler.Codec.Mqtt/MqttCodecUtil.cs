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
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Mqtt.MqttConstant;

namespace Netty.NET.Handler.Codec.Mqtt;

// CLR: a Java switch over an enum constant becomes a switch whose case labels are case guards comparing with the
// static readonly constants, because the constants are class instances and a case label is a pattern ("Specify a case
// guard after the when keyword", https://learn.microsoft.com/dotnet/csharp/language-reference/statements/selection-statements);
// the labels and their order are kept. The package-private class is internal.
internal sealed class MqttCodecUtil
{
    internal static readonly AttributeKey<MqttVersion> MQTT_VERSION_KEY =
            AttributeKey<MqttVersion>.ValueOf("NETTY_CODEC_MQTT_VERSION");

    internal static MqttVersion GetMqttVersion(IChannelHandlerContext ctx)
    {
        IAttribute<MqttVersion> attr = ctx.Channel.Attr(MQTT_VERSION_KEY);
        MqttVersion version = attr.Get();
        if (version == null)
        {
            return MqttVersion.MQTT_3_1_1;
        }
        return version;
    }

    internal static void SetMqttVersion(IChannelHandlerContext ctx, MqttVersion version)
    {
        IAttribute<MqttVersion> attr = ctx.Channel.Attr(MQTT_VERSION_KEY);
        attr.Set(version);
    }

    internal static bool IsValidPublishTopicName(string topicName)
    {
        if (topicName == null)
        {
            return false;
        }
        // publish topic name must not contain any wildcard
        for (int i = 0; i < topicName.Length; i++)
        {
            char c = topicName[i];
            if (c == '#' || c == '+' || c == '\0')
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsValidMessageId(int messageId)
    {
        return messageId != 0;
    }

    internal static bool IsValidUserName(string userName)
    {
        return userName == null || userName.IndexOf('\0') == -1;
    }

    /**
     * Determine if a client identifier is valid.
     * @param mqttVersion The MQTT version semantics to use.
     * @param maxClientIdLength The max client id length.
     * @param clientId The client id value.
     * @param acceptNulBytes MQTT normally does not allow NUL bytes in client identifiers.
     * Set this to {@code true} to enable "legacy"/"lenient" mode, otherwise {@code false} for strict spec compliance.
     * @return {@code true} if the client id is valid, otherwise {@code false}.
     */
    internal static bool IsValidClientId(MqttVersion mqttVersion, int maxClientIdLength, string clientId,
                                   bool acceptNulBytes)
    {
        if (clientId == null || (!acceptNulBytes && clientId.IndexOf('\0') != -1))
        {
            return false;
        }
        if (mqttVersion == MqttVersion.MQTT_3_1)
        {
            return clientId.Length >= MIN_CLIENT_ID_LENGTH && clientId.Length <= maxClientIdLength;
        }
        if (mqttVersion == MqttVersion.MQTT_3_1_1 || mqttVersion == MqttVersion.MQTT_5)
        {
            // In 3.1.3.1 Client Identifier of MQTT 3.1.1 and 5.0 specifications, The Server MAY allow ClientId’s
            // that contain more than 23 encoded bytes. And, The Server MAY allow zero-length ClientId.
            return true;
        }
        // CLR: Java string conversion writes null as "null" (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11).
        throw new ArgumentException((mqttVersion?.ToString() ?? "null") + " is unknown mqtt version");
    }

    internal static MqttFixedHeader ValidateFixedHeader(IChannelHandlerContext ctx, MqttFixedHeader mqttFixedHeader)
    {
        MqttMessageType messageType = mqttFixedHeader.MessageType();
        switch (messageType)
        {
            case var _ when messageType == MqttMessageType.PUBREL:
            case var _ when messageType == MqttMessageType.SUBSCRIBE:
            case var _ when messageType == MqttMessageType.UNSUBSCRIBE:
                if (mqttFixedHeader.QosLevel() != MqttQoS.AT_LEAST_ONCE)
                {
                    throw new DecoderException(mqttFixedHeader.MessageType().Name() + " message must have QoS 1");
                }
                return mqttFixedHeader;
            case var _ when messageType == MqttMessageType.AUTH:
                if (MqttCodecUtil.GetMqttVersion(ctx) != MqttVersion.MQTT_5)
                {
                    throw new DecoderException("AUTH message requires at least MQTT 5");
                }
                return mqttFixedHeader;
            default:
                return mqttFixedHeader;
        }
    }

    internal static MqttFixedHeader ResetUnusedFields(MqttFixedHeader mqttFixedHeader)
    {
        MqttMessageType messageType = mqttFixedHeader.MessageType();
        switch (messageType)
        {
            case var _ when messageType == MqttMessageType.CONNECT:
            case var _ when messageType == MqttMessageType.CONNACK:
            case var _ when messageType == MqttMessageType.PUBACK:
            case var _ when messageType == MqttMessageType.PUBREC:
            case var _ when messageType == MqttMessageType.PUBCOMP:
            case var _ when messageType == MqttMessageType.SUBACK:
            case var _ when messageType == MqttMessageType.UNSUBACK:
            case var _ when messageType == MqttMessageType.PINGREQ:
            case var _ when messageType == MqttMessageType.PINGRESP:
            case var _ when messageType == MqttMessageType.DISCONNECT:
                if (mqttFixedHeader.IsDup() ||
                        mqttFixedHeader.QosLevel() != MqttQoS.AT_MOST_ONCE ||
                        mqttFixedHeader.IsRetain())
                {
                    return new MqttFixedHeader(
                            mqttFixedHeader.MessageType(),
                            false,
                            MqttQoS.AT_MOST_ONCE,
                            false,
                            mqttFixedHeader.RemainingLength());
                }
                return mqttFixedHeader;
            case var _ when messageType == MqttMessageType.PUBREL:
            case var _ when messageType == MqttMessageType.SUBSCRIBE:
            case var _ when messageType == MqttMessageType.UNSUBSCRIBE:
                if (mqttFixedHeader.IsRetain())
                {
                    return new MqttFixedHeader(
                            mqttFixedHeader.MessageType(),
                            mqttFixedHeader.IsDup(),
                            mqttFixedHeader.QosLevel(),
                            false,
                            mqttFixedHeader.RemainingLength());
                }
                return mqttFixedHeader;
            default:
                return mqttFixedHeader;
        }
    }

    private MqttCodecUtil() { }
}
