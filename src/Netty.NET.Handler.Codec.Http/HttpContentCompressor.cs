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
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Handler.Codec.Compression;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Compresses an {@link HttpMessage} and an {@link HttpContent} in {@code gzip} or
 * {@code deflate} encoding while respecting the {@code "Accept-Encoding"} header.
 * If there is no matching encoding, no compression is done.  For more
 * information on how this handler modifies the message, please refer to
 * {@link HttpContentEncoder}.
 */
// CLR: No zstd encoder is configured, so content negotiation leaves zstd unavailable. The BCL zlib encoder
// supports only the default window size and memory level.
// Quality values use invariant float parsing. Java numeric suffixes and hexadecimal float forms fall back to zero.
public class HttpContentCompressor : HttpContentEncoder
{

    private readonly BrotliOptions brotliOptions;
    private readonly GzipOptions gzipOptions;
    private readonly DeflateOptions deflateOptions;
    private readonly ICompressionOptions zstdOptions;
    private readonly SnappyOptions snappyOptions;

    private readonly int contentSizeThreshold;
    private IChannelHandlerContext ctx;

    private static readonly ICompressionOptions[] DEFAULT_COMPRESSION_OPTIONS;
    static HttpContentCompressor()
    {
        List<ICompressionOptions> options = new List<ICompressionOptions>(5);
        options.Add(StandardCompressionOptions.Gzip());
        options.Add(StandardCompressionOptions.Deflate());
        options.Add(StandardCompressionOptions.Snappy());

        if (Brotli.IsAvailable())
        {
            options.Add(StandardCompressionOptions.Brotli());
        }

        DEFAULT_COMPRESSION_OPTIONS = options.ToArray();
    }

    /**
     * Creates a new handler with {@link StandardCompressionOptions#brotli()} (if supported) ,
     * {@link StandardCompressionOptions#zstd()} (if supported), {@link StandardCompressionOptions#snappy()},
     * {@link StandardCompressionOptions#gzip()} and {@link StandardCompressionOptions#deflate()}.
     */
    public HttpContentCompressor() : this(0, (ICompressionOptions[]) null)
    {
    }

    /**
     * Creates a new handler with the specified compression level, default
     * window size (<tt>15</tt>) and default memory level (<tt>8</tt>).
     *
     * @param compressionLevel
     *        {@code 1} yields the fastest compression and {@code 9} yields the
     *        best compression.  {@code 0} means no compression.  The default
     *        compression level is {@code 6}.
     */
    [Obsolete]
    public HttpContentCompressor(int compressionLevel) : this(compressionLevel, 15, 8, 0)
    {
    }

    /**
     * Creates a new handler with the specified compression level, window size,
     * and memory level.
     *
     * @param compressionLevel
     *        {@code 1} yields the fastest compression and {@code 9} yields the
     *        best compression.  {@code 0} means no compression.  The default
     *        compression level is {@code 6}.
     * @param windowBits
     *        The base two logarithm of the size of the history buffer.  The
     *        value should be in the range {@code 9} to {@code 15} inclusive.
     *        Larger values result in better compression at the expense of
     *        memory usage.  The default value is {@code 15}.
     * @param memLevel
     *        How much memory should be allocated for the internal compression
     *        state.  {@code 1} uses minimum memory and {@code 9} uses maximum
     *        memory.  Larger values result in better and faster compression
     *        at the expense of memory usage.  The default value is {@code 8}
     */
    [Obsolete]
    public HttpContentCompressor(int compressionLevel, int windowBits, int memLevel)
        : this(compressionLevel, windowBits, memLevel, 0)
    {
    }

