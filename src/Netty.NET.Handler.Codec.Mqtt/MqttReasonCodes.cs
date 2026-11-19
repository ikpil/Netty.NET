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

using System;
using System.Globalization;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Provides a set of enumeration that exposes standard MQTT 5 reason codes used by various messages.
 * */
// CLR: each nested enum with a field and methods is a sealed class with static readonly constants; Name(), Values()
// and ToString() stand for the java.lang.Enum members (see MqttQoS), and each static block is a static constructor.
// The reason codes stay bytes (owner decision for Java byte protocol values). ArrayIndexOutOfBoundsException is
// IndexOutOfRangeException (https://learn.microsoft.com/dotnet/api/system.indexoutofrangeexception).
public sealed class MqttReasonCodes
{

    private MqttReasonCodes()
    {
    }

    /**
     * @return the corresponding enum value to the hex value.
     * */
    // CLR: the message prints the Java byte, which is signed (-128..127,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1); the C# byte is unsigned, so it is
    // printed through sbyte with the invariant culture to keep the Java text.
    private static E ValueOfHelper<E>(byte b, E[] values)
    {
        try
        {
            return values[b & 0xFF];
        }
        catch (IndexOutOfRangeException)
        {
            throw new ArgumentException("unknown reason code: " + ((sbyte) b).ToString(CultureInfo.InvariantCulture));
        }
    }


    /**
     * Reason codes for MQTT Disconnect message.
     */
    public sealed class Disconnect
    {
        public static readonly Disconnect NORMAL_DISCONNECT = new Disconnect(nameof(NORMAL_DISCONNECT), (byte) 0x00); //sent by: client or server
        public static readonly Disconnect DISCONNECT_WITH_WILL_MESSAGE = new Disconnect(nameof(DISCONNECT_WITH_WILL_MESSAGE), (byte) 0x04); //sent by: client
        public static readonly Disconnect UNSPECIFIED_ERROR = new Disconnect(nameof(UNSPECIFIED_ERROR), (byte) 0x80); //sent by: client or server
        public static readonly Disconnect MALFORMED_PACKET = new Disconnect(nameof(MALFORMED_PACKET), (byte) 0x81); //sent by: client or server
        public static readonly Disconnect PROTOCOL_ERROR = new Disconnect(nameof(PROTOCOL_ERROR), (byte) 0x82); //sent by: client or server
        public static readonly Disconnect IMPLEMENTATION_SPECIFIC_ERROR = new Disconnect(nameof(IMPLEMENTATION_SPECIFIC_ERROR), (byte) 0x83); //sent by: client or server
        public static readonly Disconnect NOT_AUTHORIZED = new Disconnect(nameof(NOT_AUTHORIZED), (byte) 0x87); //sent by: server
        public static readonly Disconnect SERVER_BUSY = new Disconnect(nameof(SERVER_BUSY), (byte) 0x89); //sent by: server
        public static readonly Disconnect SERVER_SHUTTING_DOWN = new Disconnect(nameof(SERVER_SHUTTING_DOWN), (byte) 0x8B); //sent by: server
        public static readonly Disconnect KEEP_ALIVE_TIMEOUT = new Disconnect(nameof(KEEP_ALIVE_TIMEOUT), (byte) 0x8D); //sent by: Server
        public static readonly Disconnect SESSION_TAKEN_OVER = new Disconnect(nameof(SESSION_TAKEN_OVER), (byte) 0x8E); //sent by: Server
        public static readonly Disconnect TOPIC_FILTER_INVALID = new Disconnect(nameof(TOPIC_FILTER_INVALID), (byte) 0x8F); //sent by: Server
        public static readonly Disconnect TOPIC_NAME_INVALID = new Disconnect(nameof(TOPIC_NAME_INVALID), (byte) 0x90); //sent by: Client or Server
        public static readonly Disconnect RECEIVE_MAXIMUM_EXCEEDED = new Disconnect(nameof(RECEIVE_MAXIMUM_EXCEEDED), (byte) 0x93); //sent by: Client or Server
        public static readonly Disconnect TOPIC_ALIAS_INVALID = new Disconnect(nameof(TOPIC_ALIAS_INVALID), (byte) 0x94); //sent by: Client or Server
        public static readonly Disconnect PACKET_TOO_LARGE = new Disconnect(nameof(PACKET_TOO_LARGE), (byte) 0x95); //sent by: Client or Server
        public static readonly Disconnect MESSAGE_RATE_TOO_HIGH = new Disconnect(nameof(MESSAGE_RATE_TOO_HIGH), (byte) 0x96); //sent by: Client or Server
        public static readonly Disconnect QUOTA_EXCEEDED = new Disconnect(nameof(QUOTA_EXCEEDED), (byte) 0x97); //sent by: Client or Server
        public static readonly Disconnect ADMINISTRATIVE_ACTION = new Disconnect(nameof(ADMINISTRATIVE_ACTION), (byte) 0x98); //sent by: Client or Server
        public static readonly Disconnect PAYLOAD_FORMAT_INVALID = new Disconnect(nameof(PAYLOAD_FORMAT_INVALID), (byte) 0x99); //sent by: Client or Server
        public static readonly Disconnect RETAIN_NOT_SUPPORTED = new Disconnect(nameof(RETAIN_NOT_SUPPORTED), (byte) 0x9A); //sent by: Server
        public static readonly Disconnect QOS_NOT_SUPPORTED = new Disconnect(nameof(QOS_NOT_SUPPORTED), (byte) 0x9B); //sent by: Server
        public static readonly Disconnect USE_ANOTHER_SERVER = new Disconnect(nameof(USE_ANOTHER_SERVER), (byte) 0x9C); //sent by: Server
        public static readonly Disconnect SERVER_MOVED = new Disconnect(nameof(SERVER_MOVED), (byte) 0x9D); //sent by: Server
        public static readonly Disconnect SHARED_SUBSCRIPTIONS_NOT_SUPPORTED = new Disconnect(nameof(SHARED_SUBSCRIPTIONS_NOT_SUPPORTED), (byte) 0x9E); //sent by: Server
        public static readonly Disconnect CONNECTION_RATE_EXCEEDED = new Disconnect(nameof(CONNECTION_RATE_EXCEEDED), (byte) 0x9F); //sent by: Server
        public static readonly Disconnect MAXIMUM_CONNECT_TIME = new Disconnect(nameof(MAXIMUM_CONNECT_TIME), (byte) 0xA0); //sent by: Server
        public static readonly Disconnect SUBSCRIPTION_IDENTIFIERS_NOT_SUPPORTED = new Disconnect(nameof(SUBSCRIPTION_IDENTIFIERS_NOT_SUPPORTED), (byte) 0xA1); //sent by: Server
        public static readonly Disconnect WILDCARD_SUBSCRIPTIONS_NOT_SUPPORTED = new Disconnect(nameof(WILDCARD_SUBSCRIPTIONS_NOT_SUPPORTED), (byte) 0xA2); //sent by: Server

