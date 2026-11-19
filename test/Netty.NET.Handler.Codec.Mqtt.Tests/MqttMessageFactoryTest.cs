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

using System.Collections.Generic;
using static Netty.NET.Handler.Codec.Mqtt.MqttQoS;
using static Netty.NET.Handler.Codec.Mqtt.Tests.MqttTestUtils;

#pragma warning disable xUnit2013 // assertEquals(0, size()) keeps the source assertion.

namespace Netty.NET.Handler.Codec.Mqtt.Tests;

// CLR: java.util.List/ArrayList is List<T>.
public class MqttMessageFactoryTest
{
    private const string SAMPLE_TOPIC = "a/b/c";
    private const int SAMPLE_MESSAGE_ID = 123;

    [Fact]
    public void CreateUnsubAckV3()
    {
        MqttFixedHeader fixedHeader =
                new MqttFixedHeader(MqttMessageType.UNSUBACK, false, MqttQoS.AT_MOST_ONCE, false, 0);
        MqttMessageIdVariableHeader variableHeader =
                MqttMessageIdVariableHeader.From(SAMPLE_MESSAGE_ID);

        MqttMessage unsuback = MqttMessageFactory.NewMessage(fixedHeader, variableHeader, null);

        Assert.Equal(MqttMessageType.UNSUBACK, unsuback.FixedHeader().MessageType());
        MqttMessageIdAndPropertiesVariableHeader actualVariableHeader =
                (MqttMessageIdAndPropertiesVariableHeader) unsuback.VariableHeader();
        Assert.Equal(SAMPLE_MESSAGE_ID, actualVariableHeader.MessageId());
        ValidateProperties(MqttProperties.NO_PROPERTIES, actualVariableHeader.Properties());
        MqttUnsubAckPayload actualPayload = (MqttUnsubAckPayload) unsuback.Payload();
        Assert.NotNull(actualPayload);
        Assert.Equal(0, actualPayload.UnsubscribeReasonCodes().Count);
    }

    [Fact]
    public void CreateUnsubAckV5()
    {
        MqttFixedHeader fixedHeader =
                new MqttFixedHeader(MqttMessageType.UNSUBACK, false, MqttQoS.AT_MOST_ONCE, false, 0);
        MqttProperties properties = new MqttProperties();
        string reasonString = "All right";
        properties.Add(new MqttProperties.StringProperty(
                MqttProperties.REASON_STRING,
                reasonString));
        MqttMessageIdAndPropertiesVariableHeader variableHeader =
                new MqttMessageIdAndPropertiesVariableHeader(SAMPLE_MESSAGE_ID, properties);
        MqttUnsubAckPayload payload = new MqttUnsubAckPayload((short) 0x80 /*unspecified error*/);

        MqttMessage unsuback = MqttMessageFactory.NewMessage(fixedHeader, variableHeader, payload);

        Assert.Equal(MqttMessageType.UNSUBACK, unsuback.FixedHeader().MessageType());
        MqttMessageIdAndPropertiesVariableHeader actualVariableHeader =
                (MqttMessageIdAndPropertiesVariableHeader) unsuback.VariableHeader();
        Assert.Equal(SAMPLE_MESSAGE_ID, actualVariableHeader.MessageId());
        ValidateProperties(properties, actualVariableHeader.Properties());
        MqttUnsubAckPayload actualPayload = (MqttUnsubAckPayload) unsuback.Payload();
        Assert.Equal(payload.UnsubscribeReasonCodes(), actualPayload.UnsubscribeReasonCodes());
    }

    [Fact]
    public void CreateSubscribeV3()
    {
        MqttFixedHeader fixedHeader = new MqttFixedHeader(MqttMessageType.SUBSCRIBE, false, AT_LEAST_ONCE, false, 0);

        MqttMessageIdVariableHeader variableHeader = MqttMessageIdVariableHeader.From(SAMPLE_MESSAGE_ID);
        List<MqttTopicSubscription> subscriptions = new List<MqttTopicSubscription>();
        subscriptions.Add(new MqttTopicSubscription(SAMPLE_TOPIC, MqttQoS.AT_MOST_ONCE));

        MqttSubscribePayload payload = new MqttSubscribePayload(subscriptions);

        MqttMessage subscribe = MqttMessageFactory.NewMessage(fixedHeader, variableHeader, payload);

        Assert.Equal(MqttMessageType.SUBSCRIBE, subscribe.FixedHeader().MessageType());
        MqttMessageIdAndPropertiesVariableHeader actualVariableHeader =
                (MqttMessageIdAndPropertiesVariableHeader) subscribe.VariableHeader();
        Assert.Equal(SAMPLE_MESSAGE_ID, actualVariableHeader.MessageId());
        ValidateProperties(MqttProperties.NO_PROPERTIES, actualVariableHeader.Properties());
        MqttSubscribePayload actualPayload = (MqttSubscribePayload) subscribe.Payload();
        ValidateSubscribePayload(payload, actualPayload);
    }

