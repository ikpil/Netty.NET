/*
 * Copyright 2017 The Netty Project
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
using System.Text;

namespace Netty.NET.Handler.Codec.Mqtt.Tests;

// CLR: the @Deprecated willMessage()/password() accessors and ConnectBuilder.willMessage(String)/password(String)
// name their replacements and are not ported. The tests keep every assertion on the replacements
// (willMessageInBytes(), passwordInBytes(), willMessage(byte[]), password(byte[])); each assertion on a deprecated
// String accessor is replaced by its byte[] counterpart, which the test already asserts. CharsetUtil.UTF_8 is
// Encoding.UTF8 and Collections.emptyList() is an empty List<T>.
public class MqttConnectPayloadTest
{

    [Fact]
    public void TestNullWillMessage()
    {
        string clientIdentifier = "clientIdentifier";
        string willTopic = "willTopic";
        byte[] willMessage = null;
        string userName = "userName";
        byte[] password = Encoding.UTF8.GetBytes("password");
        MqttConnectPayload mqttConnectPayload = new MqttConnectPayload(clientIdentifier,
                MqttProperties.NO_PROPERTIES,
                willTopic,
                willMessage,
                userName,
                password);

        Assert.Null(mqttConnectPayload.WillMessageInBytes());
        // CLR: assertNull(willMessage()) of the not-ported deprecated accessor is covered by WillMessageInBytes().
    }

    [Fact]
    public void TestNullPassword()
    {
        string clientIdentifier = "clientIdentifier";
        string willTopic = "willTopic";
        byte[] willMessage = Encoding.UTF8.GetBytes("willMessage");
        string userName = "userName";
        byte[] password = null;
        MqttConnectPayload mqttConnectPayload = new MqttConnectPayload(clientIdentifier,
                MqttProperties.NO_PROPERTIES,
                willTopic,
                willMessage,
                userName,
                password);

        Assert.Null(mqttConnectPayload.PasswordInBytes());
        // CLR: assertNull(password()) of the not-ported deprecated accessor is covered by PasswordInBytes().
    }

    [Fact]
    public void TestBuilderNullPassword()
    {
        MqttMessageBuilders.ConnectBuilder builder = new MqttMessageBuilders.ConnectBuilder();
        // CLR: password((String) null) of the not-ported deprecated overload is its replacement password((byte[]) null).
        builder.Password((byte[]) null);

        MqttConnectPayload mqttConnectPayload = builder.Build().Payload();

        Assert.Null(mqttConnectPayload.PasswordInBytes());

        builder = new MqttMessageBuilders.ConnectBuilder();
        builder.Password((byte[]) null);

        mqttConnectPayload = builder.Build().Payload();

        Assert.Null(mqttConnectPayload.PasswordInBytes());
    }

    [Fact]
    public void TestBuilderNullWillMessage()
    {
        MqttMessageBuilders.ConnectBuilder builder = new MqttMessageBuilders.ConnectBuilder();
        // CLR: willMessage((String) null) of the not-ported deprecated overload is its replacement
        // willMessage((byte[]) null).
        builder.WillMessage((byte[]) null);

        MqttConnectPayload mqttConnectPayload = builder.Build().Payload();

        Assert.Null(mqttConnectPayload.WillMessageInBytes());

        builder = new MqttMessageBuilders.ConnectBuilder();
        builder.WillMessage((byte[]) null);

        mqttConnectPayload = builder.Build().Payload();

        Assert.Null(mqttConnectPayload.WillMessageInBytes());
    }

    /* See https://github.com/netty/netty/pull/9202 */
    [Fact]
    public void TestEmptyTopicsToString()
    {
        new MqttSubscribePayload(new List<MqttTopicSubscription>()).ToString();
        new MqttUnsubscribePayload(new List<string>()).ToString();
    }
}
