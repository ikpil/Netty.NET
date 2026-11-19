/*
 * Copyright 2021 The Netty Project
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

using static Netty.NET.Handler.Codec.Redis.Tests.RedisCodecTestUtil;

namespace Netty.NET.Handler.Codec.Redis.Tests;

#pragma warning disable xUnit2000 // assertEquals(fromStr.content(), "OK") keeps the source argument order.

/**
 * Verifies the correct functionality of the {@link FixedRedisMessagePool}.
 */
public class FixedRedisMessagePoolTest
{

    [Fact]
    public void ShouldGetSameMessageObject()
    {
        FixedRedisMessagePool pool = FixedRedisMessagePool.INSTANCE;

        SimpleStringRedisMessage fromStr = pool.GetSimpleString("OK");
        SimpleStringRedisMessage fromEnum = pool.GetSimpleString(FixedRedisMessagePool.RedisReplyKey.OK);
        SimpleStringRedisMessage fromByteBuf = pool.GetSimpleString(ByteBufOf("OK"));
        Assert.Equal(fromStr.Content(), "OK");
        Assert.Equal(fromStr, fromEnum);
        Assert.Equal(fromStr, fromByteBuf);

        ErrorRedisMessage errorFromStr = pool.GetError("NOAUTH Authentication required.");
        ErrorRedisMessage errorFromEnum = pool.GetError(FixedRedisMessagePool.RedisErrorKey.NOT_AUTH);
        ErrorRedisMessage errorFromByteBuf = pool.GetError(ByteBufOf("NOAUTH Authentication required."));
        Assert.Equal(errorFromStr.Content(), "NOAUTH Authentication required.");
        Assert.Equal(errorFromStr, errorFromEnum);
        Assert.Equal(errorFromStr, errorFromByteBuf);
    }

    [Fact]
    public void ShouldReturnNullByNotExistKey()
    {
        FixedRedisMessagePool pool = FixedRedisMessagePool.INSTANCE;

        SimpleStringRedisMessage message1 = pool.GetSimpleString("Not exist");
        SimpleStringRedisMessage message2 = pool.GetSimpleString(ByteBufOf("Not exist"));
        Assert.Null(message1);
        Assert.Null(message2);

        ErrorRedisMessage error1 = pool.GetError("Not exist");
        ErrorRedisMessage error2 = pool.GetError(ByteBufOf("Not exist"));
        Assert.Null(error1);
        Assert.Null(error2);
    }

    [Fact]
    public void ShouldReturnDifferentMessage()
    {
        FixedRedisMessagePool pool = FixedRedisMessagePool.INSTANCE;
        SimpleStringRedisMessage okMessage = pool.GetSimpleString(FixedRedisMessagePool.RedisReplyKey.OK);
        SimpleStringRedisMessage pongMessage = pool.GetSimpleString(FixedRedisMessagePool.RedisReplyKey.PONG);
        Assert.NotEqual(okMessage, pongMessage);
    }
}
