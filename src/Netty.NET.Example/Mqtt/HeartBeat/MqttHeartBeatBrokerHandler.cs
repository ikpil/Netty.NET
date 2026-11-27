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
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Mqtt;
using Netty.NET.Handler.Timeout;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Mqtt.HeartBeat;

// CLR: @Sharable is the [Sharable] attribute. MqttMessageType is a sealed class with static readonly constants (codec-mqtt),
// so the switch over messageType() matches each constant with a case guard, as codec-mqtt's MqttEncoder does.
// ctx.writeAndFlush/close return Tasks that are discarded as Java discards the futures, and cause.printStackTrace()
// writes Exception.ToString() to Console.Error.
[Sharable]
public sealed class MqttHeartBeatBrokerHandler : ChannelInboundHandlerAdapter
{

    public static readonly MqttHeartBeatBrokerHandler INSTANCE = new MqttHeartBeatBrokerHandler();

    private MqttHeartBeatBrokerHandler()
    {
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        MqttMessage mqttMessage = (MqttMessage) msg;
        Console.WriteLine("Received MQTT message: " + mqttMessage);
        MqttMessageType messageType = mqttMessage.FixedHeader().MessageType();
        switch (messageType)
        {
        case var _ when messageType == MqttMessageType.CONNECT:
            MqttFixedHeader connackFixedHeader =
                    new MqttFixedHeader(MqttMessageType.CONNACK, false, MqttQoS.AT_MOST_ONCE, false, 0);
            MqttConnAckVariableHeader mqttConnAckVariableHeader =
                    new MqttConnAckVariableHeader(MqttConnectReturnCode.CONNECTION_ACCEPTED, false);
            MqttConnAckMessage connack = new MqttConnAckMessage(connackFixedHeader, mqttConnAckVariableHeader);
            _ = ctx.WriteAndFlushAsync(connack);
            break;
        case var _ when messageType == MqttMessageType.PINGREQ:
            MqttFixedHeader pingreqFixedHeader = new MqttFixedHeader(MqttMessageType.PINGRESP, false,
                                                                     MqttQoS.AT_MOST_ONCE, false, 0);
            MqttMessage pingResp = new MqttMessage(pingreqFixedHeader);
            _ = ctx.WriteAndFlushAsync(pingResp);
            break;
        case var _ when messageType == MqttMessageType.DISCONNECT:
            _ = ctx.CloseAsync();
            break;
        default:
            Console.WriteLine("Unexpected message type: " + mqttMessage.FixedHeader().MessageType());
            ReferenceCountUtil.Release(msg);
            _ = ctx.CloseAsync();
            break;
        }
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        Console.WriteLine("Channel heartBeat lost");
        if (evt is IdleStateEvent && IdleState.READER_IDLE == ((IdleStateEvent) evt).State())
        {
            _ = ctx.CloseAsync();
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
