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

using System;
using System.Collections.Generic;
using Netty.NET.Buffer;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Mqtt;

// CLR: the @Deprecated ConnectBuilder.willMessage(String)/password(String) and PubAckBuilder/SubAckBuilder/
// UnsubAckBuilder.packetId(short) name their replacements (willMessage(byte[]), password(byte[]) and packetId(int)), so
// they are not ported (owner decision 2026-11-16); their Javadoc is recorded in docs/codec-mqtt-porting-manifest.json.
// Package-private constructors are internal. Boxed Long/Byte/Boolean/Integer fields are nullable value types, and
// longValue/intValue narrowing is the unchecked C# cast.
public sealed class MqttMessageBuilders
{

    public sealed class PublishBuilder
    {
        private string topic;
        private bool retained;
        private MqttQoS qos;
        private ByteBuf payload;
        private int messageId;
        private MqttProperties mqttProperties;

        internal PublishBuilder()
        {
        }

        public PublishBuilder TopicName(string topic)
        {
            this.topic = topic;
            return this;
        }

        public PublishBuilder Retained(bool retained)
        {
            this.retained = retained;
            return this;
        }

        public PublishBuilder Qos(MqttQoS qos)
        {
            this.qos = qos;
            return this;
        }

        public PublishBuilder Payload(ByteBuf payload)
        {
            this.payload = payload;
            return this;
        }

        public PublishBuilder MessageId(int messageId)
        {
            this.messageId = messageId;
            return this;
        }

        public PublishBuilder Properties(MqttProperties properties)
        {
            this.mqttProperties = properties;
            return this;
        }

        public MqttPublishMessage Build()
        {
            MqttFixedHeader mqttFixedHeader = new MqttFixedHeader(MqttMessageType.PUBLISH, false, qos, retained, 0);
            MqttPublishVariableHeader mqttVariableHeader =
                    new MqttPublishVariableHeader(topic, messageId, mqttProperties);
            return new MqttPublishMessage(mqttFixedHeader, mqttVariableHeader, Unpooled.Buffer().WriteBytes(payload));
        }
    }

    public sealed class ConnectBuilder
    {

        private MqttVersion version = MqttVersion.MQTT_3_1_1;
        private string clientId;
        private bool cleanSession;
        private bool hasUser;
        private bool hasPassword;
        private int keepAliveSecs;
        private MqttProperties willProperties = MqttProperties.NO_PROPERTIES;
        private bool willFlag;
        private bool willRetain;
        private MqttQoS willQos = MqttQoS.AT_MOST_ONCE;
        private string willTopic;
        private byte[] willMessage;
        private string username;
        private byte[] password;
        private MqttProperties properties = MqttProperties.NO_PROPERTIES;

        internal ConnectBuilder()
        {
        }

        public ConnectBuilder ProtocolVersion(MqttVersion version)
        {
            this.version = version;
            return this;
        }

        public ConnectBuilder ClientId(string clientId)
        {
            this.clientId = clientId;
            return this;
        }

        public ConnectBuilder CleanSession(bool cleanSession)
        {
            this.cleanSession = cleanSession;
            return this;
        }

        public ConnectBuilder KeepAlive(int keepAliveSecs)
        {
            this.keepAliveSecs = keepAliveSecs;
            return this;
        }

        public ConnectBuilder WillFlag(bool willFlag)
        {
            this.willFlag = willFlag;
            return this;
        }

        public ConnectBuilder WillQoS(MqttQoS willQos)
        {
            this.willQos = willQos;
            return this;
        }

        public ConnectBuilder WillTopic(string willTopic)
        {
            this.willTopic = willTopic;
            return this;
        }

        public ConnectBuilder WillMessage(byte[] willMessage)
        {
            this.willMessage = willMessage;
            return this;
        }

        public ConnectBuilder WillRetain(bool willRetain)
        {
            this.willRetain = willRetain;
            return this;
        }

        public ConnectBuilder WillProperties(MqttProperties willProperties)
        {
            this.willProperties = willProperties;
            return this;
        }

