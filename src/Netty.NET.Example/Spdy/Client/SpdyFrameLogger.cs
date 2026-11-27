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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Spdy;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Spdy.Client;

/**
 * Logs SPDY frames for debugging purposes.
 */
// CLR: InternalLogLevel is a C# enum, a value type that cannot be null, so ObjectUtil.checkNotNull(level, "level") has
// no counterpart. InternalLogger is IInternalLogger, SpdyFrame is ISpdyFrame and Enum.name() is ToString().
// write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken), whose returned Task replaces the ChannelPromise
// (docs/codec-base-porting.md, Outbound operations).
public class SpdyFrameLogger : ChannelDuplexHandler
{
    private enum Direction
    {
        INBOUND, OUTBOUND
    }

    protected readonly IInternalLogger logger;
    private readonly InternalLogLevel level;

    public SpdyFrameLogger(InternalLogLevel level)
    {
        this.level = level;
        this.logger = InternalLoggerFactory.GetInstance(GetType());
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (AcceptMessage(msg))
        {
            Log((ISpdyFrame) msg, Direction.INBOUND);
        }
        ctx.FireChannelRead(msg);
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (AcceptMessage(msg))
        {
            Log((ISpdyFrame) msg, Direction.OUTBOUND);
        }
        return ctx.WriteAsync(msg, cancellationToken);
    }

    private static bool AcceptMessage(object msg)
    {
        return msg is ISpdyFrame;
    }

    private void Log(ISpdyFrame msg, Direction d)
    {
        if (logger.IsEnabled(level))
        {
            StringBuilder b = new StringBuilder(200)
                .Append("\n----------------")
                .Append(d.ToString())
                .Append("--------------------\n")
                .Append(msg)
                .Append("\n------------------------------------");

            logger.Log(level, b.ToString());
        }
    }
}
