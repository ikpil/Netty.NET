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

using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Handler.Codec.Compression;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.HttpHeaderValues;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Decompresses an {@link HttpMessage} and an {@link HttpContent} compressed in
 * {@code gzip} or {@code deflate} encoding.  For more information on how this
 * handler modifies the message, please refer to {@link HttpContentDecoder}.
 */
// CLR: the two @Deprecated constructors whose @deprecated texts name HttpContentDecompressor(int) and
// HttpContentDecompressor(boolean, int) are not ported (owner decision 2026-11-16). zstd-jni is library-not-ported
// (owner decision 2026-11-23; docs/codec-compression-porting.md), so Zstd and ZstdDecoder have no C# type and the
// "Zstd.isAvailable() && ZSTD" branch is left out: a "zstd" Content-Encoding is passed through undecoded (null), as in
// Java when Zstd.isAvailable() is false. Brotli.IsAvailable() is always true (the BCL BrotliDecoder,
// docs/codec-compression-porting.md). AsciiString.contentEqualsIgnoreCase(CharSequence) takes the String encoding wrapped
// in StringCharSequence, and EmbeddedChannel.builder() is new EmbeddedChannel.Builder()
// (src/Netty.NET.Transport/Channels/Embedded/EmbeddedChannel.cs).
public class HttpContentDecompressor : HttpContentDecoder
{

    private readonly bool strict;
    private readonly int maxAllocation;

    /**
     * Create a new {@link HttpContentDecompressor} in non-strict mode.
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public HttpContentDecompressor(int maxAllocation) : this(false, maxAllocation)
    {
    }

    /**
     * Create a new {@link HttpContentDecompressor}.
     *
     * @param strict    if {@code true} use strict handling of deflate if used, otherwise handle it in a
     *                  more lenient fashion.
     * @param maxAllocation
     *             Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public HttpContentDecompressor(bool strict, int maxAllocation)
    {
        this.strict = strict;
        this.maxAllocation = CheckPositiveOrZero(maxAllocation, "maxAllocation");
    }

    protected override EmbeddedChannel NewContentDecoder(string contentEncoding)
    {
        IChannel channel = ctx.Channel;
        ICharSequence encoding = new StringCharSequence(contentEncoding);
        if (GZIP.ContentEqualsIgnoreCase(encoding) ||
            X_GZIP.ContentEqualsIgnoreCase(encoding))
        {
            return new EmbeddedChannel.Builder()
                    .ChannelId(channel.Id)
                    .HasDisconnect(channel.Metadata.HasDisconnect())
                    .Config(channel.Config)
                    .Handlers(ZlibCodecFactory.NewZlibDecoder(ZlibWrapper.GZIP, maxAllocation))
                    .Build();
        }
        if (DEFLATE.ContentEqualsIgnoreCase(encoding) ||
            X_DEFLATE.ContentEqualsIgnoreCase(encoding))
        {
            ZlibWrapper wrapper = strict ? ZlibWrapper.ZLIB : ZlibWrapper.ZLIB_OR_NONE;
            // To be strict, 'deflate' means ZLIB, but some servers were not implemented correctly.
            return new EmbeddedChannel.Builder()
                    .ChannelId(channel.Id)
                    .HasDisconnect(channel.Metadata.HasDisconnect())
                    .Config(channel.Config)
                    .Handlers(ZlibCodecFactory.NewZlibDecoder(wrapper, maxAllocation))
                    .Build();
        }
        if (Brotli.IsAvailable() && BR.ContentEqualsIgnoreCase(encoding))
        {
            return new EmbeddedChannel.Builder()
                    .ChannelId(channel.Id)
                    .HasDisconnect(channel.Metadata.HasDisconnect())
                    .Config(channel.Config)
                    .Handlers(BrotliDecoder.NewDecoderWithMaxAllocation(maxAllocation))
                    .Build();
        }

        if (SNAPPY.ContentEqualsIgnoreCase(encoding))
        {
            return new EmbeddedChannel.Builder()
                    .ChannelId(channel.Id)
                    .HasDisconnect(channel.Metadata.HasDisconnect())
                    .Config(channel.Config)
                    .Handlers(new SnappyFrameDecoder())
                    .Build();
        }

        // 'identity' or unsupported
        return null;
    }
}