        public ConnectBuilder HasUser(bool value)
        {
            this.hasUser = value;
            return this;
        }

        public ConnectBuilder HasPassword(bool value)
        {
            this.hasPassword = value;
            return this;
        }

        public ConnectBuilder Username(string username)
        {
            this.hasUser = username != null;
            this.username = username;
            return this;
        }

        public ConnectBuilder Password(byte[] password)
        {
            this.hasPassword = password != null;
            this.password = password;
            return this;
        }

        public ConnectBuilder Properties(MqttProperties properties)
        {
            this.properties = properties;
            return this;
        }

        public MqttConnectMessage Build()
        {
            MqttFixedHeader mqttFixedHeader =
                    new MqttFixedHeader(MqttMessageType.CONNECT, false, MqttQoS.AT_MOST_ONCE, false, 0);
            MqttConnectVariableHeader mqttConnectVariableHeader =
                    new MqttConnectVariableHeader(
                            version.ProtocolName(),
                            version.ProtocolLevel(),
                            hasUser,
                            hasPassword,
                            willRetain,
                            willQos.Value(),
                            willFlag,
                            cleanSession,
                            keepAliveSecs,
                            properties);
            MqttConnectPayload mqttConnectPayload =
                    new MqttConnectPayload(clientId, willProperties, willTopic, willMessage, username, password);
            return new MqttConnectMessage(mqttFixedHeader, mqttConnectVariableHeader, mqttConnectPayload);
        }
    }

    public sealed class SubscribeBuilder
    {

        private List<MqttTopicSubscription> subscriptions;
        private int messageId;
        private MqttProperties properties;

        internal SubscribeBuilder()
        {
        }

        public SubscribeBuilder AddSubscription(MqttQoS qos, string topic)
        {
            EnsureSubscriptionsExist();
            subscriptions.Add(new MqttTopicSubscription(topic, qos));
            return this;
        }

        public SubscribeBuilder AddSubscription(string topic, MqttSubscriptionOption option)
        {
            EnsureSubscriptionsExist();
            subscriptions.Add(new MqttTopicSubscription(topic, option));
            return this;
        }

        public SubscribeBuilder MessageId(int messageId)
        {
            this.messageId = messageId;
            return this;
        }

        public SubscribeBuilder Properties(MqttProperties properties)
        {
            this.properties = properties;
            return this;
        }

        public MqttSubscribeMessage Build()
        {
            MqttFixedHeader mqttFixedHeader =
                    new MqttFixedHeader(MqttMessageType.SUBSCRIBE, false, MqttQoS.AT_LEAST_ONCE, false, 0);
            MqttMessageIdAndPropertiesVariableHeader mqttVariableHeader =
                    new MqttMessageIdAndPropertiesVariableHeader(messageId, properties);
            MqttSubscribePayload mqttSubscribePayload = new MqttSubscribePayload(subscriptions);
            return new MqttSubscribeMessage(mqttFixedHeader, mqttVariableHeader, mqttSubscribePayload);
        }

        private void EnsureSubscriptionsExist()
        {
            if (subscriptions == null)
            {
                subscriptions = new List<MqttTopicSubscription>(5);
            }
        }
    }

    public sealed class UnsubscribeBuilder
    {

        private List<string> topicFilters;
        private int messageId;
        private MqttProperties properties;

        internal UnsubscribeBuilder()
        {
        }

        public UnsubscribeBuilder AddTopicFilter(string topic)
        {
            if (topicFilters == null)
            {
                topicFilters = new List<string>(5);
            }
            topicFilters.Add(topic);
            return this;
        }

        public UnsubscribeBuilder MessageId(int messageId)
        {
            this.messageId = messageId;
            return this;
        }

        public UnsubscribeBuilder Properties(MqttProperties properties)
        {
            this.properties = properties;
            return this;
        }

