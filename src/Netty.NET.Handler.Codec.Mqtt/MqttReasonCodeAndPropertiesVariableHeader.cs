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

using System.Globalization;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Variable Header for AUTH and DISCONNECT messages represented by {@link MqttMessage}
 */
public sealed class MqttReasonCodeAndPropertiesVariableHeader
{
    private readonly byte reasonCode;
    private readonly MqttProperties properties;

    public const byte REASON_CODE_OK = 0;

    public MqttReasonCodeAndPropertiesVariableHeader(byte reasonCode,
                                                     MqttProperties properties)
    {
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

    // CLR: StringBuilder.append(byte) prints the signed Java byte (-128..127,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1); the C# byte is unsigned, so it is printed
    // through sbyte with the invariant culture to keep the Java text (0x96 is "-106").
    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
            .Append('[')
            .Append("reasonCode=").Append(((sbyte) reasonCode).ToString(CultureInfo.InvariantCulture))
            .Append(", properties=").Append(properties)
            .Append(']')
            .ToString();
    }
}
