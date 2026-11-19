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

namespace Netty.NET.Handler.Codec.Mqtt.Tests;

// CLR: the anonymous PropertiesInitializer is the named nested class ConnAckPropertiesInitializer implementing
// IPropertiesInitializer<T>; the raw MqttProperty.value() returns object, as the source's raw type does.
public class MqttMessageBuildersTest
{

    [Fact]
    public void TestConnAckWithProperties()
    {
        MqttConnAckMessage ackMsg = MqttMessageBuilders.ConnAck()
                .Properties(new ConnAckPropertiesInitializer()).Build();

        string clientId = (string) ackMsg.VariableHeader()
                .Properties()
                .GetProperty(MqttProperties.ASSIGNED_CLIENT_IDENTIFIER)
                .Value();

        Assert.Equal("client1234", clientId);
    }

    private sealed class ConnAckPropertiesInitializer
        : MqttMessageBuilders.IPropertiesInitializer<MqttMessageBuilders.ConnAckPropertiesBuilder>
    {
        public void Apply(MqttMessageBuilders.ConnAckPropertiesBuilder builder)
        {
            builder.AssignedClientId("client1234");
            builder.UserProperty("custom_property", "value");
        }
    }

}