        public MqttUnsubscribeMessage Build()
        {
            MqttFixedHeader mqttFixedHeader =
                    new MqttFixedHeader(MqttMessageType.UNSUBSCRIBE, false, MqttQoS.AT_LEAST_ONCE, false, 0);
            MqttMessageIdAndPropertiesVariableHeader mqttVariableHeader =
                    new MqttMessageIdAndPropertiesVariableHeader(messageId, properties);
            MqttUnsubscribePayload mqttSubscribePayload = new MqttUnsubscribePayload(topicFilters);
            return new MqttUnsubscribeMessage(mqttFixedHeader, mqttVariableHeader, mqttSubscribePayload);
        }
    }

    // CLR: the Java interface takes the I prefix (owner rule) and stays nested.
    public interface IPropertiesInitializer<T>
    {
        void Apply(T builder);
    }

    public sealed class ConnAckBuilder
    {

        private MqttConnectReturnCode returnCode;
        private bool sessionPresent;
        private MqttProperties properties = MqttProperties.NO_PROPERTIES;
        private ConnAckPropertiesBuilder propsBuilder;

        // CLR: a Java private constructor is usable "from within the body of the top level class" that encloses it
        // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-6.html#jls-6.6.1), so connAck() can call it; a C# private
        // member is accessible "only within the body of the class" that declares it
        // (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/private), so the constructor is internal.
        internal ConnAckBuilder()
        {
        }

        public ConnAckBuilder ReturnCode(MqttConnectReturnCode returnCode)
        {
            this.returnCode = returnCode;
            return this;
        }

        public ConnAckBuilder SessionPresent(bool sessionPresent)
        {
            this.sessionPresent = sessionPresent;
            return this;
        }

        public ConnAckBuilder Properties(MqttProperties properties)
        {
            this.properties = properties;
            return this;
        }

        public ConnAckBuilder Properties(IPropertiesInitializer<ConnAckPropertiesBuilder> consumer)
        {
            if (propsBuilder == null)
            {
                propsBuilder = new ConnAckPropertiesBuilder();
            }
            consumer.Apply(propsBuilder);
            return this;
        }

        public MqttConnAckMessage Build()
        {
            if (propsBuilder != null)
            {
                properties = propsBuilder.Build();
            }
            MqttFixedHeader mqttFixedHeader =
                    new MqttFixedHeader(MqttMessageType.CONNACK, false, MqttQoS.AT_MOST_ONCE, false, 0);
            MqttConnAckVariableHeader mqttConnAckVariableHeader =
                    new MqttConnAckVariableHeader(returnCode, sessionPresent, properties);
            return new MqttConnAckMessage(mqttFixedHeader, mqttConnAckVariableHeader);
        }
    }

    public sealed class ConnAckPropertiesBuilder
    {
        private string clientId;
        private long? sessionExpiryInterval;
        private int receiveMaximum;
        private byte? maximumQos;
        private bool retain;
        private long? maximumPacketSize;
        private int topicAliasMaximum;
        private string reasonString;
        private readonly MqttProperties.UserProperties userProperties = new MqttProperties.UserProperties();
        private bool? wildcardSubscriptionAvailable;
        private bool? subscriptionIdentifiersAvailable;
        private bool? sharedSubscriptionAvailable;
        private int? serverKeepAlive;
        private string responseInformation;
        private string serverReference;
        private string authenticationMethod;
        private byte[] authenticationData;

