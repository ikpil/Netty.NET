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

using System;
using System.Collections.Generic;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: the Java int literals 0xff00_001c and 0xff00_001d are negative ints, so they are unchecked int casts here.
// Map<AttributeKey<?>, Object> and Map.Entry<AttributeKey<?>, Object>[] are the Dictionary and KeyValuePair array of
// Quic.ToAttributesArray, whose values carry the attribute value with its typed setter (docs/codec-classes-quic-porting.md,
// Other adaptations), so attributes.put(key, value) stores the value with the setter Quic.UpdateAttributes would make and
// entry.getValue() is the stored value; Map<ChannelOption<?>, Object> is Dictionary<ChannelOption, object>.
// AttributeKey.valueOf(UUID.randomUUID().toString()) is AttributeKey<string>.ValueOf(Guid.NewGuid().ToString()).
// test() is CLR-inapplicable (docs/codec-native-quic-porting-manifest.json): it asserts the version string of the
// quiche JNI library (Quiche.quiche_version(), a JNI unit recorded jvm-only-replaced), and System.Net.Quic reports no
// version of msquic.
public class QuicTest : AbstractQuicTest
{

    [Fact]
    public void TestVersionSupported()
    {
        // Only v1 should be supported.
        Assert.False(Quic.IsVersionSupported(unchecked((int) 0xff00_001c)));
        Assert.False(Quic.IsVersionSupported(unchecked((int) 0xff00_001d)));
        Assert.False(Quic.IsVersionSupported(unchecked((int) 0xff00_001c)));
        Assert.True(Quic.IsVersionSupported(0x0000_0001));
    }

    [Fact]
    public void TestToAttributesArrayDoesCopy()
    {
        AttributeKey<string> key = AttributeKey<string>.ValueOf(Guid.NewGuid().ToString());
        string value = "testValue";
        Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)> attributes =
                new Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)>();
        attributes[key] = (value, channel => channel.Attr(key).Set(value));
        KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] array = Quic.ToAttributesArray(attributes);
        Assert.Single(array);
        attributes[key] = ("newTestValue", channel => channel.Attr(key).Set("newTestValue"));
        KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)> entry = array[0];
        Assert.Equal(key, entry.Key);
        Assert.Equal(value, entry.Value.Value);
    }

    [Fact]
    public void TestToOptionsArrayDoesCopy()
    {
        Dictionary<ChannelOption, object> options = new Dictionary<ChannelOption, object>();
        options[ChannelOption.AUTO_READ] = true;
        KeyValuePair<ChannelOption, object>[] array = Quic.ToOptionsArray(options);
        Assert.Single(array);
        options[ChannelOption.AUTO_READ] = false;
        KeyValuePair<ChannelOption, object> entry = array[0];
        Assert.Equal(ChannelOption.AUTO_READ, entry.Key);
        Assert.Equal(true, entry.Value);
    }
}
