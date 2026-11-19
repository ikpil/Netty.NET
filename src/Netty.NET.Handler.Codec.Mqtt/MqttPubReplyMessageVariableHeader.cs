/*
 * Copyright 2020 The Netty Project
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
using System.Globalization;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Variable Header containing Packet Id, reason code and Properties as in MQTT v5 spec.
 */
public sealed class MqttPubReplyMessageVariableHeader : MqttMessageIdVariableHeader
{
    private readonly byte reasonCode;
    private readonly MqttProperties properties;

    public const byte REASON_CODE_OK = 0;

    public MqttPubReplyMessageVariableHeader(int messageId, byte reasonCode, MqttProperties properties)
        : base(messageId)
    {
        if (messageId < 1 || messageId > 0xffff)
        {
            throw new ArgumentException("messageId: " + messageId.ToString(CultureInfo.InvariantCulture) +
                    " (expected: 1 ~ 65535)");
        }
        this.reasonCode = reasonCode;
        this.properties = MqttProperties.WithEmptyDefaults(properties);
    }

    public byte ReasonCode()
    {
        return reasonCode;
    }

    public MqttProperties Properties()
    {
        return properties;
    }

    // CLR: the reason code is a Java byte, which string conversion prints signed (-128..127,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1); the C# byte is unsigned, so it is printed
    // through sbyte to keep the Java text (0x87 is "-121"). Numbers use the invariant culture.
    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "[" +
                "messageId=" + MessageId().ToString(CultureInfo.InvariantCulture) +
                ", reasonCode=" + ((sbyte) reasonCode).ToString(CultureInfo.InvariantCulture) +
                ", properties=" + properties +
                ']';
    }
}