        public MqttProperties Build()
        {
            MqttProperties props = new MqttProperties();
            if (clientId != null)
            {
                props.Add(new MqttProperties.StringProperty(MqttProperties.ASSIGNED_CLIENT_IDENTIFIER,
                        clientId));
            }
            if (sessionExpiryInterval != null)
            {
                props.Add(new MqttProperties.IntegerProperty(
                        MqttProperties.SESSION_EXPIRY_INTERVAL, (int) sessionExpiryInterval.Value));
            }
            if (receiveMaximum > 0)
            {
                props.Add(new MqttProperties.IntegerProperty(MqttProperties.RECEIVE_MAXIMUM, receiveMaximum));
            }
            if (maximumQos != null)
            {
                props.Add(new MqttProperties.IntegerProperty(MqttProperties.MAXIMUM_QOS,
                        maximumQos.Value));
            }
            props.Add(new MqttProperties.IntegerProperty(MqttProperties.RETAIN_AVAILABLE, retain ? 1 : 0));
            if (maximumPacketSize != null)
            {
                props.Add(new MqttProperties.IntegerProperty(MqttProperties.MAXIMUM_PACKET_SIZE,
                        (int) maximumPacketSize.Value));
            }
            props.Add(new MqttProperties.IntegerProperty(MqttProperties.TOPIC_ALIAS_MAXIMUM,
                    topicAliasMaximum));
            if (reasonString != null)
            {
                props.Add(new MqttProperties.StringProperty(MqttProperties.REASON_STRING, reasonString));
            }
            props.Add(userProperties);
            if (wildcardSubscriptionAvailable != null)
            {
                props.Add(new MqttProperties.IntegerProperty(MqttProperties.WILDCARD_SUBSCRIPTION_AVAILABLE,
                        wildcardSubscriptionAvailable.Value ? 1 : 0));
            }
            if (subscriptionIdentifiersAvailable != null)
            {
                props.Add(new MqttProperties.IntegerProperty(MqttProperties.SUBSCRIPTION_IDENTIFIER_AVAILABLE,
                        subscriptionIdentifiersAvailable.Value ? 1 : 0));
            }
            if (sharedSubscriptionAvailable != null)
            {
                props.Add(new MqttProperties.IntegerProperty(MqttProperties.SHARED_SUBSCRIPTION_AVAILABLE,
                        sharedSubscriptionAvailable.Value ? 1 : 0));
            }
            if (serverKeepAlive != null)
            {
                props.Add(new MqttProperties.IntegerProperty(MqttProperties.SERVER_KEEP_ALIVE,
                        serverKeepAlive.Value));
            }
            if (responseInformation != null)
            {
                props.Add(new MqttProperties.StringProperty(MqttProperties.RESPONSE_INFORMATION,
                        responseInformation));
            }
            if (serverReference != null)
            {
                props.Add(new MqttProperties.StringProperty(MqttProperties.SERVER_REFERENCE,
                        serverReference));
            }
            if (authenticationMethod != null)
            {
                props.Add(new MqttProperties.StringProperty(MqttProperties.AUTHENTICATION_METHOD,
                        authenticationMethod));
            }
            if (authenticationData != null)
            {
                props.Add(new MqttProperties.BinaryProperty(MqttProperties.AUTHENTICATION_DATA,
                        authenticationData));
            }

            return props;
        }

        public ConnAckPropertiesBuilder SessionExpiryInterval(long seconds)
        {
            this.sessionExpiryInterval = seconds;
            return this;
        }

        public ConnAckPropertiesBuilder ReceiveMaximum(int value)
        {
            this.receiveMaximum = CheckPositive(value, "value");
            return this;
        }

        public ConnAckPropertiesBuilder MaximumQos(byte value)
        {
            if (value != 0 && value != 1)
            {
                throw new ArgumentException("maximum QoS property could be 0 or 1");
            }
            this.maximumQos = value;
            return this;
        }

        public ConnAckPropertiesBuilder RetainAvailable(bool retain)
        {
            this.retain = retain;
            return this;
        }

        public ConnAckPropertiesBuilder MaximumPacketSize(long size)
        {
            this.maximumPacketSize = CheckPositive(size, "size");
            return this;
        }

        public ConnAckPropertiesBuilder AssignedClientId(string clientId)
        {
            this.clientId = clientId;
            return this;
        }

        public ConnAckPropertiesBuilder TopicAliasMaximum(int value)
        {
            this.topicAliasMaximum = value;
            return this;
        }

        public ConnAckPropertiesBuilder ReasonString(string reason)
        {
            this.reasonString = reason;
            return this;
        }

        public ConnAckPropertiesBuilder UserProperty(string name, string value)
        {
            userProperties.Add(name, value);
            return this;
        }

        public ConnAckPropertiesBuilder WildcardSubscriptionAvailable(bool value)
        {
            this.wildcardSubscriptionAvailable = value;
            return this;
        }