    /**
     * Creates a new handler with the specified compression level, window size,
     * and memory level.
     *
     * @param compressionLevel
     *        {@code 1} yields the fastest compression and {@code 9} yields the
     *        best compression.  {@code 0} means no compression.  The default
     *        compression level is {@code 6}.
     * @param windowBits
     *        The base two logarithm of the size of the history buffer.  The
     *        value should be in the range {@code 9} to {@code 15} inclusive.
     *        Larger values result in better compression at the expense of
     *        memory usage.  The default value is {@code 15}.
     * @param memLevel
     *        How much memory should be allocated for the internal compression
     *        state.  {@code 1} uses minimum memory and {@code 9} uses maximum
     *        memory.  Larger values result in better and faster compression
     *        at the expense of memory usage.  The default value is {@code 8}
     * @param contentSizeThreshold
     *        The response body is compressed when the size of the response
     *        body exceeds the threshold. The value should be a non negative
     *        number. {@code 0} will enable compression for all responses.
     */
    [Obsolete]
    public HttpContentCompressor(int compressionLevel, int windowBits, int memLevel, int contentSizeThreshold)
        : this(contentSizeThreshold,
                DefaultCompressionOptions(
                    StandardCompressionOptions.Gzip(
                            CheckInRange(compressionLevel, 0, 9, "compressionLevel"),
                            CheckInRange(windowBits, 9, 15, "windowBits"),
                            CheckInRange(memLevel, 1, 9, "memLevel")
                    ),
                    StandardCompressionOptions.Deflate(
                            CheckInRange(compressionLevel, 0, 9, "compressionLevel"),
                            CheckInRange(windowBits, 9, 15, "windowBits"),
                            CheckInRange(memLevel, 1, 9, "memLevel")
                    )
                )
        )
    {
    }

    /**
     * Create a new {@link HttpContentCompressor} Instance with specified
     * {@link CompressionOptions}s and contentSizeThreshold set to {@code 0}
     *
     * @param compressionOptions {@link CompressionOptions} or {@code null} if the default
     *        should be used.
     */
    public HttpContentCompressor(params ICompressionOptions[] compressionOptions) : this(0, compressionOptions)
    {
    }

    /**
     * Create a new {@link HttpContentCompressor} instance with specified
     * {@link CompressionOptions}s
     *
     * @param contentSizeThreshold
     *        The response body is compressed when the size of the response
     *        body exceeds the threshold. The value should be a non negative
     *        number. {@code 0} will enable compression for all responses.
     * @param compressionOptions {@link CompressionOptions} or {@code null}
     *        if the default should be used.
     */
    public HttpContentCompressor(int contentSizeThreshold, params ICompressionOptions[] compressionOptions)
        : this(contentSizeThreshold, DEFAULT_MAX_PIPELINE_DEPTH, compressionOptions)
    {
    }

    /**
     * Create a new {@link HttpContentCompressor} instance with specified
     * {@link CompressionOptions}s
     *
     * @param contentSizeThreshold
     *        The response body is compressed when the size of the response
     *        body exceeds the threshold. The value should be a non negative
     *        number. {@code 0} will enable compression for all responses.
     * @param maxPipelineDepth
     *        The maximum allowed depth of the encoding pipeline queue, the default
     *        value is set to {@link DEFAULT_MAX_PIPELINE_DEPTH}
     * @param compressionOptions {@link CompressionOptions} or {@code null}
     *        if the default should be used.
     */
    public HttpContentCompressor(int contentSizeThreshold, int maxPipelineDepth,
            params ICompressionOptions[] compressionOptions)
        : base(maxPipelineDepth)
    {
        this.contentSizeThreshold = ObjectUtil.CheckPositiveOrZero(contentSizeThreshold, "contentSizeThreshold");
        BrotliOptions brotliOptions = null;
        GzipOptions gzipOptions = null;
        DeflateOptions deflateOptions = null;
        ICompressionOptions zstdOptions = null;
        SnappyOptions snappyOptions = null;

        if (compressionOptions == null || compressionOptions.Length == 0)
        {
            compressionOptions = DEFAULT_COMPRESSION_OPTIONS;
        }

        ObjectUtil.DeepCheckNotNull("compressionOptions", compressionOptions);

        foreach (ICompressionOptions compressionOption in compressionOptions)
        {
            // BrotliOptions' class initialization depends on Brotli classes being on the classpath.
            // The Brotli.isAvailable check ensures that BrotliOptions will only get instantiated if Brotli is
            // on the classpath.
            // This results in the static analysis of native-image identifying the instanceof BrotliOptions check
            // and thus BrotliOptions itself as unreachable, enabling native-image to link all classes
            // at build time and not complain about the missing Brotli classes.
            if (Brotli.IsAvailable() && compressionOption is BrotliOptions)
            {
                brotliOptions = (BrotliOptions) compressionOption;
            }
            else if (compressionOption is GzipOptions)
            {
                gzipOptions = (GzipOptions) compressionOption;
            }
            else if (compressionOption is DeflateOptions)
            {
                deflateOptions = (DeflateOptions) compressionOption;
            }
            else if (compressionOption is SnappyOptions)
            {
                snappyOptions = (SnappyOptions) compressionOption;
            }
            else
            {
                throw new ArgumentException("Unsupported " + nameof(ICompressionOptions) +
                        ": " + compressionOption);
            }
        }

        this.gzipOptions = gzipOptions;
        this.deflateOptions = deflateOptions;
        this.brotliOptions = brotliOptions;
        this.zstdOptions = zstdOptions;
        this.snappyOptions = snappyOptions;
    }

