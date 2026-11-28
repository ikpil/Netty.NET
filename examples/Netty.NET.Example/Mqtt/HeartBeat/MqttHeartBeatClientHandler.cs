/*
 * Copyright 2019 The Netty Project
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
using System.Text;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Mqtt;
using Netty.NET.Handler.Timeout;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Mqtt.HeartBeat;

// CLR: password.getBytes() encodes with the platform default charset, UTF-8 since JDK 18 (JEP 400), so it is
// Encoding.UTF8.GetBytes. ctx.writeAndFlush/close return Tasks that are discarded as Java discards the futures,
// super.userEventTriggered is base.UserEventTriggered, and cause.printStackTrace() writes Exception.ToString() to
// Console.Error.
public class MqttHeartBeatClientHandler : ChannelInboundHandlerAdapter
{

    private const string PROTOCOL_NAME_MQTT_3_1_1 = "MQTT";
    private const int PROTOCOL_VERSION_MQTT_3_1_1 = 4;

    private readonly string clientId;
    private readonly string userName;
    private readonly byte[] password;

    public MqttHeartBeatClientHandler(string clientId, string userName, string password)
    {
        this.clientId = clientId;
        this.userName = userName;
        this.password = Encoding.UTF8.GetBytes(password);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        // discard all messages
        ReferenceCountUtil.Release(msg);
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        MqttFixedHeader connectFixedHeader =
                new MqttFixedHeader(MqttMessageType.CONNECT, false, MqttQoS.AT_MOST_ONCE, false, 0);
        MqttConnectVariableHeader connectVariableHeader =
                new MqttConnectVariableHeader(PROTOCOL_NAME_MQTT_3_1_1, PROTOCOL_VERSION_MQTT_3_1_1, true, true, false,
                                              0, false, false, 20, MqttProperties.NO_PROPERTIES);
        MqttConnectPayload connectPayload = new MqttConnectPayload(clientId,
                MqttProperties.NO_PROPERTIES,
                null,
                null,
                userName,
                password);
        MqttConnectMessage connectMessage =
                new MqttConnectMessage(connectFixedHeader, connectVariableHeader, connectPayload);
        _ = ctx.WriteAndFlushAsync(connectMessage);
        Console.WriteLine("Sent CONNECT");
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is IdleStateEvent)
        {
            MqttFixedHeader pingreqFixedHeader =
                    new MqttFixedHeader(MqttMessageType.PINGREQ, false, MqttQoS.AT_MOST_ONCE, false, 0);
            MqttMessage pingreqMessage = new MqttMessage(pingreqFixedHeader);
            _ = ctx.WriteAndFlushAsync(pingreqMessage);
            Console.WriteLine("Sent PINGREQ");
        }
        else
        {
            base.UserEventTriggered(ctx, evt);
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