        // CLR: Java protected in an enum, which cannot be subclassed, admits the package only; a protected member of a
        // sealed C# class draws warning CS0628, so it is internal (docs/codec-base-porting.md, Package access).
        internal static readonly Disconnect[] VALUES;

        static Disconnect()
        {
            Disconnect[] values = Values();
            VALUES = new Disconnect[163];
            foreach (Disconnect code in values)
            {
                int unsignedByte = code.byteValue & 0xFF;
                // Suppress a warning about out of bounds access since the enum contains only correct values
                VALUES[unsignedByte] = code;    //  [java/index-out-of-bounds]
            }
        }

        private readonly byte byteValue;
        private readonly string name;

        private Disconnect(string name, byte byteValue)
        {
            this.name = name;
            this.byteValue = byteValue;
        }

        /**
         * @return the value number corresponding to the constant.
         * */
        public byte ByteValue()
        {
            return byteValue;
        }

        /**
         * @param b the number to decode.
         * @return the enum value corresponding to the number.
         * */
        public static Disconnect ValueOf(byte b)
        {
            return ValueOfHelper(b, VALUES);
        }

        public string Name()
        {
            return name;
        }

        public static Disconnect[] Values()
        {
            return new[]
            {
                NORMAL_DISCONNECT,
                DISCONNECT_WITH_WILL_MESSAGE,
                UNSPECIFIED_ERROR,
                MALFORMED_PACKET,
                PROTOCOL_ERROR,
                IMPLEMENTATION_SPECIFIC_ERROR,
                NOT_AUTHORIZED,
                SERVER_BUSY,
                SERVER_SHUTTING_DOWN,
                KEEP_ALIVE_TIMEOUT,
                SESSION_TAKEN_OVER,
                TOPIC_FILTER_INVALID,
                TOPIC_NAME_INVALID,
                RECEIVE_MAXIMUM_EXCEEDED,
                TOPIC_ALIAS_INVALID,
                PACKET_TOO_LARGE,
                MESSAGE_RATE_TOO_HIGH,
                QUOTA_EXCEEDED,
                ADMINISTRATIVE_ACTION,
                PAYLOAD_FORMAT_INVALID,
                RETAIN_NOT_SUPPORTED,
                QOS_NOT_SUPPORTED,
                USE_ANOTHER_SERVER,
                SERVER_MOVED,
                SHARED_SUBSCRIPTIONS_NOT_SUPPORTED,
                CONNECTION_RATE_EXCEEDED,
                MAXIMUM_CONNECT_TIME,
                SUBSCRIPTION_IDENTIFIERS_NOT_SUPPORTED,
                WILDCARD_SUBSCRIPTIONS_NOT_SUPPORTED,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }

    /**
     * Reason codes for MQTT Auth message.
     */
    public sealed class Auth
    {
        public static readonly Auth SUCCESS = new Auth(nameof(SUCCESS), (byte) 0x00); //sent by: Server
        public static readonly Auth CONTINUE_AUTHENTICATION = new Auth(nameof(CONTINUE_AUTHENTICATION), (byte) 0x18); //sent by: Client or Server
        public static readonly Auth REAUTHENTICATE = new Auth(nameof(REAUTHENTICATE), (byte) 0x19); //sent by: Client

        private static readonly Auth[] VALUES;

        static Auth()
        {
            Auth[] values = Values();
            VALUES = new Auth[26];
            foreach (Auth code in values)
            {
                int unsignedByte = code.byteValue & 0xFF;
                // Suppress a warning about out of bounds access since the enum contains only correct values
                VALUES[unsignedByte] = code;    //  [java/index-out-of-bounds]
            }
        }

        private readonly byte byteValue;
        private readonly string name;

        private Auth(string name, byte byteValue)
        {
            this.name = name;
            this.byteValue = byteValue;
        }

        /**
         * @return the value number corresponding to the constant.
         * */
        public byte ByteValue()
        {
            return byteValue;
        }

        /**
         * @param b the number to decode.
         * @return the enum value corresponding to the number.
         * */
        public static Auth ValueOf(byte b)
        {
            return ValueOfHelper(b, VALUES);
        }

        public string Name()
        {
            return name;
        }

        public static Auth[] Values()
        {
            return new[]
            {
                SUCCESS,
                CONTINUE_AUTHENTICATION,
                REAUTHENTICATE,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }

    /**
     * Reason codes for MQTT PubAck message.
     */
    public sealed class PubAck
    {
        public static readonly PubAck SUCCESS = new PubAck(nameof(SUCCESS), (byte) 0x00);
        public static readonly PubAck NO_MATCHING_SUBSCRIBERS = new PubAck(nameof(NO_MATCHING_SUBSCRIBERS), (byte) 0x10);
        public static readonly PubAck UNSPECIFIED_ERROR = new PubAck(nameof(UNSPECIFIED_ERROR), (byte) 0x80);
        public static readonly PubAck IMPLEMENTATION_SPECIFIC_ERROR = new PubAck(nameof(IMPLEMENTATION_SPECIFIC_ERROR), (byte) 0x83);
        public static readonly PubAck NOT_AUTHORIZED = new PubAck(nameof(NOT_AUTHORIZED), (byte) 0x87);
        public static readonly PubAck TOPIC_NAME_INVALID = new PubAck(nameof(TOPIC_NAME_INVALID), (byte) 0x90);
        public static readonly PubAck PACKET_IDENTIFIER_IN_USE = new PubAck(nameof(PACKET_IDENTIFIER_IN_USE), (byte) 0x91);
        public static readonly PubAck QUOTA_EXCEEDED = new PubAck(nameof(QUOTA_EXCEEDED), (byte) 0x97);
        public static readonly PubAck PAYLOAD_FORMAT_INVALID = new PubAck(nameof(PAYLOAD_FORMAT_INVALID), (byte) 0x99);

        private static readonly PubAck[] VALUES;

        static PubAck()
        {
            PubAck[] values = Values();
            VALUES = new PubAck[154];
            foreach (PubAck code in values)
            {
                int unsignedByte = code.byteValue & 0xFF;
                // Suppress a warning about out of bounds access since the enum contains only correct values
                VALUES[unsignedByte] = code;    //  [java/index-out-of-bounds]
            }
        }

        private readonly byte byteValue;
        private readonly string name;

        private PubAck(string name, byte byteValue)
        {
            this.name = name;
            this.byteValue = byteValue;
        }

        /**
         * @return the value number corresponding to the constant.
         * */
        public byte ByteValue()
        {
            return byteValue;
        }

        /**
         * @param b the number to decode.
         * @return the enum value corresponding to the number.
         * */
        public static PubAck ValueOf(byte b)
        {
            return ValueOfHelper(b, VALUES);
        }

        public string Name()
        {
            return name;
        }

        public static PubAck[] Values()
        {
            return new[]
            {
                SUCCESS,
                NO_MATCHING_SUBSCRIBERS,
                UNSPECIFIED_ERROR,
                IMPLEMENTATION_SPECIFIC_ERROR,
                NOT_AUTHORIZED,
                TOPIC_NAME_INVALID,
                PACKET_IDENTIFIER_IN_USE,
                QUOTA_EXCEEDED,
                PAYLOAD_FORMAT_INVALID,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }

    /**
     * Reason codes for MQTT PubRec message.
     */
    public sealed class PubRec
    {
        public static readonly PubRec SUCCESS = new PubRec(nameof(SUCCESS), (byte) 0x00);
        public static readonly PubRec NO_MATCHING_SUBSCRIBERS = new PubRec(nameof(NO_MATCHING_SUBSCRIBERS), (byte) 0x10);
        public static readonly PubRec UNSPECIFIED_ERROR = new PubRec(nameof(UNSPECIFIED_ERROR), (byte) 0x80);
        public static readonly PubRec IMPLEMENTATION_SPECIFIC_ERROR = new PubRec(nameof(IMPLEMENTATION_SPECIFIC_ERROR), (byte) 0x83);
        public static readonly PubRec NOT_AUTHORIZED = new PubRec(nameof(NOT_AUTHORIZED), (byte) 0x87);
        public static readonly PubRec TOPIC_NAME_INVALID = new PubRec(nameof(TOPIC_NAME_INVALID), (byte) 0x90);
        public static readonly PubRec PACKET_IDENTIFIER_IN_USE = new PubRec(nameof(PACKET_IDENTIFIER_IN_USE), (byte) 0x91);
        public static readonly PubRec QUOTA_EXCEEDED = new PubRec(nameof(QUOTA_EXCEEDED), (byte) 0x97);
        public static readonly PubRec PAYLOAD_FORMAT_INVALID = new PubRec(nameof(PAYLOAD_FORMAT_INVALID), (byte) 0x99);

        private static readonly PubRec[] VALUES;

        static PubRec()
        {
            PubRec[] values = Values();
            VALUES = new PubRec[154];
            foreach (PubRec code in values)
            {
                int unsignedByte = code.byteValue & 0xFF;
                // Suppress a warning about out of bounds access since the enum contains only correct values
                VALUES[unsignedByte] = code;    //  [java/index-out-of-bounds]
            }
        }

        private readonly byte byteValue;
        private readonly string name;

        private PubRec(string name, byte byteValue)
        {
            this.name = name;
            this.byteValue = byteValue;
        }

        /**
         * @return the value number corresponding to the constant.
         * */
        public byte ByteValue()
        {
            return byteValue;
        }

        /**
         * @param b the number to decode.
         * @return the enum value corresponding to the number.
         * */
        public static PubRec ValueOf(byte b)
        {
            return ValueOfHelper(b, VALUES);
        }

        public string Name()
        {
            return name;
        }

        public static PubRec[] Values()
        {
            return new[]
            {
                SUCCESS,
                NO_MATCHING_SUBSCRIBERS,
                UNSPECIFIED_ERROR,
                IMPLEMENTATION_SPECIFIC_ERROR,
                NOT_AUTHORIZED,
                TOPIC_NAME_INVALID,
                PACKET_IDENTIFIER_IN_USE,
                QUOTA_EXCEEDED,
                PAYLOAD_FORMAT_INVALID,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }

    /**
     * Reason codes for MQTT PubRel message.
     */
    public sealed class PubRel
    {
        public static readonly PubRel SUCCESS = new PubRel(nameof(SUCCESS), (byte) 0x00);
        public static readonly PubRel PACKET_IDENTIFIER_NOT_FOUND = new PubRel(nameof(PACKET_IDENTIFIER_NOT_FOUND), (byte) 0x92);

        private static readonly PubRel[] VALUES;

        static PubRel()
        {
            PubRel[] values = Values();
            VALUES = new PubRel[147];
            foreach (PubRel code in values)
            {
                int unsignedByte = code.byteValue & 0xFF;
                // Suppress a warning about out of bounds access since the enum contains only correct values
                VALUES[unsignedByte] = code;    //  [java/index-out-of-bounds]
            }
        }

        private readonly byte byteValue;
        private readonly string name;

        private PubRel(string name, byte byteValue)
        {
            this.name = name;
            this.byteValue = byteValue;
        }

        /**
         * @return the value number corresponding to the constant.
         * */
        public byte ByteValue()
        {
            return byteValue;
        }

        /**
         * @param b the number to decode.
         * @return the enum value corresponding to the number.
         * */
        public static PubRel ValueOf(byte b)
        {
            return ValueOfHelper(b, VALUES);
        }

        public string Name()
        {
            return name;
        }

        public static PubRel[] Values()
        {
            return new[]
            {
                SUCCESS,
                PACKET_IDENTIFIER_NOT_FOUND,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }

    /**
     * Reason codes for MQTT PubComp message.
     */
    public sealed class PubComp
    {
        public static readonly PubComp SUCCESS = new PubComp(nameof(SUCCESS), (byte) 0x00);
        public static readonly PubComp PACKET_IDENTIFIER_NOT_FOUND = new PubComp(nameof(PACKET_IDENTIFIER_NOT_FOUND), (byte) 0x92);

        private static readonly PubComp[] VALUES;

        static PubComp()
        {
            PubComp[] values = Values();
            VALUES = new PubComp[147];
            foreach (PubComp code in values)
            {
                int unsignedByte = code.byteValue & 0xFF;
                // Suppress a warning about out of bounds access since the enum contains only correct values
                VALUES[unsignedByte] = code;    //  [java/index-out-of-bounds]
            }
        }

        private readonly byte byteValue;
        private readonly string name;

        private PubComp(string name, byte byteValue)
        {
            this.name = name;
            this.byteValue = byteValue;
        }

        /**
         * @return the value number corresponding to the constant.
         * */
        public byte ByteValue()
        {
            return byteValue;
        }

        /**
         * @param b the number to decode.
         * @return the enum value corresponding to the number.
         * */
        public static PubComp ValueOf(byte b)
        {
            return ValueOfHelper(b, VALUES);
        }

        public string Name()
        {
            return name;
        }

        public static PubComp[] Values()
        {
            return new[]
            {
                SUCCESS,
                PACKET_IDENTIFIER_NOT_FOUND,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }

    /**
     * Reason codes for MQTT SubAck message.
     */
    public sealed class SubAck
    {
        public static readonly SubAck GRANTED_QOS_0 = new SubAck(nameof(GRANTED_QOS_0), (byte) 0x00);
        public static readonly SubAck GRANTED_QOS_1 = new SubAck(nameof(GRANTED_QOS_1), (byte) 0x01);
        public static readonly SubAck GRANTED_QOS_2 = new SubAck(nameof(GRANTED_QOS_2), (byte) 0x02);
        public static readonly SubAck UNSPECIFIED_ERROR = new SubAck(nameof(UNSPECIFIED_ERROR), (byte) 0x80);
        public static readonly SubAck IMPLEMENTATION_SPECIFIC_ERROR = new SubAck(nameof(IMPLEMENTATION_SPECIFIC_ERROR), (byte) 0x83);
        public static readonly SubAck NOT_AUTHORIZED = new SubAck(nameof(NOT_AUTHORIZED), (byte) 0x87);
        public static readonly SubAck TOPIC_FILTER_INVALID = new SubAck(nameof(TOPIC_FILTER_INVALID), (byte) 0x8F);
        public static readonly SubAck PACKET_IDENTIFIER_IN_USE = new SubAck(nameof(PACKET_IDENTIFIER_IN_USE), (byte) 0x91);
        public static readonly SubAck QUOTA_EXCEEDED = new SubAck(nameof(QUOTA_EXCEEDED), (byte) 0x97);
        public static readonly SubAck SHARED_SUBSCRIPTIONS_NOT_SUPPORTED = new SubAck(nameof(SHARED_SUBSCRIPTIONS_NOT_SUPPORTED), (byte) 0x9E);
        public static readonly SubAck SUBSCRIPTION_IDENTIFIERS_NOT_SUPPORTED = new SubAck(nameof(SUBSCRIPTION_IDENTIFIERS_NOT_SUPPORTED), (byte) 0xA1);
        public static readonly SubAck WILDCARD_SUBSCRIPTIONS_NOT_SUPPORTED = new SubAck(nameof(WILDCARD_SUBSCRIPTIONS_NOT_SUPPORTED), (byte) 0xA2);

        private static readonly SubAck[] VALUES;

        static SubAck()
        {
            SubAck[] values = Values();
            VALUES = new SubAck[163];
            foreach (SubAck code in values)
            {
                int unsignedByte = code.byteValue & 0xFF;
                // Suppress a warning about out of bounds access since the enum contains only correct values
                VALUES[unsignedByte] = code;    //  [java/index-out-of-bounds]
            }
        }

        private readonly byte byteValue;
        private readonly string name;

        private SubAck(string name, byte byteValue)
        {
            this.name = name;
            this.byteValue = byteValue;
        }

        /**
         * @return the value number corresponding to the constant.
         * */
        public byte ByteValue()
        {
            return byteValue;
        }

        /**
         * @param b the number to decode.
         * @return the enum value corresponding to the number.
         * */
        public static SubAck ValueOf(byte b)
        {
            return ValueOfHelper(b, VALUES);
        }

        public string Name()
        {
            return name;
        }

        public static SubAck[] Values()
        {
            return new[]
            {
                GRANTED_QOS_0,
                GRANTED_QOS_1,
                GRANTED_QOS_2,
                UNSPECIFIED_ERROR,
                IMPLEMENTATION_SPECIFIC_ERROR,
                NOT_AUTHORIZED,
                TOPIC_FILTER_INVALID,
                PACKET_IDENTIFIER_IN_USE,
                QUOTA_EXCEEDED,
                SHARED_SUBSCRIPTIONS_NOT_SUPPORTED,
                SUBSCRIPTION_IDENTIFIERS_NOT_SUPPORTED,
                WILDCARD_SUBSCRIPTIONS_NOT_SUPPORTED,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }

    /**
     * Reason codes for MQTT UnsubAck message.
     */
    public sealed class UnsubAck
    {
        public static readonly UnsubAck SUCCESS = new UnsubAck(nameof(SUCCESS), (byte) 0x00);
        public static readonly UnsubAck NO_SUBSCRIPTION_EXISTED = new UnsubAck(nameof(NO_SUBSCRIPTION_EXISTED), (byte) 0x11);
        public static readonly UnsubAck UNSPECIFIED_ERROR = new UnsubAck(nameof(UNSPECIFIED_ERROR), (byte) 0x80);
        public static readonly UnsubAck IMPLEMENTATION_SPECIFIC_ERROR = new UnsubAck(nameof(IMPLEMENTATION_SPECIFIC_ERROR), (byte) 0x83);
        public static readonly UnsubAck NOT_AUTHORIZED = new UnsubAck(nameof(NOT_AUTHORIZED), (byte) 0x87);
        public static readonly UnsubAck TOPIC_FILTER_INVALID = new UnsubAck(nameof(TOPIC_FILTER_INVALID), (byte) 0x8F);
        public static readonly UnsubAck PACKET_IDENTIFIER_IN_USE = new UnsubAck(nameof(PACKET_IDENTIFIER_IN_USE), (byte) 0x91);

        private static readonly UnsubAck[] VALUES;

        static UnsubAck()
        {
            UnsubAck[] values = Values();
            VALUES = new UnsubAck[146];
            foreach (UnsubAck code in values)
            {
                int unsignedByte = code.byteValue & 0xFF;
                // Suppress a warning about out of bounds access since the enum contains only correct values
                VALUES[unsignedByte] = code;    //  [java/index-out-of-bounds]
            }
        }

        private readonly byte byteValue;
        private readonly string name;

        private UnsubAck(string name, byte byteValue)
        {
            this.name = name;
            this.byteValue = byteValue;
        }

        /**
         * @return the value number corresponding to the constant.
         * */
        public byte ByteValue()
        {
            return byteValue;
        }

        /**
         * @param b the number to decode.
         * @return the enum value corresponding to the number.
         * */
        public static UnsubAck ValueOf(byte b)
        {
            return ValueOfHelper(b, VALUES);
        }

        public string Name()
        {
            return name;
        }

        public static UnsubAck[] Values()
        {
            return new[]
            {
                SUCCESS,
                NO_SUBSCRIPTION_EXISTED,
                UNSPECIFIED_ERROR,
                IMPLEMENTATION_SPECIFIC_ERROR,
                NOT_AUTHORIZED,
                TOPIC_FILTER_INVALID,
                PACKET_IDENTIFIER_IN_USE,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }
}
