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
using Netty.NET.Handler.Codec.Memcache.Binary;

namespace Netty.NET.Handler.Codec.Memcache.Tests.Binary;

// CLR: CharsetUtil.UTF_8 is Encoding.UTF8 (docs/codec-base-porting.md).
public class BinaryMemcacheMessageTest
{

    [Fact]
    public void TestSetLengths()
    {
        ByteBuf key = Unpooled.CopiedBuffer("Netty  Rocks!", Encoding.UTF8);
        ByteBuf extras = Unpooled.CopiedBuffer("some extras", Encoding.UTF8);
        ByteBuf content = Unpooled.CopiedBuffer("content", Encoding.UTF8);
        try
        {
            TestSettingLengths(new DefaultBinaryMemcacheRequest(), 0, 0, 0);
            TestSettingLengths(new DefaultBinaryMemcacheRequest(key.Retain()), key.ReadableBytes, 0, 0);
            TestSettingLengths(new DefaultBinaryMemcacheRequest(key.Retain(), extras.Retain()),
                    key.ReadableBytes, extras.ReadableBytes, 0);

            TestSettingLengths(new DefaultBinaryMemcacheResponse(), 0, 0, 0);
            TestSettingLengths(new DefaultBinaryMemcacheResponse(key.Retain()), key.ReadableBytes, 0, 0);
            TestSettingLengths(new DefaultBinaryMemcacheResponse(key.Retain(), extras.Retain()),
                    key.ReadableBytes, extras.ReadableBytes, 0);

            TestSettingLengths(new DefaultFullBinaryMemcacheRequest(key.Retain(), extras.Retain()),
                    key.ReadableBytes, extras.ReadableBytes, 0);
            TestSettingLengths(new DefaultFullBinaryMemcacheRequest(null, extras.Retain()),
                    0, extras.ReadableBytes, 0);
            TestSettingLengths(new DefaultFullBinaryMemcacheRequest(key.Retain(), null),
                    key.ReadableBytes, 0, 0);
            TestSettingLengths(new DefaultFullBinaryMemcacheRequest(null, null), 0, 0, 0);
            TestSettingLengths(new DefaultFullBinaryMemcacheRequest(key.Retain(), extras.Retain(), content.Retain()),
                    key.ReadableBytes, extras.ReadableBytes, content.ReadableBytes);
            TestSettingLengths(new DefaultFullBinaryMemcacheRequest(null, extras.Retain(), content.Retain()),
                    0, extras.ReadableBytes, content.ReadableBytes);
            TestSettingLengths(new DefaultFullBinaryMemcacheRequest(key.Retain(), null, content.Retain()),
                    key.ReadableBytes, 0, content.ReadableBytes);
            TestSettingLengths(new DefaultFullBinaryMemcacheRequest(null, null, content.Retain()),
                    0, 0, content.ReadableBytes);

            TestSettingLengths(new DefaultFullBinaryMemcacheResponse(key.Retain(), extras.Retain()),
                    key.ReadableBytes, extras.ReadableBytes, 0);
            TestSettingLengths(new DefaultFullBinaryMemcacheResponse(null, extras.Retain()),
                    0, extras.ReadableBytes, 0);
            TestSettingLengths(new DefaultFullBinaryMemcacheResponse(key.Retain(), null),
                    key.ReadableBytes, 0, 0);
            TestSettingLengths(new DefaultFullBinaryMemcacheResponse(null, null), 0, 0, 0);
            TestSettingLengths(new DefaultFullBinaryMemcacheResponse(key.Retain(), extras.Retain(), content.Retain()),
                    key.ReadableBytes, extras.ReadableBytes, content.ReadableBytes);
            TestSettingLengths(new DefaultFullBinaryMemcacheResponse(null, extras.Retain(), content.Retain()),
                    0, extras.ReadableBytes, content.ReadableBytes);
            TestSettingLengths(new DefaultFullBinaryMemcacheResponse(key.Retain(), null, content.Retain()),
                    key.ReadableBytes, 0, content.ReadableBytes);
            TestSettingLengths(new DefaultFullBinaryMemcacheResponse(null, null, content.Retain()),
                    0, 0, content.ReadableBytes);
        }
        finally
        {
            key.Release();
            extras.Release();
            content.Release();
        }
    }

    private static void TestSettingLengths(IBinaryMemcacheMessage message,
                                    int initialKeyLength, int initialExtrasLength, int contentLength)
    {
        ByteBuf key = Unpooled.CopiedBuffer("netty", Encoding.UTF8);
        ByteBuf extras = Unpooled.CopiedBuffer("extras", Encoding.UTF8);
        ByteBuf key2 = Unpooled.CopiedBuffer("netty!", Encoding.UTF8);
        ByteBuf extras2 = Unpooled.CopiedBuffer("extras!", Encoding.UTF8);
        try
        {
            Assert.Equal(initialKeyLength, message.KeyLength());
            Assert.Equal(initialExtrasLength, message.ExtrasLength());
            Assert.Equal(initialKeyLength + initialExtrasLength + contentLength, message.TotalBodyLength());

            message.SetKey(key.Retain());
            Assert.Equal(key.ReadableBytes, message.KeyLength());
            Assert.Equal(initialExtrasLength, message.ExtrasLength());
            Assert.Equal(key.ReadableBytes + initialExtrasLength + contentLength, message.TotalBodyLength());

            message.SetExtras(extras.Retain());
            Assert.Equal(key.ReadableBytes, message.KeyLength());
            Assert.Equal(extras.ReadableBytes, message.ExtrasLength());
            Assert.Equal(key.ReadableBytes + extras.ReadableBytes + contentLength, message.TotalBodyLength());

            // Replace the previous key
            message.SetKey(key2.Retain());
            Assert.Equal(key2.ReadableBytes, message.KeyLength());
            Assert.Equal(extras.ReadableBytes, message.ExtrasLength());
            Assert.Equal(key2.ReadableBytes + extras.ReadableBytes + contentLength, message.TotalBodyLength());

            // Replace the previous extras
            message.SetExtras(extras2.Retain());
            Assert.Equal(key2.ReadableBytes, message.KeyLength());
            Assert.Equal(extras2.ReadableBytes, message.ExtrasLength());
            Assert.Equal(key2.ReadableBytes + extras2.ReadableBytes + contentLength, message.TotalBodyLength());
        }
        finally
        {
            key.Release();
            extras.Release();
            key2.Release();
            extras2.Release();
            message.Release();
        }
    }
}
