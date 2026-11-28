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
 * Variable Header containing, Packet Id and Properties as in MQTT v5 spec.
 */
public sealed class MqttMessageIdAndPropertiesVariableHeader : MqttMessageIdVariableHeader
{
    private readonly MqttProperties properties;

    public MqttMessageIdAndPropertiesVariableHeader(int messageId, MqttProperties properties) : base(messageId)
    {
        if (messageId < 1 || messageId > 0xffff)
        {
            throw new ArgumentException("messageId: " + messageId.ToString(CultureInfo.InvariantCulture) +
                    " (expected: 1 ~ 65535)");
        }

        this.properties = MqttProperties.WithEmptyDefaults(properties);
    }

    public MqttProperties Properties()
    {
        return properties;
    }

    // CLR: the int uses the invariant culture (https://learn.microsoft.com/dotnet/api/system.int32.tostring).
    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "[" +
                "messageId=" + MessageId().ToString(CultureInfo.InvariantCulture) +
                ", properties=" + properties +
                ']';
    }

    internal override MqttMessageIdAndPropertiesVariableHeader WithDefaultEmptyProperties()
    {
        return this;
    }
}
