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
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Logging;

/**
 * A {@link ChannelHandler} that logs all events using a logging framework.
 * By default, all events are logged at <tt>DEBUG</tt> level and full hex dumps are recorded for ByteBufs.
 */
// CLR: InternalLogger is Common's IInternalLogger from InternalLoggerFactory (docs/common-clr-design.md);
// Class<?> is Type and getClass() is GetType(), whose logger name is Type.FullName (InternalLoggerFactory.GetInstance).
// The outbound operations are the Task-returning BindAsync/ConnectAsync/DisconnectAsync/CloseAsync/DeregisterAsync/
// WriteAsync, whose returned Task replaces the promise (docs/codec-base-porting.md, Outbound operations). byteBufFormat()
// is the method ByteBufFormat(), so inside this class the simple name ByteBufFormat in an expression binds to that
// method group, and the enum constants are written Logging.ByteBufFormat.X (a type name in a declaration still binds to
// the enum). Numbers in the messages use the invariant culture, because Java decimal text does not depend on a culture
// while Int32.ToString() formats "by using the NumberFormatInfo object of the current culture"
// (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring?view=net-10.0).
[Sharable]
public class LoggingHandler : ChannelDuplexHandler
{
    private static readonly LogLevel DEFAULT_LEVEL = LogLevel.DEBUG;

    protected readonly IInternalLogger logger;
    protected readonly InternalLogLevel internalLevel;

    private readonly LogLevel level;
    private readonly ByteBufFormat byteBufFormat;

    /**
     * Creates a new instance whose logger name is the fully qualified class
     * name of the instance with hex dump enabled.
     */
    public LoggingHandler() : this(DEFAULT_LEVEL)
    {
    }
    /**
     * Creates a new instance whose logger name is the fully qualified class
     * name of the instance.
     *
     * @param format Format of ByteBuf dumping
     */
    public LoggingHandler(ByteBufFormat format) : this(DEFAULT_LEVEL, format)
    {
    }

    /**
     * Creates a new instance whose logger name is the fully qualified class
     * name of the instance.
     *
     * @param level the log level
     */
    public LoggingHandler(LogLevel level) : this(level, Logging.ByteBufFormat.HEX_DUMP)
    {
    }

    /**
     * Creates a new instance whose logger name is the fully qualified class
     * name of the instance.
     *
     * @param level the log level
     * @param byteBufFormat the ByteBuf format
     */
    // CLR: ByteBufFormat is a C# enum and cannot be null, so checkNotNull(byteBufFormat, "byteBufFormat") has no
    // counterpart in the constructors (see ByteBufFormat.cs).
    public LoggingHandler(LogLevel level, ByteBufFormat byteBufFormat)
    {
        this.level = ObjectUtil.CheckNotNull(level, "level");
        this.byteBufFormat = byteBufFormat;
        logger = InternalLoggerFactory.GetInstance(GetType());
        internalLevel = level.ToInternalLevel();
    }

    /**
     * Creates a new instance with the specified logger name and with hex dump
     * enabled.
     *
     * @param clazz the class type to generate the logger for
     */
    public LoggingHandler(Type clazz) : this(clazz, DEFAULT_LEVEL)
    {
    }

    /**
     * Creates a new instance with the specified logger name.
     *
     * @param clazz the class type to generate the logger for
     * @param level the log level
     */
    public LoggingHandler(Type clazz, LogLevel level) : this(clazz, level, Logging.ByteBufFormat.HEX_DUMP)
    {
    }

    /**
     * Creates a new instance with the specified logger name.
     *
     * @param clazz the class type to generate the logger for
     * @param level the log level
     * @param byteBufFormat the ByteBuf format
     */
    public LoggingHandler(Type clazz, LogLevel level, ByteBufFormat byteBufFormat)
    {
        ObjectUtil.CheckNotNull(clazz, "clazz");
        this.level = ObjectUtil.CheckNotNull(level, "level");
        this.byteBufFormat = byteBufFormat;
        logger = InternalLoggerFactory.GetInstance(clazz);
        internalLevel = level.ToInternalLevel();
    }

    /**
     * Creates a new instance with the specified logger name using the default log level.
     *
     * @param name the name of the class to use for the logger
     */
    public LoggingHandler(string name) : this(name, DEFAULT_LEVEL)
    {
    }

    /**
     * Creates a new instance with the specified logger name.
     *
     * @param name the name of the class to use for the logger
     * @param level the log level
     */
    public LoggingHandler(string name, LogLevel level) : this(name, level, Logging.ByteBufFormat.HEX_DUMP)
    {
    }

