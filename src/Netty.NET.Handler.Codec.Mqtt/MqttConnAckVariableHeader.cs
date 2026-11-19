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
 * Variable header of {@link MqttConnectMessage}
 */
public sealed class MqttConnAckVariableHeader
{
    private readonly MqttConnectReturnCode connectReturnCode;

    private readonly bool sessionPresent;

    private readonly MqttProperties properties;

    public MqttConnAckVariableHeader(MqttConnectReturnCode connectReturnCode, bool sessionPresent)
        : this(connectReturnCode, sessionPresent, MqttProperties.NO_PROPERTIES)
    {
    }

    public MqttConnAckVariableHeader(MqttConnectReturnCode connectReturnCode, bool sessionPresent,
                                     MqttProperties properties)
    {
        this.connectReturnCode = connectReturnCode;
        this.sessionPresent = sessionPresent;
        this.properties = MqttProperties.WithEmptyDefaults(properties);
    }

    public MqttConnectReturnCode ConnectReturnCode()
    {
        return connectReturnCode;
    }

    public bool IsSessionPresent()
    {
        return sessionPresent;
    }

    public MqttProperties Properties()
    {
        return properties;
    }

    // CLR: Java string conversion writes a null reference as "null" and a boolean as "true"/"false"
    // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11); StringBuilder.Append writes nothing for
    // null and Boolean.ToString() returns "True"/"False" (https://learn.microsoft.com/dotnet/api/system.boolean.tostring),
    // so the Java text is kept explicitly.
    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
            .Append('[')
            .Append("connectReturnCode=").Append(connectReturnCode?.ToString() ?? "null")
            .Append(", sessionPresent=").Append(sessionPresent ? "true" : "false")
            .Append(']')
            .ToString();
    }
}
