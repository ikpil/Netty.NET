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
using System.Globalization;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Return Code of {@link MqttConnAckMessage}
 */
// CLR: an enum with a field and methods is a sealed class with static readonly constants; Name(), Values() and
// ToString() stand for the java.lang.Enum members (see MqttQoS). The return codes stay bytes (owner decision for Java
// byte protocol values); the static block is the static constructor, and ArrayIndexOutOfBoundsException is the CLR
// IndexOutOfRangeException of an array access (https://learn.microsoft.com/dotnet/api/system.indexoutofrangeexception).
public sealed class MqttConnectReturnCode
{
    public static readonly MqttConnectReturnCode CONNECTION_ACCEPTED =
            new MqttConnectReturnCode(nameof(CONNECTION_ACCEPTED), (byte) 0x00);
    //MQTT 3 codes
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_UNACCEPTABLE_PROTOCOL_VERSION =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_UNACCEPTABLE_PROTOCOL_VERSION), (byte) 0X01);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_IDENTIFIER_REJECTED =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_IDENTIFIER_REJECTED), (byte) 0x02);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_SERVER_UNAVAILABLE =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_SERVER_UNAVAILABLE), (byte) 0x03);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_BAD_USER_NAME_OR_PASSWORD =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_BAD_USER_NAME_OR_PASSWORD), (byte) 0x04);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_NOT_AUTHORIZED =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_NOT_AUTHORIZED), (byte) 0x05);
    //MQTT 5 codes
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_UNSPECIFIED_ERROR =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_UNSPECIFIED_ERROR), (byte) 0x80);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_MALFORMED_PACKET =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_MALFORMED_PACKET), (byte) 0x81);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_PROTOCOL_ERROR =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_PROTOCOL_ERROR), (byte) 0x82);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_IMPLEMENTATION_SPECIFIC =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_IMPLEMENTATION_SPECIFIC), (byte) 0x83);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_UNSUPPORTED_PROTOCOL_VERSION =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_UNSUPPORTED_PROTOCOL_VERSION), (byte) 0x84);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_CLIENT_IDENTIFIER_NOT_VALID =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_CLIENT_IDENTIFIER_NOT_VALID), (byte) 0x85);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_BAD_USERNAME_OR_PASSWORD =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_BAD_USERNAME_OR_PASSWORD), (byte) 0x86);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_NOT_AUTHORIZED_5 =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_NOT_AUTHORIZED_5), (byte) 0x87);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_SERVER_UNAVAILABLE_5 =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_SERVER_UNAVAILABLE_5), (byte) 0x88);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_SERVER_BUSY =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_SERVER_BUSY), (byte) 0x89);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_BANNED =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_BANNED), (byte) 0x8A);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_BAD_AUTHENTICATION_METHOD =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_BAD_AUTHENTICATION_METHOD), (byte) 0x8C);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_TOPIC_NAME_INVALID =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_TOPIC_NAME_INVALID), (byte) 0x90);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_PACKET_TOO_LARGE =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_PACKET_TOO_LARGE), (byte) 0x95);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_QUOTA_EXCEEDED =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_QUOTA_EXCEEDED), (byte) 0x97);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_PAYLOAD_FORMAT_INVALID =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_PAYLOAD_FORMAT_INVALID), (byte) 0x99);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_RETAIN_NOT_SUPPORTED =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_RETAIN_NOT_SUPPORTED), (byte) 0x9A);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_QOS_NOT_SUPPORTED =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_QOS_NOT_SUPPORTED), (byte) 0x9B);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_USE_ANOTHER_SERVER =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_USE_ANOTHER_SERVER), (byte) 0x9C);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_SERVER_MOVED =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_SERVER_MOVED), (byte) 0x9D);
    public static readonly MqttConnectReturnCode CONNECTION_REFUSED_CONNECTION_RATE_EXCEEDED =
            new MqttConnectReturnCode(nameof(CONNECTION_REFUSED_CONNECTION_RATE_EXCEEDED), (byte) 0x9F);

    private static readonly MqttConnectReturnCode[] VALUES;

    static MqttConnectReturnCode()
    {
        MqttConnectReturnCode[] values = Values();
        VALUES = new MqttConnectReturnCode[160];

        foreach (MqttConnectReturnCode code in values)
        {
            int unsignedByte = code.byteValue & 0xFF;
            // Suppress a warning about out of bounds access since the enum contains only correct values
            VALUES[unsignedByte] = code;    //  [java/index-out-of-bounds]
        }
    }

    private readonly byte byteValue;
    private readonly string name;

    private MqttConnectReturnCode(string name, byte byteValue)
    {
        this.name = name;
        this.byteValue = byteValue;
    }

    public byte ByteValue()
    {
        return byteValue;
    }

    public static MqttConnectReturnCode ValueOf(byte b)
    {
        int unsignedByte = b & 0xFF;
        MqttConnectReturnCode mqttConnectReturnCode = null;

        try
        {
            mqttConnectReturnCode = VALUES[unsignedByte];
        }
        catch (IndexOutOfRangeException)
        {
            // no op
        }

        if (mqttConnectReturnCode == null)
        {
            throw new ArgumentException("unknown connect return code: " + unsignedByte.ToString(CultureInfo.InvariantCulture));
        }

        return mqttConnectReturnCode;
    }

    public string Name()
    {
        return name;
    }

    public static MqttConnectReturnCode[] Values()
    {
        return new[]
        {
            CONNECTION_ACCEPTED,
            CONNECTION_REFUSED_UNACCEPTABLE_PROTOCOL_VERSION,
            CONNECTION_REFUSED_IDENTIFIER_REJECTED,
            CONNECTION_REFUSED_SERVER_UNAVAILABLE,
            CONNECTION_REFUSED_BAD_USER_NAME_OR_PASSWORD,
            CONNECTION_REFUSED_NOT_AUTHORIZED,
            CONNECTION_REFUSED_UNSPECIFIED_ERROR,
            CONNECTION_REFUSED_MALFORMED_PACKET,
            CONNECTION_REFUSED_PROTOCOL_ERROR,
            CONNECTION_REFUSED_IMPLEMENTATION_SPECIFIC,
            CONNECTION_REFUSED_UNSUPPORTED_PROTOCOL_VERSION,
            CONNECTION_REFUSED_CLIENT_IDENTIFIER_NOT_VALID,
            CONNECTION_REFUSED_BAD_USERNAME_OR_PASSWORD,
            CONNECTION_REFUSED_NOT_AUTHORIZED_5,
            CONNECTION_REFUSED_SERVER_UNAVAILABLE_5,
            CONNECTION_REFUSED_SERVER_BUSY,
            CONNECTION_REFUSED_BANNED,
            CONNECTION_REFUSED_BAD_AUTHENTICATION_METHOD,
            CONNECTION_REFUSED_TOPIC_NAME_INVALID,
            CONNECTION_REFUSED_PACKET_TOO_LARGE,
            CONNECTION_REFUSED_QUOTA_EXCEEDED,
            CONNECTION_REFUSED_PAYLOAD_FORMAT_INVALID,
            CONNECTION_REFUSED_RETAIN_NOT_SUPPORTED,
            CONNECTION_REFUSED_QOS_NOT_SUPPORTED,
            CONNECTION_REFUSED_USE_ANOTHER_SERVER,
            CONNECTION_REFUSED_SERVER_MOVED,
            CONNECTION_REFUSED_CONNECTION_RATE_EXCEEDED,
        };
    }

    public override string ToString()
    {
        return name;
    }
}
