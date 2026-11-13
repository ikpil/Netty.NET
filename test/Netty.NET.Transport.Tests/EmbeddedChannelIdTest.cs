/*
 * Copyright 2015 The Netty Project
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
using System.IO;
using System.Text.Json;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

public class EmbeddedChannelIdTest
{
    [Fact]
    public void TestSerialization()
    {
        // test that a deserialized instance works the same as a normal instance (issue #2869)
        IChannelId normalInstance = EmbeddedChannelId.Instance;
        using var buf = new MemoryStream();
        // CLR: serialize the concrete value with the BCL JSON contract; stream ownership stays with this test.
        JsonSerializer.Serialize(buf, (EmbeddedChannelId)normalInstance, ChannelIdJsonContext.Default.EmbeddedChannelId);
        buf.Position = 0;
        IChannelId deserializedInstance = JsonSerializer.Deserialize(buf, ChannelIdJsonContext.Default.EmbeddedChannelId);
        Assert.Equal(normalInstance, deserializedInstance);
        Assert.Equal(normalInstance.GetHashCode(), deserializedInstance.GetHashCode());
        Assert.Equal(0, normalInstance.CompareTo(deserializedInstance));
    }
}