        public ConnAckPropertiesBuilder SubscriptionIdentifiersAvailable(bool value)
        {
            this.subscriptionIdentifiersAvailable = value;
            return this;
        }

        public ConnAckPropertiesBuilder SharedSubscriptionAvailable(bool value)
        {
            this.sharedSubscriptionAvailable = value;
            return this;
        }

        public ConnAckPropertiesBuilder ServerKeepAlive(int seconds)
        {
            this.serverKeepAlive = seconds;
            return this;
        }

        public ConnAckPropertiesBuilder ResponseInformation(string value)
        {
            this.responseInformation = value;
            return this;
        }

        public ConnAckPropertiesBuilder ServerReference(string host)
        {
            this.serverReference = host;
            return this;
        }

        public ConnAckPropertiesBuilder AuthenticationMethod(string methodName)
        {
            this.authenticationMethod = methodName;
            return this;
        }

        public ConnAckPropertiesBuilder AuthenticationData(byte[] rawData)
        {
            this.authenticationData = (byte[]) rawData.Clone();
            return this;
        }
    }

    public sealed class PubAckBuilder
    {

        private int packetId;
        private byte reasonCode;
        private MqttProperties properties;

        internal PubAckBuilder()
        {
        }

        public PubAckBuilder ReasonCode(byte reasonCode)
        {
            this.reasonCode = reasonCode;
            return this;
        }

        public PubAckBuilder PacketId(int packetId)
        {
            this.packetId = packetId;
            return this;
        }

        public PubAckBuilder Properties(MqttProperties properties)
        {
            this.properties = properties;
            return this;
        }

        public MqttMessage Build()
        {
            MqttFixedHeader mqttFixedHeader =
                    new MqttFixedHeader(MqttMessageType.PUBACK, false, MqttQoS.AT_MOST_ONCE, false, 0);
            MqttPubReplyMessageVariableHeader mqttPubAckVariableHeader =
                    new MqttPubReplyMessageVariableHeader(packetId, reasonCode, properties);
            return new MqttMessage(mqttFixedHeader, mqttPubAckVariableHeader);
        }
    }

    public sealed class SubAckBuilder
    {

        private int packetId;
        private MqttProperties properties;
        private readonly List<MqttQoS> grantedQoses = new List<MqttQoS>();

        internal SubAckBuilder()
        {
        }

        public SubAckBuilder PacketId(int packetId)
        {
            this.packetId = packetId;
            return this;
        }

        public SubAckBuilder Properties(MqttProperties properties)
        {
            this.properties = properties;
            return this;
        }

        public SubAckBuilder AddGrantedQos(MqttQoS qos)
        {
            this.grantedQoses.Add(qos);
            return this;
        }

        public SubAckBuilder AddGrantedQoses(params MqttQoS[] qoses)
        {
            this.grantedQoses.AddRange(qoses);
            return this;
        }

        public MqttSubAckMessage Build()
        {
            MqttFixedHeader mqttFixedHeader =
                    new MqttFixedHeader(MqttMessageType.SUBACK, false, MqttQoS.AT_MOST_ONCE, false, 0);
            MqttMessageIdAndPropertiesVariableHeader mqttSubAckVariableHeader =
                    new MqttMessageIdAndPropertiesVariableHeader(packetId, properties);

            //transform to primitive types
            int[] grantedQoses = new int[this.grantedQoses.Count];
            int i = 0;
            foreach (MqttQoS grantedQos in this.grantedQoses)
            {
                grantedQoses[i++] = grantedQos.Value();
            }

            MqttSubAckPayload subAckPayload = new MqttSubAckPayload(grantedQoses);
            return new MqttSubAckMessage(mqttFixedHeader, mqttSubAckVariableHeader, subAckPayload);
        }
    }

    public sealed class UnsubAckBuilder
    {

        private int packetId;
        private MqttProperties properties;
        private readonly List<short> reasonCodes = new List<short>();

        internal UnsubAckBuilder()
        {
        }

