/*
 * Copyright 2013 The Netty Project
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

namespace Netty.NET.Transport.Tests;

public class DefaultChannelIdTest
{
    [Fact]
    public void TestShortText()
    {
        string text = DefaultChannelId.NewInstance().AsShortText();
        Assert.Matches("^[0-9a-f]{8}$", text);
    }
    [Fact]
    public void TestSerialization()
    {
        IChannelId a = DefaultChannelId.NewInstance();
        IChannelId b;
        using var buf = new MemoryStream();
        // CLR: use the concrete type's JSON contract with source-generated metadata instead of Java ObjectStreams.
        JsonSerializer.Serialize(buf, (DefaultChannelId)a, ChannelIdJsonContext.Default.DefaultChannelId);
        buf.Position = 0;
        b = JsonSerializer.Deserialize(buf, ChannelIdJsonContext.Default.DefaultChannelId);
        Assert.Equal(a, b);
        Assert.NotSame(a, b);
        Assert.Equal(a.AsLongText(), b.AsLongText());
    }
}
