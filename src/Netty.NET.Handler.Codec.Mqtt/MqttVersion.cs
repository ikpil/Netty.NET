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
 * Mqtt version specific constant values used by multiple classes in mqtt-codec.
 */
// CLR: an enum with fields and methods is a sealed class with static readonly constants; Name(), Values() and
// ToString() stand for the java.lang.Enum members (see MqttQoS). The protocol level stays a byte.
// CharsetUtil.UTF_8 is Encoding.UTF8.
public sealed class MqttVersion
{
    public static readonly MqttVersion MQTT_3_1 = new MqttVersion(nameof(MQTT_3_1), "MQIsdp", (byte) 3);
    public static readonly MqttVersion MQTT_3_1_1 = new MqttVersion(nameof(MQTT_3_1_1), "MQTT", (byte) 4);
    public static readonly MqttVersion MQTT_5 = new MqttVersion(nameof(MQTT_5), "MQTT", (byte) 5);

    private readonly string name;
    private readonly byte level;
    // CLR: the constant name that java.lang.Enum keeps for name() and toString(); 'name' is the protocol name field
    // of the source.
    private readonly string constantName;

    private MqttVersion(string constantName, string protocolName, byte protocolLevel)
    {
        this.constantName = constantName;
        name = ObjectUtil.CheckNotNull(protocolName, "protocolName");
        level = protocolLevel;
    }

    public string ProtocolName()
    {
        return name;
    }

    public byte[] ProtocolNameBytes()
    {
        return Encoding.UTF8.GetBytes(name);
    }

    public byte ProtocolLevel()
    {
        return level;
    }

    public static MqttVersion FromProtocolNameAndLevel(string protocolName, byte protocolLevel)
    {
        MqttVersion mv;

        switch (protocolLevel)
        {
        case 3:
            mv = MQTT_3_1;
            break;
        case 4:
            mv = MQTT_3_1_1;
            break;
        case 5:
            mv = MQTT_5;
            break;
        default:
            // CLR: Java string conversion renders a null reference as "null"
            // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11); C# concatenation renders it
            // as an empty string (https://learn.microsoft.com/dotnet/csharp/language-reference/operators/addition-operator).
            throw new MqttUnacceptableProtocolVersionException((protocolName ?? "null") + " is an unknown protocol name");
        }

        if (mv.name.Equals(protocolName))
        {
            return mv;
        }

        // CLR: the Java byte is signed (-128..127, https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1)
        // and the C# byte is not, so the message prints the level through sbyte to keep the Java text.
        throw new MqttUnacceptableProtocolVersionException((protocolName ?? "null") + " and " +
                ((sbyte) protocolLevel).ToString(CultureInfo.InvariantCulture) + " don't match");
    }

    public string Name()
    {
        return constantName;
    }

    public static MqttVersion[] Values()
    {
        return new[] { MQTT_3_1, MQTT_3_1_1, MQTT_5 };
    }

    public override string ToString()
    {
        return constantName;
    }
}
