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
using System.Globalization;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Variable Header containing only Message Id
 * See <a href="https://public.dhe.ibm.com/software/dw/webservices/ws-mqtt/mqtt-v3r1.html#msg-id">MQTTV3.1/msg-id</a>
 */
public class MqttMessageIdVariableHeader
{
    private readonly int messageId;

    public static MqttMessageIdVariableHeader From(int messageId)
    {
      if (messageId < 1 || messageId > 0xffff)
      {
        throw new ArgumentException("messageId: " + messageId.ToString(CultureInfo.InvariantCulture) +
                " (expected: 1 ~ 65535)");
      }

      return new MqttMessageIdVariableHeader(messageId);
    }

    protected MqttMessageIdVariableHeader(int messageId)
    {
        this.messageId = messageId;
    }

    public int MessageId()
    {
        return messageId;
    }

    // CLR: the int uses the invariant culture (https://learn.microsoft.com/dotnet/api/system.int32.tostring).
    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
            .Append('[')
            .Append("messageId=").Append(messageId.ToString(CultureInfo.InvariantCulture))
            .Append(']')
            .ToString();
    }

    public MqttMessageIdAndPropertiesVariableHeader WithEmptyProperties()
    {
        return new MqttMessageIdAndPropertiesVariableHeader(messageId, MqttProperties.NO_PROPERTIES);
    }

    // CLR: Java package access is internal (the assembly), as for the package-level members of codec-base
    // (docs/codec-base-porting.md, Package access).
    internal virtual MqttMessageIdAndPropertiesVariableHeader WithDefaultEmptyProperties()
    {
        return WithEmptyProperties();
    }
}