    /**
     * Creates a new instance with the specified logger name.
     *
     * @param name the name of the class to use for the logger
     * @param level the log level
     * @param byteBufFormat the ByteBuf format
     */
    public LoggingHandler(string name, LogLevel level, ByteBufFormat byteBufFormat)
    {
        ObjectUtil.CheckNotNull(name, "name");

        this.level = ObjectUtil.CheckNotNull(level, "level");
        this.byteBufFormat = byteBufFormat;
        logger = InternalLoggerFactory.GetInstance(name);
        internalLevel = level.ToInternalLevel();
    }

    /**
     * Returns the {@link LogLevel} that this handler uses to log
     */
    public LogLevel Level()
    {
        return level;
    }

    /**
     * Returns the {@link ByteBufFormat} that this handler uses to log
     */
    public ByteBufFormat ByteBufFormat()
    {
        return byteBufFormat;
    }

    public override void ChannelRegistered(IChannelHandlerContext ctx)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "REGISTERED"));
        }
        ctx.FireChannelRegistered();
    }

    public override void ChannelUnregistered(IChannelHandlerContext ctx)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "UNREGISTERED"));
        }
        ctx.FireChannelUnregistered();
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "ACTIVE"));
        }
        ctx.FireChannelActive();
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "INACTIVE"));
        }
        ctx.FireChannelInactive();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "EXCEPTION", cause), cause);
        }
        ctx.FireExceptionCaught(cause);
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "USER_EVENT", evt));
        }
        ctx.FireUserEventTriggered(evt);
    }

    public override Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "BIND", localAddress));
        }
        return ctx.BindAsync(localAddress, cancellationToken);
    }

    public override Task ConnectAsync(
            IChannelHandlerContext ctx,
            EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "CONNECT", remoteAddress, localAddress));
        }
        return ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    public override Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "DISCONNECT"));
        }
        return ctx.DisconnectAsync(cancellationToken);
    }

    public override Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "CLOSE"));
        }
        return ctx.CloseAsync(cancellationToken);
    }

    public override Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "DEREGISTER"));
        }
        return ctx.DeregisterAsync(cancellationToken);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "READ COMPLETE"));
        }
        ctx.FireChannelReadComplete();
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "READ", msg));
        }
        ctx.FireChannelRead(msg);
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "WRITE", msg));
        }
        return ctx.WriteAsync(msg, cancellationToken);
    }

    public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "WRITABILITY CHANGED"));
        }
        ctx.FireChannelWritabilityChanged();
    }

    public override void Flush(IChannelHandlerContext ctx)
    {
        if (logger.IsEnabled(internalLevel))
        {
            logger.Log(internalLevel, Format(ctx, "FLUSH"));
        }
        ctx.Flush();
    }

    /**
     * Formats an event and returns the formatted message.
     *
     * @param eventName the name of the event
     */
    protected virtual string Format(IChannelHandlerContext ctx, string eventName)
    {
        string chStr = ctx.Channel.ToString();
        return new StringBuilder(chStr.Length + 1 + eventName.Length)
            .Append(chStr)
            .Append(' ')
            .Append(eventName)
            .ToString();
    }

    /**
     * Formats an event and returns the formatted message.
     *
     * @param eventName the name of the event
     * @param arg       the argument of the event
     */
    // CLR: ByteBufHolder is IByteBufHolder (docs/buffer-porting.md).
    protected virtual string Format(IChannelHandlerContext ctx, string eventName, object arg)
    {
        if (arg is ByteBuf)
        {
            return FormatByteBuf(ctx, eventName, (ByteBuf) arg);
        }
        else if (arg is IByteBufHolder)
        {
            return FormatByteBufHolder(ctx, eventName, (IByteBufHolder) arg);
        }
        else
        {
            return FormatSimple(ctx, eventName, arg);
        }
    }

    /**
     * Formats an event and returns the formatted message.  This method is currently only used for formatting
     * {@link ChannelOutboundHandler#connect(ChannelHandlerContext, SocketAddress, SocketAddress, ChannelPromise)}.
     *
     * @param eventName the name of the event
     * @param firstArg  the first argument of the event
     * @param secondArg the second argument of the event
     */
    // CLR: String.valueOf(firstArg) renders null as "null". A SocketAddress argument prints EndPoint.ToString(), which
    // is "192.168.1.2:80" for an IPEndPoint
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipendpoint.tostring?view=net-10.0), while Java's
    // InetSocketAddress.toString() prints InetAddress.toString() (host name, '/', literal) and ':' and the port
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetSocketAddress.html), as recorded for
    // codec-dns (docs/codec-dns-porting.md).
    protected virtual string Format(IChannelHandlerContext ctx, string eventName, object firstArg, object secondArg)
    {
        if (secondArg == null)
        {
            return FormatSimple(ctx, eventName, firstArg);
        }

        string chStr = ctx.Channel.ToString();
        string arg1Str = firstArg?.ToString() ?? "null";
        string arg2Str = secondArg.ToString();
        StringBuilder buf = new StringBuilder(
                chStr.Length + 1 + eventName.Length + 2 + arg1Str.Length + 2 + arg2Str.Length);
        buf.Append(chStr).Append(' ').Append(eventName).Append(": ").Append(arg1Str).Append(", ").Append(arg2Str);
        return buf.ToString();
    }

    /**
     * Generates the default log message of the specified event whose argument is a {@link ByteBuf}.
     */
    private string FormatByteBuf(IChannelHandlerContext ctx, string eventName, ByteBuf msg)
    {
        string chStr = ctx.Channel.ToString();
        int length = msg.ReadableBytes;
        if (length == 0)
        {
            StringBuilder buf = new StringBuilder(chStr.Length + 1 + eventName.Length + 4);
            buf.Append(chStr).Append(' ').Append(eventName).Append(": 0B");
            return buf.ToString();
        }
        else
        {
            int outputLength = chStr.Length + 1 + eventName.Length + 2 + 10 + 1;
            if (byteBufFormat == Logging.ByteBufFormat.HEX_DUMP)
            {
                int rows = length / 16 + (length % 15 == 0? 0 : 1) + 4;
                int hexDumpLength = 2 + rows * 80;
                outputLength += hexDumpLength;
            }
            StringBuilder buf = new StringBuilder(outputLength);
            buf.Append(chStr).Append(' ').Append(eventName).Append(": ")
               .Append(length.ToString(CultureInfo.InvariantCulture)).Append('B');
            if (byteBufFormat == Logging.ByteBufFormat.HEX_DUMP)
            {
                buf.Append(StringUtil.NEWLINE);
                ByteBufUtil.AppendPrettyHexDump(buf, msg);
            }

            return buf.ToString();
        }
    }

    /**
     * Generates the default log message of the specified event whose argument is a {@link ByteBufHolder}.
     */
    // CLR: content() is the IByteBufHolder Content property (docs/buffer-porting.md).
    private string FormatByteBufHolder(IChannelHandlerContext ctx, string eventName, IByteBufHolder msg)
    {
        string chStr = ctx.Channel.ToString();
        string msgStr = msg.ToString();
        ByteBuf content = msg.Content;
        int length = content.ReadableBytes;
        if (length == 0)
        {
            StringBuilder buf = new StringBuilder(chStr.Length + 1 + eventName.Length + 2 + msgStr.Length + 4);
            buf.Append(chStr).Append(' ').Append(eventName).Append(", ").Append(msgStr).Append(", 0B");
            return buf.ToString();
        }
        else
        {
            int outputLength = chStr.Length + 1 + eventName.Length + 2 + msgStr.Length + 2 + 10 + 1;
            if (byteBufFormat == Logging.ByteBufFormat.HEX_DUMP)
            {
                int rows = length / 16 + (length % 15 == 0? 0 : 1) + 4;
                int hexDumpLength = 2 + rows * 80;
                outputLength += hexDumpLength;
            }
            StringBuilder buf = new StringBuilder(outputLength);
            buf.Append(chStr).Append(' ').Append(eventName).Append(": ")
               .Append(msgStr).Append(", ").Append(length.ToString(CultureInfo.InvariantCulture)).Append('B');
            if (byteBufFormat == Logging.ByteBufFormat.HEX_DUMP)
            {
                buf.Append(StringUtil.NEWLINE);
                ByteBufUtil.AppendPrettyHexDump(buf, content);
            }

            return buf.ToString();
        }
    }

    /**
     * Generates the default log message of the specified event whose argument is an arbitrary object.
     */
    // CLR: String.valueOf(msg) renders null as "null" and otherwise calls toString(). For a Throwable (the cause of
    // exceptionCaught) toString() is "the name of the class of this object", ": " and the message
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Throwable.html#toString()), while
    // Exception.ToString() also appends the inner exception and the stack trace
    // (https://learn.microsoft.com/en-us/dotnet/api/system.exception.tostring?view=net-10.0), so an Exception is
    // rendered with the Java text through Common's ThrowableUtil.ThrowableToString (Exception.Message is never null, so
    // ": " is always present).
    private static string FormatSimple(IChannelHandlerContext ctx, string eventName, object msg)
    {
        string chStr = ctx.Channel.ToString();
        string msgStr = msg is Exception cause
                ? ThrowableUtil.ThrowableToString(cause)
                : msg?.ToString() ?? "null";
        StringBuilder buf = new StringBuilder(chStr.Length + 1 + eventName.Length + 2 + msgStr.Length);
        return buf.Append(chStr).Append(' ').Append(eventName).Append(": ").Append(msgStr).ToString();
    }
}
