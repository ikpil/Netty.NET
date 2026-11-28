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
 * Contains a topic name and Qos Level.
 * This is part of the {@link MqttSubscribePayload}
 */
// CLR: the @Deprecated topicName() ("use topicFilter") names its replacement, so it is not ported; its Javadoc is
// recorded in docs/codec-mqtt-porting-manifest.json and the callers use TopicFilter().
public sealed class MqttTopicSubscription
{
    private string topicFilter;
    private readonly MqttSubscriptionOption option;

    public MqttTopicSubscription(string topicFilter, MqttQoS qualityOfService)
    {
        this.topicFilter = topicFilter;
        this.option = MqttSubscriptionOption.OnlyFromQos(qualityOfService);
    }

    public MqttTopicSubscription(string topicFilter, MqttSubscriptionOption option)
    {
        this.topicFilter = topicFilter;
        this.option = option;
    }

    public string TopicFilter()
    {
        return topicFilter;
    }

    /**
     * Rewrite topic filter.
     * <p>
     *
     * Many IoT devices do not support reconfiguration or upgrade, so it is hard to
     * change their subscribed topics. To resolve this issue, MQTT server may offer
     * topic rewrite capability.
     *
     * @param topicFilter Topic to rewrite to
     */
    public void SetTopicFilter(string topicFilter)
    {
        this.topicFilter = topicFilter;
    }

    public MqttQoS QualityOfService()
    {
        return option.Qos();
    }

    public MqttSubscriptionOption Option()
    {
        return option;
    }

    // CLR: Java string conversion writes null as "null" (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11).
    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
                .Append('[')
                .Append("topicFilter=").Append(topicFilter ?? "null")
                .Append(", option=").Append(this.option?.ToString() ?? "null")
                .Append(']')
                .ToString();
    }
}
