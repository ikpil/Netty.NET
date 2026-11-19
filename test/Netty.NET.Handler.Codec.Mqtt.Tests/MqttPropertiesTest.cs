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
using static Netty.NET.Handler.Codec.Mqtt.MqttProperties;

namespace Netty.NET.Handler.Codec.Mqtt.Tests;

// CLR: assertEquals on two lists is the element-wise Assert.Equal<T>(IEnumerable<T>, IEnumerable<T>), with the
// element type named where the source relies on List<? extends MqttProperty>; Collections.singletonList is a one-element
// list and Integer.intValue() is the int value itself.
public class MqttPropertiesTest
{

    private MqttProperties CreateSampleProperties()
    {
        MqttProperties props = new MqttProperties();
        props.Add(new MqttProperties.IntegerProperty(SUBSCRIPTION_IDENTIFIER, 10));
        props.Add(new MqttProperties.IntegerProperty(SUBSCRIPTION_IDENTIFIER, 20));
        props.Add(new MqttProperties.IntegerProperty(PAYLOAD_FORMAT_INDICATOR, 6));
        props.Add(new MqttProperties.StringProperty(CONTENT_TYPE, "text/plain"));
        props.Add(new MqttProperties.UserProperty("isSecret", "true"));
        props.Add(new MqttProperties.UserProperty("tag", "firstTag"));
        props.Add(new MqttProperties.UserProperty("tag", "secondTag"));
        return props;
    }

    [Fact]
    public void TestGetProperty()
    {
        MqttProperties props = CreateSampleProperties();

        Assert.Equal(
                "text/plain",
                ((MqttProperties.StringProperty) props.GetProperty(CONTENT_TYPE)).value);
        Assert.Equal(
                10,
                ((MqttProperties.IntegerProperty) props.GetProperty(SUBSCRIPTION_IDENTIFIER)).value);

        List<MqttProperties.StringPair> expectedUserProps = new List<MqttProperties.StringPair>();
        expectedUserProps.Add(new MqttProperties.StringPair("isSecret", "true"));
        expectedUserProps.Add(new MqttProperties.StringPair("tag", "firstTag"));
        expectedUserProps.Add(new MqttProperties.StringPair("tag", "secondTag"));
        List<MqttProperties.StringPair> actualUserProps =
                ((MqttProperties.UserProperties) props.GetProperty(USER_PROPERTY)).value;
        Assert.Equal(expectedUserProps, actualUserProps);
    }

    [Fact]
    public void TestGetProperties()
    {
        MqttProperties props = CreateSampleProperties();

        Assert.Equal<MqttProperties.MqttProperty>(
                new List<MqttProperties.MqttProperty> { new MqttProperties.StringProperty(CONTENT_TYPE, "text/plain") },
                props.GetProperties(CONTENT_TYPE));

        List<MqttProperties.IntegerProperty> expectedSubscriptionIds = new List<MqttProperties.IntegerProperty>();
        expectedSubscriptionIds.Add(new MqttProperties.IntegerProperty(SUBSCRIPTION_IDENTIFIER, 10));
        expectedSubscriptionIds.Add(new MqttProperties.IntegerProperty(SUBSCRIPTION_IDENTIFIER, 20));
        Assert.Equal<MqttProperties.MqttProperty>(
                expectedSubscriptionIds,
               props.GetProperties(SUBSCRIPTION_IDENTIFIER));

        List<MqttProperties.UserProperty> expectedUserProps = new List<MqttProperties.UserProperty>();
        expectedUserProps.Add(new MqttProperties.UserProperty("isSecret", "true"));
        expectedUserProps.Add(new MqttProperties.UserProperty("tag", "firstTag"));
        expectedUserProps.Add(new MqttProperties.UserProperty("tag", "secondTag"));
        List<MqttProperties.UserProperty> actualUserProps =
                (List<MqttProperties.UserProperty>) props.GetProperties(USER_PROPERTY);
        Assert.Equal(expectedUserProps, actualUserProps);
    }

    [Fact]
    public void TestListAll()
    {
        MqttProperties props = CreateSampleProperties();

        List<MqttProperties.MqttProperty> expectedProperties = new List<MqttProperties.MqttProperty>();
        expectedProperties.Add(new MqttProperties.IntegerProperty(PAYLOAD_FORMAT_INDICATOR, 6));
        expectedProperties.Add(new MqttProperties.StringProperty(CONTENT_TYPE, "text/plain"));

        expectedProperties.Add(new MqttProperties.IntegerProperty(SUBSCRIPTION_IDENTIFIER, 10));
        expectedProperties.Add(new MqttProperties.IntegerProperty(SUBSCRIPTION_IDENTIFIER, 20));

        MqttProperties.UserProperties expectedUserProperties = new MqttProperties.UserProperties();
        expectedUserProperties.Add(new MqttProperties.StringPair("isSecret", "true"));
        expectedUserProperties.Add(new MqttProperties.StringPair("tag", "firstTag"));
        expectedUserProperties.Add(new MqttProperties.StringPair("tag", "secondTag"));

        expectedProperties.Add(expectedUserProperties);

        Assert.Equal<MqttProperties.MqttProperty>(expectedProperties, props.ListAll());
    }

    [Fact]
    public void TestIsEmptyWithOnlyUserProperties()
    {
        MqttProperties props = new MqttProperties();

        Assert.True(props.IsEmpty());

        props.Add(new MqttProperties.UserProperty("tag", "firstTag"));

        Assert.False(props.IsEmpty());
    }

    [Fact]
    public void TestIsEmptyWithOnlySubscriptionIdentifiers()
    {
        MqttProperties props = new MqttProperties();

        Assert.True(props.IsEmpty());

        props.Add(new MqttProperties.IntegerProperty(SUBSCRIPTION_IDENTIFIER, 10));

        Assert.False(props.IsEmpty());
    }

}
