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
using Netty.NET.Buffer;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Logs HTTP2 frames for debugging purposes.
 */
// CLR: ChannelHandlerAdapter is the Transport ChannelHandlerAdapter; LogLevel is handler's Netty.NET.Handler.Logging
// LogLevel and InternalLogLevel/InternalLogger the Common logging types; Class<?> is Type. direction.name() is
// Direction.ToString() (the constant name of the C# enum). The overridable Java methods are virtual. frameType & 0xFF
// masks the signed Java byte to its unsigned value; the C# frameType is already the unsigned System.Byte, so the mask
// gives the same value. The logger's MessageFormatter prints booleans as true/false
// (src/Netty.NET.Common/Internal/Logging/MessageFormatter.cs).
public class Http2FrameLogger : ChannelHandlerAdapter
{

    public enum Direction
    {
        INBOUND,
        OUTBOUND
    }

    private const int BUFFER_LENGTH_THRESHOLD = 64;
    private readonly IInternalLogger logger;
    private readonly InternalLogLevel level;

    public Http2FrameLogger(LogLevel level)
        : this(CheckAndConvertLevel(level), InternalLoggerFactory.GetInstance(typeof(Http2FrameLogger)))
    {
    }

    public Http2FrameLogger(LogLevel level, string name)
        : this(CheckAndConvertLevel(level), InternalLoggerFactory.GetInstance(CheckNotNull(name, "name")))
    {
    }

    public Http2FrameLogger(LogLevel level, Type clazz)
        : this(CheckAndConvertLevel(level), InternalLoggerFactory.GetInstance(CheckNotNull(clazz, "clazz")))
    {
    }

    private Http2FrameLogger(InternalLogLevel level, IInternalLogger logger)
    {
        this.level = level;
        this.logger = logger;
    }

    private static InternalLogLevel CheckAndConvertLevel(LogLevel level)
    {
        return CheckNotNull(level, "level").ToInternalLevel();
    }

    public virtual bool IsEnabled()
    {
        return logger.IsEnabled(level);
    }

    public virtual void LogData(Direction direction, IChannelHandlerContext ctx, int streamId, ByteBuf data,
            int padding,
            bool endStream)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} DATA: streamId={} padding={} endStream={} length={} bytes={}", ctx.Channel,
                    direction.ToString(), streamId, padding, endStream, data.ReadableBytes, ToString(data));
        }
    }

    public virtual void LogHeaders(Direction direction, IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
            int padding, bool endStream)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} HEADERS: streamId={} headers={} padding={} endStream={}", ctx.Channel,
                    direction.ToString(), streamId, headers, padding, endStream);
        }
    }

    public virtual void LogHeaders(Direction direction, IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
            int streamDependency, short weight, bool exclusive, int padding, bool endStream)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} HEADERS: streamId={} headers={} streamDependency={} weight={} exclusive={} " +
                    "padding={} endStream={}", ctx.Channel,
                    direction.ToString(), streamId, headers, streamDependency, weight, exclusive, padding, endStream);
        }
    }

    public virtual void LogPriority(Direction direction, IChannelHandlerContext ctx, int streamId, int streamDependency,
            short weight, bool exclusive)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} PRIORITY: streamId={} streamDependency={} weight={} exclusive={}", ctx.Channel,
                    direction.ToString(), streamId, streamDependency, weight, exclusive);
        }
    }

    public virtual void LogRstStream(Direction direction, IChannelHandlerContext ctx, int streamId, long errorCode)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} RST_STREAM: streamId={} errorCode={}", ctx.Channel,
                    direction.ToString(), streamId, errorCode);
        }
    }

    public virtual void LogSettingsAck(Direction direction, IChannelHandlerContext ctx)
    {
        logger.Log(level, "{} {} SETTINGS: ack=true", ctx.Channel, direction.ToString());
    }

    public virtual void LogSettings(Direction direction, IChannelHandlerContext ctx, Http2Settings settings)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} SETTINGS: ack=false settings={}", ctx.Channel, direction.ToString(), settings);
        }
    }

    public virtual void LogPing(Direction direction, IChannelHandlerContext ctx, long data)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} PING: ack=false bytes={}", ctx.Channel,
                    direction.ToString(), data);
        }
    }

    public virtual void LogPingAck(Direction direction, IChannelHandlerContext ctx, long data)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} PING: ack=true bytes={}", ctx.Channel,
                    direction.ToString(), data);
        }
    }

    public virtual void LogPushPromise(Direction direction, IChannelHandlerContext ctx, int streamId,
            int promisedStreamId,
            IHttp2Headers headers, int padding)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} PUSH_PROMISE: streamId={} promisedStreamId={} headers={} padding={}",
                    ctx.Channel, direction.ToString(), streamId, promisedStreamId, headers, padding);
        }
    }

    public virtual void LogGoAway(Direction direction, IChannelHandlerContext ctx, int lastStreamId, long errorCode,
            ByteBuf debugData)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} GO_AWAY: lastStreamId={} errorCode={} length={} bytes={}", ctx.Channel,
                    direction.ToString(), lastStreamId, errorCode, debugData.ReadableBytes, ToString(debugData));
        }
    }

    public virtual void LogWindowsUpdate(Direction direction, IChannelHandlerContext ctx, int streamId,
            int windowSizeIncrement)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} WINDOW_UPDATE: streamId={} windowSizeIncrement={}", ctx.Channel,
                    direction.ToString(), streamId, windowSizeIncrement);
        }
    }

    public virtual void LogUnknownFrame(Direction direction, IChannelHandlerContext ctx, byte frameType, int streamId,
            Http2Flags flags, ByteBuf data)
    {
        if (IsEnabled())
        {
            logger.Log(level, "{} {} UNKNOWN: frameType={} streamId={} flags={} length={} bytes={}", ctx.Channel,
                    direction.ToString(), frameType & 0xFF, streamId, flags.Value(), data.ReadableBytes,
                    ToString(data));
        }
    }

    private string ToString(ByteBuf buf)
    {
        if (level == InternalLogLevel.TRACE || buf.ReadableBytes <= BUFFER_LENGTH_THRESHOLD)
        {
            // Log the entire buffer.
            return ByteBufUtil.HexDump(buf);
        }

        // Otherwise just log the first 64 bytes.
        int length = Math.Min(buf.ReadableBytes, BUFFER_LENGTH_THRESHOLD);
        return ByteBufUtil.HexDump(buf, buf.ReaderIndex, length) + "...";
    }
}
