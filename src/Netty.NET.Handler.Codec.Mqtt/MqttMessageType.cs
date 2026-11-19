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
using System.Diagnostics;
using System.Globalization;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * MQTT Message Types.
 */
// CLR: an enum with a field and methods is a sealed class with static readonly constants; Name(), Values() and
// ToString() stand for the java.lang.Enum members (see MqttQoS). The static block is the static constructor, which
// runs after the field initializers in textual order. AssertionError, a java.lang.Error for a broken invariant, is
// UnreachableException, "thrown when the program executes an instruction that was thought to be unreachable"
// (https://learn.microsoft.com/dotnet/api/system.diagnostics.unreachableexception), as codec-base maps the Error of
// LengthFieldPrepender.
public sealed class MqttMessageType
{
    public static readonly MqttMessageType CONNECT = new MqttMessageType(nameof(CONNECT), 1);
    public static readonly MqttMessageType CONNACK = new MqttMessageType(nameof(CONNACK), 2);
    public static readonly MqttMessageType PUBLISH = new MqttMessageType(nameof(PUBLISH), 3);
    public static readonly MqttMessageType PUBACK = new MqttMessageType(nameof(PUBACK), 4);
    public static readonly MqttMessageType PUBREC = new MqttMessageType(nameof(PUBREC), 5);
    public static readonly MqttMessageType PUBREL = new MqttMessageType(nameof(PUBREL), 6);
    public static readonly MqttMessageType PUBCOMP = new MqttMessageType(nameof(PUBCOMP), 7);
    public static readonly MqttMessageType SUBSCRIBE = new MqttMessageType(nameof(SUBSCRIBE), 8);
    public static readonly MqttMessageType SUBACK = new MqttMessageType(nameof(SUBACK), 9);
    public static readonly MqttMessageType UNSUBSCRIBE = new MqttMessageType(nameof(UNSUBSCRIBE), 10);
    public static readonly MqttMessageType UNSUBACK = new MqttMessageType(nameof(UNSUBACK), 11);
    public static readonly MqttMessageType PINGREQ = new MqttMessageType(nameof(PINGREQ), 12);
    public static readonly MqttMessageType PINGRESP = new MqttMessageType(nameof(PINGRESP), 13);
    public static readonly MqttMessageType DISCONNECT = new MqttMessageType(nameof(DISCONNECT), 14);
    public static readonly MqttMessageType AUTH = new MqttMessageType(nameof(AUTH), 15);

    private static readonly MqttMessageType[] VALUES;

    static MqttMessageType()
    {
        // this prevent values to be assigned with the wrong order
        // and ensure valueOf to work fine
        MqttMessageType[] values = Values();
        VALUES = new MqttMessageType[values.Length + 1];
        foreach (MqttMessageType mqttMessageType in values)
        {
            int value = mqttMessageType.value;
            if (VALUES[value] != null)
            {
                throw new UnreachableException("value already in use: " + value.ToString(CultureInfo.InvariantCulture));
            }
            VALUES[value] = mqttMessageType;
        }
    }

    private readonly int value;
    private readonly string name;

    private MqttMessageType(string name, int value)
    {
        this.name = name;
        this.value = value;
    }

    public int Value()
    {
        return value;
    }

    public static MqttMessageType ValueOf(int type)
    {
        if (type <= 0 || type >= VALUES.Length)
        {
            throw new ArgumentException("unknown message type: " + type.ToString(CultureInfo.InvariantCulture));
        }
        return VALUES[type];
    }

    public string Name()
    {
        return name;
    }

    public static MqttMessageType[] Values()
    {
        return new[]
        {
            CONNECT, CONNACK, PUBLISH, PUBACK, PUBREC, PUBREL, PUBCOMP, SUBSCRIBE, SUBACK, UNSUBSCRIBE, UNSUBACK,
            PINGREQ, PINGRESP, DISCONNECT, AUTH,
        };
    }

    public override string ToString()
    {
        return name;
    }
}
