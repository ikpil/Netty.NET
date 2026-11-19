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

using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Base class for all MQTT message types.
 */
// CLR: the subclasses narrow variableHeader()/payload() with covariant return types, which C# override methods
// support (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/override), so both accessors are
// virtual here.
public class MqttMessage
{
    private readonly MqttFixedHeader mqttFixedHeader;
    private readonly object variableHeader;
    private readonly object payload;
    private readonly DecoderResult decoderResult;

    // Constants for fixed-header only message types with all flags set to 0 (see
    // https://docs.oasis-open.org/mqtt/mqtt/v3.1.1/os/mqtt-v3.1.1-os.html#_Table_2.2_-)
    public static readonly MqttMessage PINGREQ = new MqttMessage(new MqttFixedHeader(MqttMessageType.PINGREQ, false,
            MqttQoS.AT_MOST_ONCE, false, 0));

    public static readonly MqttMessage PINGRESP = new MqttMessage(new MqttFixedHeader(MqttMessageType.PINGRESP, false,
            MqttQoS.AT_MOST_ONCE, false, 0));

    public static readonly MqttMessage DISCONNECT = new MqttMessage(new MqttFixedHeader(MqttMessageType.DISCONNECT, false,
            MqttQoS.AT_MOST_ONCE, false, 0));

    public MqttMessage(MqttFixedHeader mqttFixedHeader) : this(mqttFixedHeader, null, null)
    {
    }

    public MqttMessage(MqttFixedHeader mqttFixedHeader, object variableHeader) : this(mqttFixedHeader, variableHeader, null)
    {
    }

    // CLR: inside this class the simple name DecoderResult binds to the DecoderResult() method, so the type is
    // qualified through its Netty.NET.Handler.Codec namespace.
    public MqttMessage(MqttFixedHeader mqttFixedHeader, object variableHeader, object payload)
        : this(mqttFixedHeader, variableHeader, payload, Codec.DecoderResult.SUCCESS)
    {
    }

    public MqttMessage(
            MqttFixedHeader mqttFixedHeader,
            object variableHeader,
            object payload,
            DecoderResult decoderResult)
    {
        this.mqttFixedHeader = mqttFixedHeader;
        this.variableHeader = variableHeader;
        this.payload = payload;
        this.decoderResult = decoderResult;
    }

    public MqttFixedHeader FixedHeader()
    {
        return mqttFixedHeader;
    }

    public virtual object VariableHeader()
    {
        return variableHeader;
    }

    public virtual object Payload()
    {
        return payload;
    }

    public DecoderResult DecoderResult()
    {
        return decoderResult;
    }

    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
            .Append('[')
            .Append("fixedHeader=").Append(FixedHeader() != null ? FixedHeader().ToString() : "")
            .Append(", variableHeader=").Append(VariableHeader() != null ? variableHeader.ToString() : "")
            .Append(", payload=").Append(Payload() != null ? payload.ToString() : "")
            .Append(']')
            .ToString();
    }
}
