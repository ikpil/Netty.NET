/*
 * Copyright 2015 The Netty Project
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

using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Socksx.V4;
using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socksx;

/**
 * Detects the version of the current SOCKS connection and initializes the pipeline with
 * {@link Socks4ServerDecoder} or {@link Socks5InitialRequestDecoder}.
 */
// CLR: List<Object> out is IList<object> @out and 'in' is @in (C# keywords); the switch over SocksVersion is an
// if/else chain (see SocksVersion.cs); ctx.close() returns a Task, discarded as the source ignores the future (as
// ChannelInitializer.ExceptionCaught in Transport). getByte returns the unsigned System.Byte (owner decision,
// docs/common-clr-design.md), so versionVal & 0xFF logs the same 0-255 value as the source.
public class SocksPortUnificationServerHandler : ByteToMessageDecoder
{

    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(SocksPortUnificationServerHandler));

    private readonly Socks5ServerEncoder socks5encoder;

    /**
     * Creates a new instance with the default configuration.
     */
    public SocksPortUnificationServerHandler() : this(Socks5ServerEncoder.DEFAULT)
    {
    }

    /**
     * Creates a new instance with the specified {@link Socks5ServerEncoder}.
     * This constructor is useful when a user wants to use an alternative {@link Socks5AddressEncoder}.
     */
    public SocksPortUnificationServerHandler(Socks5ServerEncoder socks5encoder)
    {
        this.socks5encoder = ObjectUtil.CheckNotNull(socks5encoder, "socks5encoder");
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        int readerIndex = @in.ReaderIndex;
        if (@in.WriterIndex == readerIndex)
        {
            return;
        }

        IChannelPipeline p = ctx.Pipeline;
        byte versionVal = @in.GetByte(readerIndex);
        SocksVersion version = SocksVersion.ValueOf(versionVal);

        if (version == SocksVersion.SOCKS4a)
        {
            LogKnownVersion(ctx, version);
            p.AddAfter(ctx.Name, null, Socks4ServerEncoder.INSTANCE);
            p.AddAfter(ctx.Name, null, new Socks4ServerDecoder());
        }
        else if (version == SocksVersion.SOCKS5)
        {
            LogKnownVersion(ctx, version);
            p.AddAfter(ctx.Name, null, socks5encoder);
            p.AddAfter(ctx.Name, null, new Socks5InitialRequestDecoder());
        }
        else
        {
            LogUnknownVersion(ctx, versionVal);
            @in.SkipBytes(@in.ReadableBytes);
            _ = ctx.CloseAsync();
            return;
        }

        p.Remove(this);
    }

    private static void LogKnownVersion(IChannelHandlerContext ctx, SocksVersion version)
    {
        logger.Debug("{} Protocol version: {}({})", ctx.Channel, version);
    }

    private static void LogUnknownVersion(IChannelHandlerContext ctx, byte versionVal)
    {
        if (logger.IsDebugEnabled())
        {
            logger.Debug("{} Unknown protocol version: {}", ctx.Channel, versionVal & 0xFF);
        }
    }
}
