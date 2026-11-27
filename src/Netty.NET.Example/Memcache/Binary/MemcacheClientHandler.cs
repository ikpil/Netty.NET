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
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Memcache.Binary;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Memcache.Binary;

// CLR: write(ctx, msg, promise) is WriteAsync, which returns the Task of ctx.WriteAsync in place of completing the
// caller's promise (docs/transport-porting.md: promise arguments are the returned Task); the pipeline turns an exception
// thrown by WriteAsync into a failed Task, as Java fails the promise. The Java interfaces BinaryMemcacheRequest and
// FullBinaryMemcacheResponse are IBinaryMemcacheRequest and IFullBinaryMemcacheResponse. String.getBytes(UTF_8) is
// Encoding.UTF8.GetBytes, startsWith is an ordinal StartsWith, split(" ", 3) is Split(' ', 3) (at most three parts),
// IllegalArgumentException is ArgumentException and IllegalStateException InvalidOperationException.
// cause.printStackTrace() writes Exception.ToString() to Console.Error.
public class MemcacheClientHandler : ChannelDuplexHandler
{

    /**
     * Transforms basic string requests to binary memcache requests
     */
    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        string command = (string) msg;
        if (command.StartsWith("get ", StringComparison.Ordinal))
        {
            string keyString = command.Substring("get ".Length);
            ByteBuf key = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(keyString));

            IBinaryMemcacheRequest req = new DefaultBinaryMemcacheRequest(key);
            req.SetOpcode(BinaryMemcacheOpcodes.GET);

            return ctx.WriteAsync(req, cancellationToken);
        }
        else if (command.StartsWith("set ", StringComparison.Ordinal))
        {
            string[] parts = command.Split(' ', 3);
            if (parts.Length < 3)
            {
                throw new ArgumentException("Malformed Command: " + command);
            }
            string keyString = parts[1];
            string value = parts[2];

            ByteBuf key = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(keyString));
            ByteBuf content = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(value));
            ByteBuf extras = ctx.Alloc().Buffer(8);
            extras.WriteZero(8);

            IBinaryMemcacheRequest req = new DefaultFullBinaryMemcacheRequest(key, extras, content);
            req.SetOpcode(BinaryMemcacheOpcodes.SET);

            return ctx.WriteAsync(req, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("Unknown Message: " + msg);
        }
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        IFullBinaryMemcacheResponse res = (IFullBinaryMemcacheResponse) msg;
        Console.WriteLine(res.Content.ToString(Encoding.UTF8));
        res.Release();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