        public UnsubAckBuilder PacketId(int packetId)
        {
            this.packetId = packetId;
            return this;
        }

        public UnsubAckBuilder Properties(MqttProperties properties)
        {
            this.properties = properties;
            return this;
        }

        public UnsubAckBuilder AddReasonCode(short reasonCode)
        {
            this.reasonCodes.Add(reasonCode);
            return this;
        }

        public UnsubAckBuilder AddReasonCodes(params short[] reasonCodes)
        {
            this.reasonCodes.AddRange(reasonCodes);
            return this;
        }

        public MqttUnsubAckMessage Build()
        {
            MqttFixedHeader mqttFixedHeader =
                    new MqttFixedHeader(MqttMessageType.UNSUBACK, false, MqttQoS.AT_MOST_ONCE, false, 0);
            MqttMessageIdAndPropertiesVariableHeader mqttSubAckVariableHeader =
                    new MqttMessageIdAndPropertiesVariableHeader(packetId, properties);

            MqttUnsubAckPayload subAckPayload = new MqttUnsubAckPayload(reasonCodes);
            return new MqttUnsubAckMessage(mqttFixedHeader, mqttSubAckVariableHeader, subAckPayload);
        }
    }

    public sealed class DisconnectBuilder
    {

        private MqttProperties properties;
        private byte reasonCode;

        internal DisconnectBuilder()
        {
        }

        public DisconnectBuilder Properties(MqttProperties properties)
        {
            this.properties = properties;
            return this;
        }

        public DisconnectBuilder ReasonCode(byte reasonCode)
        {
            this.reasonCode = reasonCode;
            return this;
        }

        public MqttMessage Build()
        {
            MqttFixedHeader mqttFixedHeader =
                    new MqttFixedHeader(MqttMessageType.DISCONNECT, false, MqttQoS.AT_MOST_ONCE, false, 0);
            MqttReasonCodeAndPropertiesVariableHeader mqttDisconnectVariableHeader =
                    new MqttReasonCodeAndPropertiesVariableHeader(reasonCode, properties);

            return new MqttMessage(mqttFixedHeader, mqttDisconnectVariableHeader);
        }
    }

    public sealed class AuthBuilder
    {

        private MqttProperties properties;
        private byte reasonCode;

        internal AuthBuilder()
        {
        }

        public AuthBuilder Properties(MqttProperties properties)
        {
            this.properties = properties;
            return this;
        }

        public AuthBuilder ReasonCode(byte reasonCode)
        {
            this.reasonCode = reasonCode;
            return this;
        }

        public MqttMessage Build()
        {
            MqttFixedHeader mqttFixedHeader =
                    new MqttFixedHeader(MqttMessageType.AUTH, false, MqttQoS.AT_MOST_ONCE, false, 0);
            MqttReasonCodeAndPropertiesVariableHeader mqttAuthVariableHeader =
                    new MqttReasonCodeAndPropertiesVariableHeader(reasonCode, properties);

            return new MqttMessage(mqttFixedHeader, mqttAuthVariableHeader);
        }
    }

    public static ConnectBuilder Connect()
    {
        return new ConnectBuilder();
    }

    public static ConnAckBuilder ConnAck()
    {
        return new ConnAckBuilder();
    }

    public static PublishBuilder Publish()
    {
        return new PublishBuilder();
    }

    public static SubscribeBuilder Subscribe()
    {
        return new SubscribeBuilder();
    }

    public static UnsubscribeBuilder Unsubscribe()
    {
        return new UnsubscribeBuilder();
    }

    public static PubAckBuilder PubAck()
    {
        return new PubAckBuilder();
    }

    public static SubAckBuilder SubAck()
    {
        return new SubAckBuilder();
    }

    public static UnsubAckBuilder UnsubAck()
    {
        return new UnsubAckBuilder();
    }

    public static DisconnectBuilder Disconnect()
    {
        return new DisconnectBuilder();
    }

    public static AuthBuilder Auth()
    {
        return new AuthBuilder();
    }

    private MqttMessageBuilders()
    {
    }
}
