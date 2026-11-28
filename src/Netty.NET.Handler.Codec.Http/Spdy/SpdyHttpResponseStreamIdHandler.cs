/*
 * Copyright 2012 The Netty Project
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

using System;
using System.Collections.Generic;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * {@link MessageToMessageCodec} that takes care of adding the right {@link SpdyHttpHeaders.Names#STREAM_ID} to the
 * {@link HttpMessage} if one is not present. This makes it possible to just re-use plan handlers current used
 * for HTTP.
 */
// CLR: MessageToMessageCodec<Object, HttpMessage> is the codec-base MessageToMessageCodec<object, IHttpMessage> with
// typeof(...) matchers. The ArrayDeque<Integer> queue is a LinkedList<int>, because remove(Object) removes the first
// occurrence of a value from the middle of the queue: LinkedList.Remove(T) "Removes the first occurrence of the
// specified value"
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.linkedlist-1.remove?view=net-10.0); poll()
// returns the first value or null when the queue is empty
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/ArrayDeque.html). ArrayDeque rejects a null
// element with NullPointerException, which the unboxed add of a null getInt(...) result here keeps as
// ArgumentNullException (the CheckNotNull mapping, docs/codec-base-porting.md). headers().getInt(name) returns int? as
// the Java Integer.
public class SpdyHttpResponseStreamIdHandler : MessageToMessageCodec<object, IHttpMessage>
{
    private const int NO_ID = -1;
    private readonly LinkedList<int> ids = new LinkedList<int>();

    public SpdyHttpResponseStreamIdHandler() : base(typeof(object), typeof(IHttpMessage))
    {
    }

    public override bool AcceptInboundMessage(object msg)
    {
        return msg is IHttpMessage || msg is ISpdyRstStreamFrame;
    }

    protected override void Encode(IChannelHandlerContext ctx, IHttpMessage msg, IList<object> @out)
    {
        int? id = Poll();
        if (id != null && id.Value != NO_ID && !msg.Headers().Contains(SpdyHttpHeaders.Names.STREAM_ID))
        {
            msg.Headers().SetInt(SpdyHttpHeaders.Names.STREAM_ID, id.Value);
        }

        @out.Add(ReferenceCountUtil.Retain(msg));
    }

    protected override void Decode(IChannelHandlerContext ctx, object msg, IList<object> @out)
    {
        if (msg is IHttpMessage)
        {
            bool contains = ((IHttpMessage) msg).Headers().Contains(SpdyHttpHeaders.Names.STREAM_ID);
            if (!contains)
            {
                ids.AddLast(NO_ID);
            }
            else
            {
                int? id = ((IHttpMessage) msg).Headers().GetInt(SpdyHttpHeaders.Names.STREAM_ID);
                ids.AddLast(id ?? throw new ArgumentNullException("e"));
            }
        }
        else if (msg is ISpdyRstStreamFrame)
        {
            ids.Remove(((ISpdyRstStreamFrame) msg).StreamId());
        }

        @out.Add(ReferenceCountUtil.Retain(msg));
    }

    private int? Poll()
    {
        if (ids.First == null)
        {
            return null;
        }

        int id = ids.First.Value;
        ids.RemoveFirst();
        return id;
    }
}
