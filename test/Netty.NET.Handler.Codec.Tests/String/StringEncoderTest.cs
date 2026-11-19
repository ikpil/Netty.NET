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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.String;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Tests.String;

// CLR: CharsetUtil.UTF_8 is Encoding.UTF8.
public class StringEncoderTest
{
    [Fact]
    public void TestEncode()
    {
        string msg = "Test";
        EmbeddedChannel channel = new EmbeddedChannel(new StringEncoder());
        Assert.True(channel.WriteOutbound(msg));
        Assert.True(channel.Finish());
        ByteBuf buf = channel.ReadOutbound<ByteBuf>();
        byte[] data = new byte[buf.ReadableBytes];
        buf.ReadBytes(data);
        Assert.Equal(Encoding.UTF8.GetBytes(msg), data);
        Assert.Null(channel.ReadOutbound());
        buf.Release();
        Assert.False(channel.Finish());
    }
}
