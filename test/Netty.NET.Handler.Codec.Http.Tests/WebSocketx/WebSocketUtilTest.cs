/*
 * Copyright 2018 The Netty Project
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
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Base64Codec;
using Netty.NET.Handler.Codec.Http.WebSocketx;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: the package-private WebSocketUtil is internal and reached through InternalsVisibleTo. The codec-base Base64 is
// Netty.NET.Handler.Codec.Base64Codec.Base64 (docs/codec-base-porting.md), EmptyArrays.EMPTY_BYTES is the Common
// EmptyArrays.EMPTY_BYTES, String.getBytes(UTF_8) is Encoding.UTF8.GetBytes and new String(bytes, UTF_8) is
// Encoding.UTF8.GetString (CharsetUtil.UTF_8 is Encoding.UTF8, docs/codec-smtp-porting.md).
public class WebSocketUtilTest
{

    // how many times do we want to run each random variable checker
    private const int NUM_ITERATIONS = 1000;

    private static void AssertRandomWithinBoundaries(int min, int max)
    {
        int r = WebSocketUtil.RandomNumber(min, max);
        Assert.True(min <= r && r <= max);
    }

    [Fact]
    public void TestRandomNumberGenerator()
    {
        int iteration = 0;
        while (++iteration < NUM_ITERATIONS)
        {
            AssertRandomWithinBoundaries(0, 1);
            AssertRandomWithinBoundaries(0, 1);
            AssertRandomWithinBoundaries(-1, 1);
            AssertRandomWithinBoundaries(-1, 0);
        }
    }

    [Fact]
    public void TestBase64()
    {
        string base64 = WebSocketUtil.Base64(EmptyArrays.EMPTY_BYTES);
        Assert.NotNull(base64);
        Assert.True(base64.Length == 0);

        base64 = WebSocketUtil.Base64(Encoding.UTF8.GetBytes("foo"));
        Assert.Equal("Zm9v", base64);

        base64 = WebSocketUtil.Base64(Encoding.UTF8.GetBytes("bar"));
        ByteBuf src = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(base64));
        try
        {
            ByteBuf dst = Base64.Decode(src);
            try
            {
                Assert.Equal("bar", Encoding.UTF8.GetString(ByteBufUtil.GetBytes(dst)));
            }
            finally
            {
                dst.Release();
            }
        }
        finally
        {
            src.Release();
        }
    }
}
