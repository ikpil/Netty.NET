/*
 * Copyright 2023 The Netty Project
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

using static Netty.NET.Handler.Codec.Mqtt.MqttReasonCodes;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Mqtt.Tests;

// CLR: the package-private test class is public because xUnit discovers public test classes; values()/valueOf(byte)
// are Values()/ValueOf(byte) of the enum classes.
public class MqttReasonCodesTest
{

    [Fact]
    public void GivenADisconnectReasonCodeTheCorrectEnumerationValueIsReturned()
    {
        Assert.Equal(Disconnect.WILDCARD_SUBSCRIPTIONS_NOT_SUPPORTED, Disconnect.ValueOf((byte) 0xA2),
                "0xA2 must match 'wildcard subscriptions not supported'");
    }

    [Fact]
    public void TestDisconnectReasonCodesCorrectlyMapToConstant()
    {
        foreach (Disconnect reasonCode in Disconnect.Values())
        {
            Assert.Equal(reasonCode, Disconnect.ValueOf((byte) (reasonCode.ByteValue() & 0xFF)),
                    "Disconnect hex doesn't match the proper constant");
        }
    }

    [Fact]
    public void TestAuthReasonCodesCorrectlyMapToConstant()
    {
        foreach (Auth reasonCode in Auth.Values())
        {
            Assert.Equal(reasonCode, Auth.ValueOf((byte) (reasonCode.ByteValue() & 0xFF)),
                    "Auth hex doesn't match the proper constant");
        }
    }

    [Fact]
    public void TestPubAckReasonCodesCorrectlyMapToConstant()
    {
        foreach (PubAck reasonCode in PubAck.Values())
        {
            Assert.Equal(reasonCode, PubAck.ValueOf((byte) (reasonCode.ByteValue() & 0xFF)),
                    "PubAck hex doesn't match the proper constant");
        }
    }

    [Fact]
    public void TestPubRecReasonCodesCorrectlyMapToConstant()
    {
        foreach (PubRec reasonCode in PubRec.Values())
        {
            Assert.Equal(reasonCode, PubRec.ValueOf((byte) (reasonCode.ByteValue() & 0xFF)),
                    "PubRec hex doesn't match the proper constant");
        }
    }

    [Fact]
    public void TestPubRelReasonCodesCorrectlyMapToConstant()
    {
        foreach (PubRel reasonCode in PubRel.Values())
        {
            Assert.Equal(reasonCode, PubRel.ValueOf((byte) (reasonCode.ByteValue() & 0xFF)),
                    "PubRel hex doesn't match the proper constant");
        }
    }

    [Fact]
    public void TestPubCompReasonCodesCorrectlyMapToConstant()
    {
        foreach (PubComp reasonCode in PubComp.Values())
        {
            Assert.Equal(reasonCode, PubComp.ValueOf((byte) (reasonCode.ByteValue() & 0xFF)),
                    "PubComp hex doesn't match the proper constant");
        }
    }

    [Fact]
    public void TestSubAckReasonCodesCorrectlyMapToConstant()
    {
        foreach (SubAck reasonCode in SubAck.Values())
        {
            Assert.Equal(reasonCode, SubAck.ValueOf((byte) (reasonCode.ByteValue() & 0xFF)),
                    "SubAck hex doesn't match the proper constant");
        }
    }

    [Fact]
    public void TestUnsubAckReasonCodesCorrectlyMapToConstant()
    {
        foreach (UnsubAck reasonCode in UnsubAck.Values())
        {
            Assert.Equal(reasonCode, UnsubAck.ValueOf((byte) (reasonCode.ByteValue() & 0xFF)),
                    "UnsubAck hex doesn't match the proper constant");
        }
    }
}
