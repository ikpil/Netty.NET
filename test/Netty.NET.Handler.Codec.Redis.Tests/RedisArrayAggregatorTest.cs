/*
 * Copyright 2026 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Redis.Tests;

// CLR: CharsetUtil.US_ASCII is Encoding.ASCII (docs/codec-base-porting.md). JUnit assertThrows passes "if the thrown
// exception type is the same as expectedType or a subtype thereof"
// (https://docs.junit.org/current/api/org.junit.jupiter.api/org/junit/jupiter/api/Assertions.html); the aggregator's
// CodecException reaches the caller wrapped in a DecoderException (a CodecException subtype) by MessageToMessageDecoder,
// so assertThrows(CodecException.class, ...) is Assert.ThrowsAny<CodecException>, which accepts derived types
// (https://raw.githubusercontent.com/xunit/assert.xunit/main/ExceptionAsserts.cs); the exact PrematureChannelClosureException
// stays Assert.Throws. assertFalse(BooleanSupplier) and the method references are lambdas or direct calls.
public class RedisArrayAggregatorTest
{

    [Fact]
    public void TestLimitNested()
    {
        byte[] arrayHeader = Encoding.ASCII.GetBytes("*1\r\n");
        int maxNestedDepth = 100;
        EmbeddedChannel channel = new EmbeddedChannel(new RedisDecoder(),
                new RedisArrayAggregator(RedisConstants.REDIS_MAX_ARRAY_LENGTH, maxNestedDepth));
        for (int i = 0; i < maxNestedDepth; i++)
        {
            Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(arrayHeader)));
        }

        // Next write should trigger an exception.
        Assert.ThrowsAny<CodecException>(() => channel.WriteInbound(Unpooled.WrappedBuffer(arrayHeader)));
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestTotalPendingElementBudgetAcrossNestedArrays()
    {
        // maxElements and maxNestedArrayDepth are independent limits. Each header below stays
        // within maxElements on its own, and the nesting never reaches maxNestedArrayDepth, but
        // the total number of elements declared across the active nested arrays would multiply
        // out to maxElements * maxNestedArrayDepth if the two limits were not also bounded jointly.
        int maxElements = 4096;
        int maxNestedArrayDepth = 4;
        EmbeddedChannel channel = new EmbeddedChannel(
                new RedisArrayAggregator(maxElements, maxNestedArrayDepth));

        Assert.False(channel.WriteInbound(new ArrayHeaderRedisMessage(maxElements)));

        // A second header of the same declared length pushes the total outstanding element count
        // to 2 * maxElements, which must be rejected even though this header, considered alone,
        // and the resulting nesting depth of 2 both stay within their respective limits.
        Assert.ThrowsAny<CodecException>(
                () => channel.WriteInbound(new ArrayHeaderRedisMessage(maxElements)));
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestPendingElementBudgetIsReclaimedOnceArrayCompletes()
    {
        int maxElements = 4096;
        int maxNestedArrayDepth = 4;
        EmbeddedChannel channel = new EmbeddedChannel(
                new RedisArrayAggregator(maxElements, maxNestedArrayDepth));

        // Complete a full array so its declared length is released from the outstanding budget.
        Assert.False(channel.WriteInbound(new ArrayHeaderRedisMessage(1)));
        FullBulkStringRedisMessage element = new FullBulkStringRedisMessage(Unpooled.Buffer());
        Assert.True(channel.WriteInbound(element));
        ((ArrayRedisMessage) channel.ReadInbound()).Release();

        // The budget should now allow a fresh header declaring up to maxElements again.
        Assert.False(channel.WriteInbound(new ArrayHeaderRedisMessage(maxElements)));

        Assert.Throws<PrematureChannelClosureException>(() => channel.Finish());
    }

    // CLR: the @Deprecated RedisArrayAggregator() is not ported (docs/codec-redis-porting.md); the test checks the
    // release on close, so it uses its replacement RedisArrayAggregator(int, int) with the arguments the deprecated
    // constructor passed, this(DEFAULT_MAX_ARRAY_LENGTH, 1024).
    [Fact]
    public void TestDoesNotLeakOnClose()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new RedisArrayAggregator(RedisConstants.REDIS_MAX_ARRAY_LENGTH, 1024));
        Assert.False(ch.WriteInbound(new ArrayHeaderRedisMessage(2)));

        FullBulkStringRedisMessage redisMessage = new FullBulkStringRedisMessage(Unpooled.Buffer());
        Assert.Equal(1, redisMessage.RefCnt);
        Assert.False(ch.WriteInbound(redisMessage));
        Assert.Equal(1, redisMessage.RefCnt);

        Assert.Throws<PrematureChannelClosureException>(() => ch.Finish());
        Assert.Equal(0, redisMessage.RefCnt);
    }

    // CLR: as in testDoesNotLeakOnClose, the replacement RedisArrayAggregator(int, int) takes the deprecated
    // constructor's arguments; pipeline().remove(Class) is the generic Pipeline.Remove<T>().
    [Fact]
    public void TestDoesNotLeakOnRemoval()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new RedisArrayAggregator(RedisConstants.REDIS_MAX_ARRAY_LENGTH, 1024));
        Assert.False(ch.WriteInbound(new ArrayHeaderRedisMessage(2)));

        FullBulkStringRedisMessage redisMessage = new FullBulkStringRedisMessage(Unpooled.Buffer());
        Assert.Equal(1, redisMessage.RefCnt);
        Assert.False(ch.WriteInbound(redisMessage));
        Assert.Equal(1, redisMessage.RefCnt);
        ch.Pipeline.Remove<RedisArrayAggregator>();
        Assert.Equal(0, redisMessage.RefCnt);
        Assert.False(ch.Finish());
    }
}
