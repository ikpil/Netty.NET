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
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Redis;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Redis;

/**
 * An example Redis client handler. This handler read input from STDIN and write output to STDOUT.
 */
// CLR: write(ctx, msg, promise) is WriteAsync, which returns the Task of ctx.WriteAsync in place of completing the
// caller's promise (docs/transport-porting.md: promise arguments are the returned Task). String.split("\\s+") is
// Regex.Split over the same pattern; Java also drops trailing empty strings, which a trimmed line never yields. The Java
// interface RedisMessage is IRedisMessage, ArrayList is List, and CharsetUtil.UTF_8 is Encoding.UTF8.
// cause.printStackTrace(System.err) writes Exception.ToString() to Console.Error.
public class RedisClientHandler : ChannelDuplexHandler
{

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        string[] commands = Regex.Split((string) msg, "\\s+");
        List<IRedisMessage> children = new List<IRedisMessage>(commands.Length);

        foreach (string cmdString in commands)
        {
            children.Add(new FullBulkStringRedisMessage(ByteBufUtil.WriteUtf8(ctx.Alloc(), cmdString)));
        }

        IRedisMessage request = new ArrayRedisMessage(children);
        return ctx.WriteAsync(request, cancellationToken);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        IRedisMessage redisMessage = (IRedisMessage) msg;
        PrintAggregatedRedisResponse(redisMessage);
        ReferenceCountUtil.Release(redisMessage);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.Write("exceptionCaught: ");
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }

    private static void PrintAggregatedRedisResponse(IRedisMessage msg)
    {
        if (msg is SimpleStringRedisMessage)
        {
            Console.WriteLine(((SimpleStringRedisMessage) msg).Content());
        }
        else if (msg is ErrorRedisMessage)
        {
            Console.WriteLine(((ErrorRedisMessage) msg).Content());
        }
        else if (msg is IntegerRedisMessage)
        {
            Console.WriteLine(((IntegerRedisMessage) msg).Value());
        }
        else if (msg is FullBulkStringRedisMessage)
        {
            Console.WriteLine(GetString((FullBulkStringRedisMessage) msg));
        }
        else if (msg is ArrayRedisMessage)
        {
            foreach (IRedisMessage child in ((ArrayRedisMessage) msg).Children())
            {
                PrintAggregatedRedisResponse(child);
            }
        }
        else
        {
            throw new CodecException("unknown message type: " + msg);
        }
    }

    private static string GetString(FullBulkStringRedisMessage msg)
    {
        if (msg.IsNull())
        {
            return "(null)";
        }

        return msg.Content.ToString(Encoding.UTF8);
    }
}
