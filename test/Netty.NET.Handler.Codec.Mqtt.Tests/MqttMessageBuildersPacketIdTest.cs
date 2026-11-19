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

namespace Netty.NET.Handler.Codec.Mqtt.Tests;

// CLR: @ParameterizedTest with @MethodSource("data") is [Theory] with [MemberData(nameof(Data))]; Integer is int.
// testUnsubAckMessageIdAsShort, testSubAckMessageIdAsShort and testPubAckMessageIdAsShort only exercise the @Deprecated
// packetId(short) overloads, which name packetId(int) as their replacement and are not ported (owner decision
// 2026-11-16); they are recorded as CLR-inapplicable in docs/codec-mqtt-porting-manifest.json, and the AsInt tests
// below cover packetId(int) with the same inputs.
public class MqttMessageBuildersPacketIdTest
{

    public static IEnumerable<object[]> Data()
    {
        // we take a subset of valid packetIds
        return new List<object[]>
        {
                new object[] { 0x0001 },
                new object[] { 0x000F },
                new object[] { 0x00FF },
                new object[] { 0x0FFF },
                new object[] { 0xFFFF },
        };
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestUnsubAckMessageIdAsInt(int id)
    {
        MqttUnsubAckMessage msg = MqttMessageBuilders.UnsubAck()
                .PacketId(id)
                .Build();

        Assert.Equal(
                id,
                msg.VariableHeader().MessageId()
        );
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestSubAckMessageIdAsInt(int id)
    {
        MqttSubAckMessage msg = MqttMessageBuilders.SubAck()
                .PacketId(id)
                .Build();

        Assert.Equal(
                id,
                msg.VariableHeader().MessageId()
        );
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestPubAckMessageIdAsInt(int id)
    {
        MqttMessage msg = MqttMessageBuilders.PubAck()
                .PacketId(id)
                .Build();

        Assert.Equal(
                id,
                ((MqttMessageIdVariableHeader) msg.VariableHeader()).MessageId()
        );
    }
}