    [Obsolete]
    private static ICompressionOptions[] DefaultCompressionOptions(
            GzipOptions gzipOptions, DeflateOptions deflateOptions)
    {
        List<ICompressionOptions> options = new List<ICompressionOptions>(5);
        options.Add(gzipOptions);
        options.Add(deflateOptions);
        options.Add(StandardCompressionOptions.Snappy());

        if (Brotli.IsAvailable())
        {
            options.Add(StandardCompressionOptions.Brotli());
        }

        return options.ToArray();
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
    }

    protected override Result BeginEncode(IHttpResponse httpResponse, string acceptEncoding)
    {
        if (this.contentSizeThreshold > 0)
        {
            if (httpResponse is IHttpContent &&
                    ((IHttpContent) httpResponse).Content.ReadableBytes < contentSizeThreshold)
            {
                return null;
            }
        }

        string contentEncoding = httpResponse.Headers().Get(HttpHeaderNames.CONTENT_ENCODING);
        if (contentEncoding != null)
        {
            // Content-Encoding was set, either as something specific or as the IDENTITY encoding
            // Therefore, we should NOT encode here
            return null;
        }

        string targetContentEncoding = DetermineEncoding(acceptEncoding);
        if (targetContentEncoding == null)
        {
            return null;
        }

        IChannel channel = ctx.Channel;
        return new Result(targetContentEncoding,
                new EmbeddedChannel.Builder()
                        .ChannelId(channel.Id)
                        .HasDisconnect(channel.Metadata.HasDisconnect())
                        .Config(channel.Config)
                        .Handlers(CreateEncoderFor(targetContentEncoding))
                        .Build());
    }

    private MessageToByteEncoder<ByteBuf> CreateEncoderFor(string targetContentEncoding)
    {
        switch (targetContentEncoding)
        {
            case "br":
                if (brotliOptions == null || !Brotli.IsAvailable())
                {
                    throw new InvalidOperationException("Brotli not configured");
                }

                return new BrotliEncoder(brotliOptions);
            case "zstd":
                if (zstdOptions == null)
                {
                    throw new InvalidOperationException("Zstd not configured");
                }

                throw new NotSupportedException("ZstdEncoder needs zstd-jni, which is not ported");
            case "snappy":
                if (snappyOptions == null)
                {
                    throw new InvalidOperationException("Snappy not configured");
                }

                return new SnappyFrameEncoder();
            case "gzip":
                if (gzipOptions == null)
                {
                    throw new InvalidOperationException("Gzip not configured");
                }

                return ZlibCodecFactory.NewZlibEncoder(
                        ZlibWrapper.GZIP,
                        gzipOptions.CompressionLevel(),
                        gzipOptions.WindowBits(),
                        gzipOptions.MemLevel()
                );
            case "deflate":
                if (deflateOptions == null)
                {
                    throw new InvalidOperationException("Deflate not configured");
                }

                return ZlibCodecFactory.NewZlibEncoder(
                        ZlibWrapper.ZLIB,
                        deflateOptions.CompressionLevel(),
                        deflateOptions.WindowBits(),
                        deflateOptions.MemLevel()
                );
            default:
                throw new InvalidOperationException("Unknown encoding: " + targetContentEncoding);
        }
    }

