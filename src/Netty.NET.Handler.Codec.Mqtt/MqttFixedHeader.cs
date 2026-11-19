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

using System.Globalization;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * See <a href="https://public.dhe.ibm.com/software/dw/webservices/ws-mqtt/mqtt-v3r1.html#fixed-header">
 *     MQTTV3.1/fixed-header</a>
 */
public sealed class MqttFixedHeader
{
    private readonly MqttMessageType messageType;
    private readonly bool isDup;
    private readonly MqttQoS qosLevel;
    private readonly bool isRetain;
    private readonly int remainingLength;

    public MqttFixedHeader(
            MqttMessageType messageType,
            bool isDup,
            MqttQoS qosLevel,
            bool isRetain,
            int remainingLength)
    {
        this.messageType = ObjectUtil.CheckNotNull(messageType, "messageType");
        this.isDup = isDup;
        this.qosLevel = ObjectUtil.CheckNotNull(qosLevel, "qosLevel");
        this.isRetain = isRetain;
        this.remainingLength = remainingLength;
    }

    public MqttMessageType MessageType()
    {
        return messageType;
    }

    public bool IsDup()
    {
        return isDup;
    }

    public MqttQoS QosLevel()
    {
        return qosLevel;
    }

    public bool IsRetain()
    {
        return isRetain;
    }

    public int RemainingLength()
    {
        return remainingLength;
    }

    // CLR: StringBuilder.append(boolean) writes "true"/"false" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Boolean.html)
    // while Boolean.ToString() returns "True"/"False" (https://learn.microsoft.com/dotnet/api/system.boolean.tostring),
    // and the int uses the invariant culture, so the Java text is kept explicitly.
    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
            .Append('[')
            .Append("messageType=").Append(messageType)
            .Append(", isDup=").Append(isDup ? "true" : "false")
            .Append(", qosLevel=").Append(qosLevel)
            .Append(", isRetain=").Append(isRetain ? "true" : "false")
            .Append(", remainingLength=").Append(remainingLength.ToString(CultureInfo.InvariantCulture))
            .Append(']')
            .ToString();
    }
}
