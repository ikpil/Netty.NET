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
 * Variable Header of the {@link MqttPublishMessage}
 */
// CLR: the @Deprecated messageId() ("Use packetId() instead") is not ported (owner decision 2026-11-16 for deprecated
// members whose replacement is named); its Javadoc is recorded in docs/codec-mqtt-porting-manifest.json.
public sealed class MqttPublishVariableHeader
{
    private readonly string topicName;
    private readonly int packetId;
    private readonly MqttProperties properties;

    public MqttPublishVariableHeader(string topicName, int packetId)
        : this(topicName, packetId, MqttProperties.NO_PROPERTIES)
    {
    }

    public MqttPublishVariableHeader(string topicName, int packetId, MqttProperties properties)
    {
        this.topicName = topicName;
        this.packetId = packetId;
        this.properties = MqttProperties.WithEmptyDefaults(properties);
    }

    public string TopicName()
    {
        return topicName;
    }

    public int PacketId()
    {
        return packetId;
    }

    public MqttProperties Properties()
    {
        return properties;
    }

    // CLR: Java string conversion writes null as "null" and an int without culture
    // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11); kept explicitly.
    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
            .Append('[')
            .Append("topicName=").Append(topicName ?? "null")
            .Append(", packetId=").Append(packetId.ToString(CultureInfo.InvariantCulture))
            .Append(']')
            .ToString();
    }
}
