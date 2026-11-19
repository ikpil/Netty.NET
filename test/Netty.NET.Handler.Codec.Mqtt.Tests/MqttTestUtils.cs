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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Netty.NET.Buffer;
using static Netty.NET.Handler.Codec.Mqtt.MqttProperties;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Mqtt.Tests;

// CLR: JUnit asserts with messages are XunitAssertMessages.AssertM; the internal propertyId/value fields are reachable
// through InternalsVisibleTo as the Java tests reach them through package access. Integer is int, and the @Deprecated
// MqttTopicSubscription.topicName() is replaced by topicFilter() (not ported, see the main manifest entry).
public sealed class MqttTestUtils
{
    private MqttTestUtils()
    {
    }

    public static void ValidateProperties(MqttProperties expected, MqttProperties actual)
    {
        foreach (MqttProperties.MqttProperty expectedProperty in expected.ListAll())
        {
            int propertyId = expectedProperty.propertyId;
            MqttProperties.MqttProperty actualProperty = actual.GetProperty(propertyId);
            IReadOnlyList<MqttProperties.MqttProperty> actualProperties =
                    actual.GetProperties(propertyId);
            switch (propertyId)
            {
                // one byte value integer property
                case PAYLOAD_FORMAT_INDICATOR:
                case REQUEST_PROBLEM_INFORMATION:
                case REQUEST_RESPONSE_INFORMATION:
                case MAXIMUM_QOS:
                case RETAIN_AVAILABLE:
                case WILDCARD_SUBSCRIPTION_AVAILABLE:
                case SUBSCRIPTION_IDENTIFIER_AVAILABLE:
                case SHARED_SUBSCRIPTION_AVAILABLE:
                {
                    int expectedValue = ((MqttProperties.IntegerProperty) expectedProperty).value;
                    int actualValue = ((MqttProperties.IntegerProperty) actualProperty).value;
                    Assert.Equal(expectedValue, actualValue, "one byte property doesn't match");
                    break;
                }
                // two byte value integer property
                case SERVER_KEEP_ALIVE:
                case RECEIVE_MAXIMUM:
                case TOPIC_ALIAS_MAXIMUM:
                case TOPIC_ALIAS:
                {
                    int expectedValue = ((MqttProperties.IntegerProperty) expectedProperty).value;
                    int actualValue = ((MqttProperties.IntegerProperty) actualProperty).value;
                    Assert.Equal(expectedValue, actualValue, "two byte property doesn't match");
                    break;
                }
                // four byte value integer property
                case PUBLICATION_EXPIRY_INTERVAL:
                case SESSION_EXPIRY_INTERVAL:
                case WILL_DELAY_INTERVAL:
                case MAXIMUM_PACKET_SIZE:
                {
                    int expectedValue = ((MqttProperties.IntegerProperty) expectedProperty).value;
                    int actualValue = ((MqttProperties.IntegerProperty) actualProperty).value;
                    Assert.Equal(expectedValue, actualValue, "four byte property doesn't match");
                    break;
                }
                // four byte value integer property
                case SUBSCRIPTION_IDENTIFIER:
                {
                    int expectedValue = ((MqttProperties.IntegerProperty) expectedProperty).value;
                    AssertContainsValue("Subscription ID doesn't match", expectedValue, actualProperties);
                    break;
                }
                // UTF-8 string value integer property
                case CONTENT_TYPE:
                case RESPONSE_TOPIC:
                case ASSIGNED_CLIENT_IDENTIFIER:
                case AUTHENTICATION_METHOD:
                case RESPONSE_INFORMATION:
                case SERVER_REFERENCE:
                case REASON_STRING:
                {
                    string expectedValue = ((MqttProperties.StringProperty) expectedProperty).value;
                    string actualValue = ((MqttProperties.StringProperty) actualProperty).value;
                    Assert.Equal(expectedValue, actualValue, "String property doesn't match");
                    break;
                }
                // User property
                case USER_PROPERTY:
                {
                    List<MqttProperties.StringPair> expectedPairs =
                            ((MqttProperties.UserProperties) expectedProperty).value;
                    List<MqttProperties.StringPair> actualPairs =
                            ((MqttProperties.UserProperties) actualProperty).value;
                    Assert.Equal(expectedPairs, actualPairs, "User properties count doesn't match");
                    for (int i = 0; i < expectedPairs.Count; i++)
                    {
                        Assert.Equal(expectedPairs[i], actualPairs[i], "User property mismatch");
                    }
                    break;
                }
                // byte[] property
                case CORRELATION_DATA:
                case AUTHENTICATION_DATA:
                {
                    byte[] expectedValue = ((MqttProperties.BinaryProperty) expectedProperty).value;
                    byte[] actualValue = ((MqttProperties.BinaryProperty) actualProperty).value;
                    string expectedHexDump = ByteBufUtil.HexDump(expectedValue);
                    string actualHexDump = ByteBufUtil.HexDump(actualValue);
                    Assert.Equal(expectedHexDump, actualHexDump, "byte[] property doesn't match");
                    break;
                }
                default:
                    // CLR: Integer.toHexString is the lower-case "x" format.
                    Assert.Fail("Property Id not recognized " + propertyId.ToString("x", CultureInfo.InvariantCulture));
                    break;
            }
        }
        foreach (MqttProperties.MqttProperty actualProperty in actual.ListAll())
        {
            MqttProperties.MqttProperty expectedProperty = expected.GetProperty(actualProperty.propertyId);
            Assert.NotNull(expectedProperty, "Property " +
                    actualProperty.propertyId.ToString(CultureInfo.InvariantCulture) + " not expected");
        }
    }