    [Fact]
    public void CreateSubscribeV5()
    {
        MqttFixedHeader fixedHeader = new MqttFixedHeader(MqttMessageType.SUBSCRIBE, false, AT_LEAST_ONCE, false, 0);

        MqttProperties properties = new MqttProperties();
        properties.Add(new MqttProperties.UserProperty("correlationId", "111222"));
        MqttMessageIdAndPropertiesVariableHeader variableHeader =
                new MqttMessageIdAndPropertiesVariableHeader(SAMPLE_MESSAGE_ID, properties);
        List<MqttTopicSubscription> subscriptions = new List<MqttTopicSubscription>();
        subscriptions.Add(new MqttTopicSubscription(SAMPLE_TOPIC, MqttQoS.AT_MOST_ONCE));

        MqttSubscribePayload payload = new MqttSubscribePayload(subscriptions);

        MqttMessage subscribe = MqttMessageFactory.NewMessage(fixedHeader, variableHeader, payload);

        Assert.Equal(MqttMessageType.SUBSCRIBE, subscribe.FixedHeader().MessageType());
        MqttMessageIdAndPropertiesVariableHeader actualVariableHeader =
                (MqttMessageIdAndPropertiesVariableHeader) subscribe.VariableHeader();
        Assert.Equal(SAMPLE_MESSAGE_ID, actualVariableHeader.MessageId());
        ValidateProperties(properties, actualVariableHeader.Properties());
        MqttSubscribePayload actualPayload = (MqttSubscribePayload) subscribe.Payload();
        ValidateSubscribePayload(payload, actualPayload);
    }

    [Fact]
    public void CreateUnsubscribeV3()
    {
        MqttFixedHeader fixedHeader = new MqttFixedHeader(MqttMessageType.UNSUBSCRIBE, false, AT_LEAST_ONCE, false, 0);

        MqttMessageIdVariableHeader variableHeader = MqttMessageIdVariableHeader.From(SAMPLE_MESSAGE_ID);

        List<string> topics = new List<string>();
        topics.Add(SAMPLE_TOPIC);
        MqttUnsubscribePayload payload = new MqttUnsubscribePayload(topics);

        MqttMessage unsubscribe = MqttMessageFactory.NewMessage(fixedHeader, variableHeader, payload);

        Assert.Equal(MqttMessageType.UNSUBSCRIBE, unsubscribe.FixedHeader().MessageType());
        MqttMessageIdAndPropertiesVariableHeader actualVariableHeader =
                (MqttMessageIdAndPropertiesVariableHeader) unsubscribe.VariableHeader();
        Assert.Equal(SAMPLE_MESSAGE_ID, actualVariableHeader.MessageId());
        ValidateProperties(MqttProperties.NO_PROPERTIES, actualVariableHeader.Properties());
        MqttUnsubscribePayload actualPayload = (MqttUnsubscribePayload) unsubscribe.Payload();
        ValidateUnsubscribePayload(payload, actualPayload);
    }

    [Fact]
    public void CreateUnsubscribeV5()
    {
        MqttFixedHeader fixedHeader = new MqttFixedHeader(MqttMessageType.UNSUBSCRIBE, false, AT_LEAST_ONCE, false, 0);

        MqttProperties properties = new MqttProperties();
        properties.Add(new MqttProperties.UserProperty("correlationId", "111222"));
        MqttMessageIdAndPropertiesVariableHeader variableHeader =
                new MqttMessageIdAndPropertiesVariableHeader(SAMPLE_MESSAGE_ID, properties);

        List<string> topics = new List<string>();
        topics.Add(SAMPLE_TOPIC);
        MqttUnsubscribePayload payload = new MqttUnsubscribePayload(topics);

        MqttMessage unsubscribe = MqttMessageFactory.NewMessage(fixedHeader, variableHeader, payload);

        Assert.Equal(MqttMessageType.UNSUBSCRIBE, unsubscribe.FixedHeader().MessageType());
        MqttMessageIdAndPropertiesVariableHeader actualVariableHeader =
                (MqttMessageIdAndPropertiesVariableHeader) unsubscribe.VariableHeader();
        Assert.Equal(SAMPLE_MESSAGE_ID, actualVariableHeader.MessageId());
        ValidateProperties(properties, actualVariableHeader.Properties());
        MqttUnsubscribePayload actualPayload = (MqttUnsubscribePayload) unsubscribe.Payload();
        ValidateUnsubscribePayload(payload, actualPayload);
    }
}
