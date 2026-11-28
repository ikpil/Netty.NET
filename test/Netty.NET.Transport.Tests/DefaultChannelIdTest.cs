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
    public void TestLongText()
    {
        string text = DefaultChannelId.NewInstance().AsLongText();
        Assert.Matches("^[0-9a-f]{16}-[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{16}-[0-9a-f]{8}$", text);
    }

    [Fact]
    public void TestIdempotentMachineId()
    {
        string a = DefaultChannelId.NewInstance().AsLongText().Substring(0, 16);
        string b = DefaultChannelId.NewInstance().AsLongText().Substring(0, 16);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TestIdempotentProcessId()
    {
        string a = DefaultChannelId.NewInstance().AsLongText().Substring(17, 4);
        string b = DefaultChannelId.NewInstance().AsLongText().Substring(17, 4);
        Assert.Equal(a, b);
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

    [Fact]
    public void TestDeserialization()
    {
        // DefaultChannelId with 8 byte machineId
        DefaultChannelId c8 = new DefaultChannelId(
                new byte[] {
                        (byte) 0x01, (byte) 0x23, (byte) 0x45, (byte) 0x67,
                        (byte) 0x89, (byte) 0xab, (byte) 0xcd, (byte) 0xef
                },
                0x000052af,
                0x00000000,
                0x06504f638eb4c386L,
                unchecked((int)0xd964df5e));

        // DefaultChannelId with 6 byte machineId
        DefaultChannelId c6 =
                new DefaultChannelId(
                        new byte[] {
                                (byte) 0x01, (byte) 0x23, (byte) 0x45, (byte) 0x67,
                                (byte) 0x89, (byte) 0xab,
                        },
                        unchecked((int)0xce005283),
                        0x00000001,
                        0x069e6dce9eb4516fL,
                        0x721757b7);

        Assert.Equal("0123456789abcdef-000052af-00000000-06504f638eb4c386-d964df5e", c8.AsLongText());
        Assert.Equal("0123456789ab-ce005283-00000001-069e6dce9eb4516f-721757b7", c6.AsLongText());
    }
}