    protected virtual string DetermineEncoding(string acceptEncoding)
    {
        float starQ = -1.0f;
        float brQ = -1.0f;
        float zstdQ = -1.0f;
        float snappyQ = -1.0f;
        float gzipQ = -1.0f;
        float deflateQ = -1.0f;

        int start = 0;
        int length = acceptEncoding.Length;

        while (start < length)
        {
            int comma = acceptEncoding.IndexOf(',', start);
            if (comma == -1)
            {
                comma = length;
            }

            string encoding = acceptEncoding.Substring(start, comma - start);
            float q = 1.0f;
            int equalsPos = encoding.IndexOf('=');
            if (equalsPos != -1)
            {
                try
                {
                    q = ParseFloat(encoding.Substring(equalsPos + 1));
                }
                catch (FormatException)
                {
                    // Ignore encoding
                    q = 0.0f;
                }
            }

            if (encoding.Contains('*'))
            {
                starQ = q;
            }
            else if (encoding.Contains("br", StringComparison.Ordinal) && q > brQ)
            {
                brQ = q;
            }
            else if (encoding.Contains("zstd", StringComparison.Ordinal) && q > zstdQ)
            {
                zstdQ = q;
            }
            else if (encoding.Contains("snappy", StringComparison.Ordinal) && q > snappyQ)
            {
                snappyQ = q;
            }
            else if (encoding.Contains("gzip", StringComparison.Ordinal) && q > gzipQ)
            {
                gzipQ = q;
            }
            else if (encoding.Contains("deflate", StringComparison.Ordinal) && q > deflateQ)
            {
                deflateQ = q;
            }

            start = comma + 1;
        }

        if (brQ > 0.0f || zstdQ > 0.0f || snappyQ > 0.0f || gzipQ > 0.0f || deflateQ > 0.0f)
        {
            if (brQ != -1.0f && brQ >= zstdQ && this.brotliOptions != null)
            {
                return "br";
            }
            else if (zstdQ != -1.0f && zstdQ >= snappyQ && this.zstdOptions != null)
            {
                return "zstd";
            }
            else if (snappyQ != -1.0f && snappyQ >= gzipQ && this.snappyOptions != null)
            {
                return "snappy";
            }
            else if (gzipQ != -1.0f && gzipQ >= deflateQ && this.gzipOptions != null)
            {
                return "gzip";
            }
            else if (deflateQ != -1.0f && this.deflateOptions != null)
            {
                return "deflate";
            }
        }

        if (starQ > 0.0f)
        {
            if (brQ == -1.0f && this.brotliOptions != null)
            {
                return "br";
            }
            if (zstdQ == -1.0f && this.zstdOptions != null)
            {
                return "zstd";
            }
            if (snappyQ == -1.0f && this.snappyOptions != null)
            {
                return "snappy";
            }
            if (gzipQ == -1.0f && this.gzipOptions != null)
            {
                return "gzip";
            }
            if (deflateQ == -1.0f && this.deflateOptions != null)
            {
                return "deflate";
            }
        }

        return null;
    }

    [Obsolete]
    protected virtual ZlibWrapper? DetermineWrapper(string acceptEncoding)
    {
        float starQ = -1.0f;
        float gzipQ = -1.0f;
        float deflateQ = -1.0f;

        foreach (string encoding in acceptEncoding.Split(','))
        {
            float q = 1.0f;
            int equalsPos = encoding.IndexOf('=');
            if (equalsPos != -1)
            {
                try
                {
                    q = ParseFloat(encoding.Substring(equalsPos + 1));
                }
                catch (FormatException)
                {
                    // Ignore encoding
                    q = 0.0f;
                }
            }

            if (encoding.Contains('*'))
            {
                starQ = q;
            }
            else if (encoding.Contains("gzip", StringComparison.Ordinal) && q > gzipQ)
            {
                gzipQ = q;
            }
            else if (encoding.Contains("deflate", StringComparison.Ordinal) && q > deflateQ)
            {
                deflateQ = q;
            }
        }

        if (gzipQ > 0.0f || deflateQ > 0.0f)
        {
            if (gzipQ >= deflateQ)
            {
                return ZlibWrapper.GZIP;
            }
            else
            {
                return ZlibWrapper.ZLIB;
            }
        }

        if (starQ > 0.0f)
        {
            if (gzipQ == -1.0f)
            {
                return ZlibWrapper.GZIP;
            }
            if (deflateQ == -1.0f)
            {
                return ZlibWrapper.ZLIB;
            }
        }

        return null;
    }

    // CLR: java.lang.Float.parseFloat(String): leading and trailing whitespace is ignored as Java trims it
    // (String.trim), an invalid number is the FormatException (NumberFormatException). OverflowException cannot
    // occur: "In.NET Core 3.0 and later, values that are too large to represent are rounded to PositiveInfinity or
    // NegativeInfinity" (https://learn.microsoft.com/en-us/dotnet/api/system.single.parse?view=net-10.0), as
    // Java's parseFloat returns infinity.
    private static float ParseFloat(string s)
    {
        return float.Parse(HttpUtil.TrimJava(s), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
