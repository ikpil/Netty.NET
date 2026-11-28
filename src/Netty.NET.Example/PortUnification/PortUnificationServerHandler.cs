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
using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Example.Factorial;
using Netty.NET.Example.Http.Snoop;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Compression;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.PortUnification;

/**
 * Manipulates the current pipeline dynamically to switch protocols or enable
 * SSL or GZIP.
 */
// CLR: List<Object> out is IList<object> and getUnsignedByte(index) is GetByte(index), which returns the unsigned C#
// byte (docs/buffer-porting.md); readerIndex() and readableBytes() are the ReaderIndex and ReadableBytes properties.
// ChannelPipeline is IChannelPipeline and ctx.close() is CloseAsync(), whose Task is discarded as Java discards the
// future. CompressionOptions is the marker interface ICompressionOptions (codec-compression).
public class PortUnificationServerHandler : ByteToMessageDecoder
{
    private const int MAX_CONTENT_LENGTH = 65536;

    private readonly SslContext sslCtx;
    private readonly bool detectSsl;
    private readonly bool detectGzip;

    public PortUnificationServerHandler(SslContext sslCtx) : this(sslCtx, true, true)
    {
    }

    private PortUnificationServerHandler(SslContext sslCtx, bool detectSsl, bool detectGzip)
    {
        this.sslCtx = sslCtx;
        this.detectSsl = detectSsl;
        this.detectGzip = detectGzip;
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        // Will use the first five bytes to detect a protocol.
        if (@in.ReadableBytes < 5)
        {
            return;
        }

        if (IsSsl(@in))
        {
            EnableSsl(ctx);
        }
        else
        {
            int magic1 = @in.GetByte(@in.ReaderIndex);
            int magic2 = @in.GetByte(@in.ReaderIndex + 1);
            if (IsGzip(magic1, magic2))
            {
                EnableGzip(ctx);
            }
            else if (IsHttp(magic1, magic2))
            {
                SwitchToHttp(ctx);
            }
            else if (IsFactorial(magic1))
            {
                SwitchToFactorial(ctx);
            }
            else
            {
                // Unknown protocol; discard everything and close the connection.
                @in.Clear();
                _ = ctx.CloseAsync();
            }
        }
    }

    private bool IsSsl(ByteBuf buf)
    {
        if (detectSsl)
        {
            return SslHandler.IsEncrypted(buf, false);
        }

        return false;
    }

    private bool IsGzip(int magic1, int magic2)
    {
        if (detectGzip)
        {
            return magic1 == 31 && magic2 == 139;
        }

        return false;
    }

    private static bool IsHttp(int magic1, int magic2)
    {
        return
            magic1 == 'G' && magic2 == 'E' || // GET
            magic1 == 'P' && magic2 == 'O' || // POST
            magic1 == 'P' && magic2 == 'U' || // PUT
            magic1 == 'H' && magic2 == 'E' || // HEAD
            magic1 == 'O' && magic2 == 'P' || // OPTIONS
            magic1 == 'P' && magic2 == 'A' || // PATCH
            magic1 == 'D' && magic2 == 'E' || // DELETE
            magic1 == 'T' && magic2 == 'R' || // TRACE
            magic1 == 'C' && magic2 == 'O';   // CONNECT
    }

    private static bool IsFactorial(int magic1)
    {
        return magic1 == 'F';
    }

    private void EnableSsl(IChannelHandlerContext ctx)
    {
        IChannelPipeline p = ctx.Pipeline;
        p.AddLast("ssl", sslCtx.NewHandler(ctx.Alloc()));
        p.AddLast("unificationA", new PortUnificationServerHandler(sslCtx, false, detectGzip));
        p.Remove(this);
    }

    private void EnableGzip(IChannelHandlerContext ctx)
    {
        IChannelPipeline p = ctx.Pipeline;
        p.AddLast("gzipdeflater", ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.GZIP));
        p.AddLast("gzipinflater", ZlibCodecFactory.NewZlibDecoder(ZlibWrapper.GZIP, MAX_CONTENT_LENGTH));
        p.AddLast("unificationB", new PortUnificationServerHandler(sslCtx, detectSsl, false));
        p.Remove(this);
    }

    private void SwitchToHttp(IChannelHandlerContext ctx)
    {
        IChannelPipeline p = ctx.Pipeline;
        p.AddLast("decoder", new HttpRequestDecoder());
        p.AddLast("encoder", new HttpResponseEncoder());
        p.AddLast("deflater", new HttpContentCompressor((ICompressionOptions[]) null));
        p.AddLast("handler", new HttpSnoopServerHandler());
        p.Remove(this);
    }

    private void SwitchToFactorial(IChannelHandlerContext ctx)
    {
        IChannelPipeline p = ctx.Pipeline;
        p.AddLast("decoder", new BigIntegerDecoder());
        p.AddLast("encoder", new NumberEncoder());
        p.AddLast("handler", new FactorialServerHandler());
        p.Remove(this);
    }
}
