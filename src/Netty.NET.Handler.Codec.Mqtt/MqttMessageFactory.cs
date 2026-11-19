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
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Utility class with factory methods to create different types of MQTT messages.
 */
// CLR: the switch over the MqttMessageType constants uses case guards (see MqttCodecUtil); Throwable is Exception.
public sealed class MqttMessageFactory
{
    public static MqttMessage NewMessage(MqttFixedHeader mqttFixedHeader, object variableHeader, object payload)
    {
        MqttMessageType messageType = mqttFixedHeader.MessageType();
        switch (messageType)
        {
            case var _ when messageType == MqttMessageType.CONNECT:
                return new MqttConnectMessage(
                        mqttFixedHeader,
                        (MqttConnectVariableHeader) variableHeader,
                        (MqttConnectPayload) payload);

            case var _ when messageType == MqttMessageType.CONNACK:
                return new MqttConnAckMessage(mqttFixedHeader, (MqttConnAckVariableHeader) variableHeader);

            case var _ when messageType == MqttMessageType.SUBSCRIBE:
                return new MqttSubscribeMessage(
                        mqttFixedHeader,
                        (MqttMessageIdVariableHeader) variableHeader,
                        (MqttSubscribePayload) payload);

            case var _ when messageType == MqttMessageType.SUBACK:
                return new MqttSubAckMessage(
                        mqttFixedHeader,
                        (MqttMessageIdVariableHeader) variableHeader,
                        (MqttSubAckPayload) payload);

            case var _ when messageType == MqttMessageType.UNSUBACK:
                return new MqttUnsubAckMessage(
                        mqttFixedHeader,
                        (MqttMessageIdVariableHeader) variableHeader,
                        (MqttUnsubAckPayload) payload);

            case var _ when messageType == MqttMessageType.UNSUBSCRIBE:
                return new MqttUnsubscribeMessage(
                        mqttFixedHeader,
                        (MqttMessageIdVariableHeader) variableHeader,
                        (MqttUnsubscribePayload) payload);

            case var _ when messageType == MqttMessageType.PUBLISH:
                return new MqttPublishMessage(
                        mqttFixedHeader,
                        (MqttPublishVariableHeader) variableHeader,
                        (ByteBuf) payload);

            case var _ when messageType == MqttMessageType.PUBACK:
                //Having MqttPubReplyMessageVariableHeader or MqttMessageIdVariableHeader
                return new MqttPubAckMessage(mqttFixedHeader, (MqttMessageIdVariableHeader) variableHeader);
            case var _ when messageType == MqttMessageType.PUBREC:
            case var _ when messageType == MqttMessageType.PUBREL:
            case var _ when messageType == MqttMessageType.PUBCOMP:
                //Having MqttPubReplyMessageVariableHeader or MqttMessageIdVariableHeader
                return new MqttMessage(mqttFixedHeader, variableHeader);

            case var _ when messageType == MqttMessageType.PINGREQ:
            case var _ when messageType == MqttMessageType.PINGRESP:
                return new MqttMessage(mqttFixedHeader);

            case var _ when messageType == MqttMessageType.DISCONNECT:
            case var _ when messageType == MqttMessageType.AUTH:
                //Having MqttReasonCodeAndPropertiesVariableHeader
                return new MqttMessage(mqttFixedHeader,
                        variableHeader);

            default:
                throw new ArgumentException("unknown message type: " + mqttFixedHeader.MessageType());
        }
    }

    public static MqttMessage NewInvalidMessage(Exception cause)
    {
        return new MqttMessage(null, null, null, DecoderResult.Failure(cause));
    }

    public static MqttMessage NewInvalidMessage(MqttFixedHeader mqttFixedHeader, object variableHeader,
                                                Exception cause)
    {
        return new MqttMessage(mqttFixedHeader, variableHeader, null, DecoderResult.Failure(cause));
    }

    private MqttMessageFactory() { }
}
