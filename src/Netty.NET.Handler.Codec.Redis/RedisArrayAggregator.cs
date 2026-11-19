/*
 * Copyright 2016 The Netty Project
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

using System;
using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * Aggregates {@link RedisMessage} parts into {@link ArrayRedisMessage}. This decoder
 * should be used together with {@link RedisDecoder}.
 */
// CLR: the @Deprecated no-argument constructor ("Use RedisArrayAggregator(int, int) instead") is not ported (owner
// decision 2026-11-16, docs/codec-redis-porting.md), and with it the private DEFAULT_MAX_ARRAY_LENGTH that only it used.
// RedisMessage.class is typeof(IRedisMessage) (docs/codec-base-porting.md, Type matching). The ArrayDeque used as a
// stack (push/pop/peek at its head) is Stack<T>, a last-in-first-out collection
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/ArrayDeque.html,
// https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.stack-1); the order in which
// releaseAndClearDepths() releases the states does not matter. Numbers in the messages use the invariant culture
// (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
[UnstableApi]
public sealed class RedisArrayAggregator : MessageToMessageDecoder<IRedisMessage>
{

    // Bound the eagerly reserved backing capacity of an aggregate's child list, regardless of the
    // length declared in the array header. The list can still grow to the declared length as
    // elements actually arrive.
    private const int INITIAL_CHILDREN_CAPACITY = 32;

    private readonly int maxNestedArrayDepth;
    private readonly Stack<AggregateState> depths = new Stack<AggregateState>(4);
    private readonly int maxElements;

    // Sum of the declared lengths of all currently active (nested) aggregate states. Bounding this
    // total, rather than each header individually, prevents an attacker from multiplying the
    // per-header element cap by the nesting depth cap to reserve far more backing capacity than
    // maxElements alone would suggest.
    private long pendingElements;

    /**
     * Create a new instance that will aggregate an {@link ArrayHeaderRedisMessage}
     * and its subsequent elements into an {@link ArrayRedisMessage}.
     * <p>
     * A {@link CodecException} will be thrown if the array header specify a length greater than
     * the given number of max elements.
     * @param maxElements The maximum number of elements to aggregate in a single message. This applies cumulatively.
     * @param maxNestedArrayDepth   the maximum depth of the nested array before an exception will be thrown
     */
    public RedisArrayAggregator(int maxElements, int maxNestedArrayDepth) : base(typeof(IRedisMessage))
    {
        this.maxElements = ObjectUtil.CheckPositive(maxElements, "maxElements");
        this.maxNestedArrayDepth = ObjectUtil.CheckPositive(maxNestedArrayDepth, "maxNestedArrayDepth");
    }

    protected override void Decode(IChannelHandlerContext ctx, IRedisMessage msg, IList<object> @out)
    {
        if (msg is ArrayHeaderRedisMessage)
        {
            msg = DecodeRedisArrayHeader((ArrayHeaderRedisMessage) msg);
            if (msg == null)
            {
                return;
            }
        }
        else
        {
            ReferenceCountUtil.Retain(msg);
        }

        while (depths.Count != 0)
        {
            AggregateState current = depths.Peek();
            current.children.Add(msg);

            // if current aggregation completed, go to parent aggregation.
            if (current.children.Count == current.length)
            {
                msg = new ArrayRedisMessage(current.children);
                depths.Pop();
                pendingElements -= current.length;
            }
            else
            {
                // not aggregated yet. try next time.
                return;
            }
        }

        @out.Add(msg);
    }

    private CodecException ClearAndCreateException(string msg)
    {
        ReleaseAndClearDepths();
        return new CodecException(msg);
    }

    private IRedisMessage DecodeRedisArrayHeader(ArrayHeaderRedisMessage header)
    {
        if (header.IsNull())
        {
            return ArrayRedisMessage.NULL_INSTANCE;
        }
        else if (header.Length() == 0L)
        {
            return ArrayRedisMessage.EMPTY_INSTANCE;
        }
        else if (header.Length() > 0L)
        {
            // Currently, this codec doesn't support `long` length for arrays because Java's List.size() is int.
            if (header.Length() > maxElements)
            {
                throw ClearAndCreateException("this codec doesn't support longer length than " +
                        maxElements.ToString(CultureInfo.InvariantCulture));
            }

            if (depths.Count >= maxNestedArrayDepth)
            {
                throw ClearAndCreateException("max nested array depth exceeded: "  +
                        maxNestedArrayDepth.ToString(CultureInfo.InvariantCulture));
            }

            // Bound the total number of elements declared across all currently active (nested)
            // aggregate states, not just the length of this one header. Without this, a header's
            // declared length could be repeated at every nesting level, letting maxElements and
            // maxNestedArrayDepth multiply into a far larger reserved capacity than either limit
            // implies on its own.
            long newPendingElements = pendingElements + header.Length();
            if (newPendingElements > maxElements)
            {
                throw ClearAndCreateException(
                        "total outstanding array elements exceeds " + maxElements.ToString(CultureInfo.InvariantCulture));
            }
            pendingElements = newPendingElements;

            // start aggregating array
            depths.Push(new AggregateState((int) header.Length()));
            return null;
        }
        else
        {
            throw ClearAndCreateException("bad length: " + header.Length().ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class AggregateState
    {
        internal readonly int length;
        internal readonly List<IRedisMessage> children;
        internal AggregateState(int length)
        {
            this.length = length;
            this.children = new List<IRedisMessage>(Math.Min(length, INITIAL_CHILDREN_CAPACITY));
        }
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        base.HandlerRemoved(ctx);
        ReleaseAndClearDepths();
    }

    private void ReleaseAndClearDepths()
    {
        foreach (AggregateState state in depths)
        {
            foreach (IRedisMessage message in state.children)
            {
                ReferenceCountUtil.SafeRelease(message);
            }
        }
        depths.Clear();
        pendingElements = 0;
    }

    // CLR: the depth count in the message uses the invariant culture
    // (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        base.ChannelInactive(ctx);
        if (depths.Count != 0)
        {
            ctx.FireExceptionCaught(new PrematureChannelClosureException(
                    "channel gone inactive with " + depths.Count.ToString(CultureInfo.InvariantCulture) +
                            " messages still incomplete"));
        }
    }
}
