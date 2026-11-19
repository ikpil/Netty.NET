/*
 * Copyright 2016 The Netty Project
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
public class LineEncoderTest
{
    [Fact]
    public void TestEncode()
    {
        TestLineEncode(LineSeparator.DEFAULT, "abc");
        TestLineEncode(LineSeparator.WINDOWS, "abc");
        TestLineEncode(LineSeparator.UNIX, "abc");
    }

    private static void TestLineEncode(LineSeparator lineSeparator, string msg)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new LineEncoder(lineSeparator, Encoding.UTF8));
        Assert.True(channel.WriteOutbound(msg));
        ByteBuf buf = channel.ReadOutbound<ByteBuf>();
        try
        {
            byte[] data = new byte[buf.ReadableBytes];
            buf.ReadBytes(data);
            byte[] expected = Encoding.UTF8.GetBytes(msg + lineSeparator.Value());
            Assert.Equal(expected, data);
            Assert.Null(channel.ReadOutbound());
        }
        finally
        {
            buf.Release();
            Assert.False(channel.Finish());
        }
    }
}