    // CLR: the source compares the boxed Integer values with ==, a reference comparison that holds for the cached
    // values the tests use; the int values are compared by value here. The List text in the message is
    // AbstractCollection.toString() ("[a, b]").
    private static void AssertContainsValue(string message,
                                            int expectedValue,
                                            IReadOnlyList<MqttProperties.MqttProperty> properties)
    {
        foreach (MqttProperties.MqttProperty property in properties)
        {
            if (property is MqttProperties.IntegerProperty &&
                    ((MqttProperties.IntegerProperty) property).value == expectedValue)
            {
                return;
            }
        }
        Assert.Fail(message + " - properties didn't contain expected integer value " +
                expectedValue.ToString(CultureInfo.InvariantCulture) + ": [" + string.Join(", ", properties) + "]");
    }

    public static void ValidateSubscribePayload(MqttSubscribePayload expected, MqttSubscribePayload actual)
    {
        IList<MqttTopicSubscription> expectedTopicSubscriptions = expected.TopicSubscriptions();
        IList<MqttTopicSubscription> actualTopicSubscriptions = actual.TopicSubscriptions();

        Assert.Equal(
                expectedTopicSubscriptions.Count,
                actualTopicSubscriptions.Count,
                "MqttSubscribePayload TopicSubscriptionList size mismatch");
        for (int i = 0; i < expectedTopicSubscriptions.Count; i++)
        {
            ValidateTopicSubscription(expectedTopicSubscriptions[i], actualTopicSubscriptions[i]);
        }
    }

    public static void ValidateTopicSubscription(
            MqttTopicSubscription expected,
            MqttTopicSubscription actual)
    {
        Assert.Equal(expected.TopicFilter(), actual.TopicFilter(), "MqttTopicSubscription TopicName mismatch");
        Assert.Equal(
                expected.QualityOfService(),
                actual.QualityOfService(),
                "MqttTopicSubscription Qos mismatch");
        Assert.Equal(
                expected.Option(),
                actual.Option(),
                "MqttTopicSubscription options mismatch");
    }

    public static void ValidateUnsubscribePayload(MqttUnsubscribePayload expected, MqttUnsubscribePayload actual)
    {
        Assert.Equal(
                expected.Topics().ToArray(),
                actual.Topics().ToArray(),
                "MqttUnsubscribePayload TopicList mismatch");
    }
}
