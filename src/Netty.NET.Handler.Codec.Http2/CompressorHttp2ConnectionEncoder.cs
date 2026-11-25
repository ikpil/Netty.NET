/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Handler.Codec.Compression;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpHeaderValues;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A decorating HTTP2 encoder that will compress data frames according to the {@code content-encoding} header for each
 * stream. The compression provided by this class will be applied to the data for the entire stream.
 */
// CLR: ChannelPromise is TaskCompletionSource and ChannelFuture is Task; the Write*Async overrides return the promise's
// Task (docs/codec-http-porting.md, Shared CLR adaptations). promise.setSuccess() is SetResult(), tryFailure is
// TrySetException and ctx.newPromise() is Http2CodecUtil.NewPromise(). PromiseCombiner is a list of the buffer promises'
// Tasks combined with Task.WhenAll ("Collect List<Task> and call Task.WhenAll after collection",
// docs/common-task-composition.md), and combiner.finish(promise) copies the outcome of that Task into the promise
// (Http2CodecUtil.Cascade, TrySetFromTask). The two anonymous Http2ConnectionAdapter instances of the constructors are
// the same code and are one nested class, StreamRemovedListener. CompressionOptions is codec-compression's marker
// interface ICompressionOptions (CompressionOptions... is params ICompressionOptions[]) and the option and encoder types
// are the codec-compression ports (docs/codec-compression-porting.md); EmbeddedChannel.builder() is new
// EmbeddedChannel.Builder() (docs/codec-http-porting.md, HttpContentCompressor). zstd-jni is library-not-ported (owner
// decision 2026-11-23; docs/codec-compression-porting.md: the BCL Zstandard types exist only from .NET 11,
// https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zstandardstream), so ZstdOptions, Zstd and
// ZstdEncoder have no C# type and this class behaves as Java does when Zstd.isAvailable() is false, as codec-http's
// HttpContentCompressor does (docs/codec-http-porting.md): defaultCompressionOptions() never adds
// StandardCompressionOptions.zstd(), no option can be a ZstdOptions (the instanceof ZstdOptions branch has no type to
// test), and the zstdOptions field stays an always-null ICompressionOptions, so the zstd branch of
// newContentCompressor is never taken (it throws NotSupportedException if it were). brotli4j is not ported either:
// Brotli.IsAvailable() is always true (the BCL BrotliEncoder) and BrotliOptions.parameters(), which returns brotli4j's
// Encoder.Parameters, is not ported, so the BrotliEncoder is created from the BrotliOptions themselves
// (BrotliEncoder(BrotliOptions), docs/codec-compression-porting.md). The @Deprecated constructor (int, int, int) has no
// @deprecated text and names no replacement, so it is ported with [Obsolete] (owner decision 2026-11-16); a window
// size or memory level other than 15/8 makes ZlibCodecFactory.NewZlibEncoder throw NotSupportedException (jzlib is not
// ported, docs/codec-compression-porting.md). CompressionOptions.class.getSimpleName() is nameof(ICompressionOptions),
// as in HttpContentCompressor. ZlibWrapper is a C# enum, whose ToString() is the constant name, as Java's
// Enum.toString(). The package-private cleanup(...) is internal. Throwable is Exception and IllegalArgumentException is
// ArgumentException (docs/codec-base-porting.md); "throws Http2Exception" has no counterpart.
public class CompressorHttp2ConnectionEncoder : DecoratingHttp2ConnectionEncoder
{
    // We cannot remove this because it'll be breaking change
    public const int DEFAULT_COMPRESSION_LEVEL = 6;
    public const int DEFAULT_WINDOW_BITS = 15;
    public const int DEFAULT_MEM_LEVEL = 8;

    private int compressionLevel;
    private int windowBits;
    private int memLevel;
    private readonly IHttp2Connection.IPropertyKey propertyKey;

    private readonly bool supportsCompressionOptions;

    private BrotliOptions brotliOptions;
    private GzipOptions gzipCompressionOptions;
    private DeflateOptions deflateOptions;
    // CLR: always null (ZstdOptions is library-not-ported, see the class comment).
    private ICompressionOptions zstdOptions = null;
    private SnappyOptions snappyOptions;

    /**
     * Create a new {@link CompressorHttp2ConnectionEncoder} instance
     * with default implementation of {@link StandardCompressionOptions}
     */
    public CompressorHttp2ConnectionEncoder(IHttp2ConnectionEncoder @delegate)
        : this(@delegate, DefaultCompressionOptions())
    {
    }

    private static ICompressionOptions[] DefaultCompressionOptions()
    {
        List<ICompressionOptions> compressionOptions = new List<ICompressionOptions>();
        compressionOptions.Add(StandardCompressionOptions.Gzip());
        compressionOptions.Add(StandardCompressionOptions.Deflate());
        compressionOptions.Add(StandardCompressionOptions.Snappy());
        if (Brotli.IsAvailable())
        {
            compressionOptions.Add(StandardCompressionOptions.Brotli());
        }
        return compressionOptions.ToArray();
    }

    /**
     * Create a new {@link CompressorHttp2ConnectionEncoder} instance
     */
    [Obsolete]
    public CompressorHttp2ConnectionEncoder(IHttp2ConnectionEncoder @delegate, int compressionLevel, int windowBits,
                                            int memLevel)
        : base(@delegate)
    {
        this.compressionLevel = ObjectUtil.CheckInRange(compressionLevel, 0, 9, "compressionLevel");
        this.windowBits = ObjectUtil.CheckInRange(windowBits, 9, 15, "windowBits");
        this.memLevel = ObjectUtil.CheckInRange(memLevel, 1, 9, "memLevel");

        propertyKey = Connection().NewKey();
        Connection().AddListener(new StreamRemovedListener(this));

        supportsCompressionOptions = false;
    }

    /**
     * Create a new {@link CompressorHttp2ConnectionEncoder} with
     * specified {@link StandardCompressionOptions}
     */
    public CompressorHttp2ConnectionEncoder(IHttp2ConnectionEncoder @delegate,
                                            params ICompressionOptions[] compressionOptionsArgs)
        : base(@delegate)
    {
        ObjectUtil.CheckNotNull(compressionOptionsArgs, "CompressionOptions");
        ObjectUtil.DeepCheckNotNull("CompressionOptions", compressionOptionsArgs);

        foreach (ICompressionOptions compressionOptions in compressionOptionsArgs)
        {
            // BrotliOptions' class initialization depends on Brotli classes being on the classpath.
            // The Brotli.isAvailable check ensures that BrotliOptions will only get instantiated if Brotli is on
            // the classpath.
            // This results in the static analysis of native-image identifying the instanceof BrotliOptions check
            // and thus BrotliOptions itself as unreachable, enabling native-image to link all classes at build time
            // and not complain about the missing Brotli classes.
            if (Brotli.IsAvailable() && compressionOptions is BrotliOptions)
            {
                brotliOptions = (BrotliOptions) compressionOptions;
            }
            else if (compressionOptions is GzipOptions)
            {
                gzipCompressionOptions = (GzipOptions) compressionOptions;
            }
            else if (compressionOptions is DeflateOptions)
            {
                deflateOptions = (DeflateOptions) compressionOptions;
            }
            else if (compressionOptions is SnappyOptions)
            {
                snappyOptions = (SnappyOptions) compressionOptions;
            }
            else
            {
                throw new ArgumentException("Unsupported " + nameof(ICompressionOptions) +
                        ": " + compressionOptions);
            }
        }

        supportsCompressionOptions = true;

        propertyKey = Connection().NewKey();
        Connection().AddListener(new StreamRemovedListener(this));
    }

    // CLR: the anonymous Http2ConnectionAdapter of both constructors.
    private sealed class StreamRemovedListener : Http2ConnectionAdapter
    {
        private readonly CompressorHttp2ConnectionEncoder encoder;

        internal StreamRemovedListener(CompressorHttp2ConnectionEncoder encoder)
        {
            this.encoder = encoder;
        }

        public override void OnStreamRemoved(IHttp2Stream stream)
        {
            EmbeddedChannel compressor = stream.GetProperty<EmbeddedChannel>(encoder.propertyKey);
            if (compressor != null)
            {
                encoder.Cleanup(stream, compressor);
            }
        }
    }

    public override Task WriteDataAsync(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding,
            bool endOfStream, TaskCompletionSource promise)
    {
        IHttp2Stream stream = Connection().Stream(streamId);
        EmbeddedChannel channel = stream == null ? null : stream.GetProperty<EmbeddedChannel>(propertyKey);
        if (channel == null)
        {
            // The compressor may be null if no compatible encoding type was found in this stream's headers
            return base.WriteDataAsync(ctx, streamId, data, padding, endOfStream, promise);
        }

        try
        {
            // The channel will release the buffer after being written
            channel.WriteOutbound(data);
            ByteBuf buf = NextReadableBuf(channel);
            if (buf == null)
            {
                if (endOfStream)
                {
                    if (channel.Finish())
                    {
                        buf = NextReadableBuf(channel);
                    }
                    return base.WriteDataAsync(ctx, streamId, buf == null ? Unpooled.EmptyBuffer : buf, padding,
                            true, promise);
                }
                // END_STREAM is not set and the assumption is data is still forthcoming.
                promise.SetResult();
                return promise.Task;
            }

            List<Task> combiner = new List<Task>();
            for (;;)
            {
                ByteBuf nextBuf = NextReadableBuf(channel);
                bool compressedEndOfStream = nextBuf == null && endOfStream;
                if (compressedEndOfStream && channel.Finish())
                {
                    nextBuf = NextReadableBuf(channel);
                    compressedEndOfStream = nextBuf == null;
                }

                TaskCompletionSource bufPromise = Http2CodecUtil.NewPromise();
                combiner.Add(bufPromise.Task);
                _ = base.WriteDataAsync(ctx, streamId, buf, padding, compressedEndOfStream, bufPromise);
                if (nextBuf == null)
                {
                    break;
                }

                padding = 0; // Padding is only communicated once on the first iteration
                buf = nextBuf;
            }
            Http2CodecUtil.Cascade(ctx, Task.WhenAll(combiner), promise);
        }
        catch (Exception cause)
        {
            promise.TrySetException(cause);
        }
        finally
        {
            if (endOfStream)
            {
                Cleanup(stream, channel);
            }
        }
        return promise.Task;
    }

    public override Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
            int padding, bool endStream, TaskCompletionSource promise)
    {
        EmbeddedChannel compressor = null;
        try
        {
            // Determine if compression is required and sanitize the headers.
            compressor = NewCompressor(ctx, headers, endStream);

            // Write the headers and create the stream object.
            Task future = base.WriteHeadersAsync(ctx, streamId, headers, padding, endStream, promise);

            // After the stream object has been created, then attach the compressor as a property for data compression.
            if (BindCompressorToStream(compressor, streamId))
            {
                compressor = null;
            }

            return future;
        }
        catch (Exception e)
        {
            promise.TrySetException(e);
        }
        finally
        {
            if (compressor != null)
            {
                compressor.FinishAndReleaseAll();
            }
        }
        return promise.Task;
    }

    public override Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
            int streamDependency, short weight, bool exclusive, int padding,
            bool endOfStream, TaskCompletionSource promise)
    {
        EmbeddedChannel compressor = null;
        try
        {
            // Determine if compression is required and sanitize the headers.
            compressor = NewCompressor(ctx, headers, endOfStream);

            // Write the headers and create the stream object.
            Task future = base.WriteHeadersAsync(ctx, streamId, headers, streamDependency, weight, exclusive,
                                                      padding, endOfStream, promise);

            // After the stream object has been created, then attach the compressor as a property for data compression.
            if (BindCompressorToStream(compressor, streamId))
            {
                compressor = null;
            }

            return future;
        }
        catch (Exception e)
        {
            promise.TrySetException(e);
        }
        finally
        {
            if (compressor != null)
            {
                compressor.FinishAndReleaseAll();
            }
        }
        return promise.Task;
    }

    /**
     * Returns a new {@link EmbeddedChannel} that encodes the HTTP2 message content encoded in the specified
     * {@code contentEncoding}.
     *
     * @param ctx the context.
     * @param contentEncoding the value of the {@code content-encoding} header
     * @return a new {@link ByteToMessageDecoder} if the specified encoding is supported. {@code null} otherwise
     * (alternatively, you can throw a {@link Http2Exception} to block unknown encoding).
     * @throws Http2Exception If the specified encoding is not supported and warrants an exception
     */
    protected virtual EmbeddedChannel NewContentCompressor(IChannelHandlerContext ctx, ICharSequence contentEncoding)
    {
        if (GZIP.ContentEqualsIgnoreCase(contentEncoding) || X_GZIP.ContentEqualsIgnoreCase(contentEncoding))
        {
            return NewCompressionChannel(ctx, ZlibWrapper.GZIP);
        }
        if (DEFLATE.ContentEqualsIgnoreCase(contentEncoding) || X_DEFLATE.ContentEqualsIgnoreCase(contentEncoding))
        {
            return NewCompressionChannel(ctx, ZlibWrapper.ZLIB);
        }
        IChannel channel = ctx.Channel;
        if (Brotli.IsAvailable() && brotliOptions != null && BR.ContentEqualsIgnoreCase(contentEncoding))
        {
            return new EmbeddedChannel.Builder()
                    .ChannelId(channel.Id)
                    .HasDisconnect(channel.Metadata.HasDisconnect())
                    .Config(channel.Config)
                    .Handlers(new BrotliEncoder(brotliOptions))
                    .Build();
        }
        if (zstdOptions != null && ZSTD.ContentEqualsIgnoreCase(contentEncoding))
        {
            // CLR: unreachable, zstdOptions is always null (see the class comment); ZstdEncoder needs zstd-jni.
            throw new NotSupportedException("ZstdEncoder needs zstd-jni, which is not ported");
        }
        if (snappyOptions != null && SNAPPY.ContentEqualsIgnoreCase(contentEncoding))
        {
            return new EmbeddedChannel.Builder()
                    .ChannelId(channel.Id)
                    .HasDisconnect(channel.Metadata.HasDisconnect())
                    .Config(channel.Config)
                    .Handlers(new SnappyFrameEncoder())
                    .Build();
        }
        // 'identity' or unsupported
        return null;
    }

    /**
     * Returns the expected content encoding of the decoded content. Returning {@code contentEncoding} is the default
     * behavior, which is the case for most compressors.
     *
     * @param contentEncoding the value of the {@code content-encoding} header
     * @return the expected content encoding of the new content.
     * @throws Http2Exception if the {@code contentEncoding} is not supported and warrants an exception
     */
    protected virtual ICharSequence GetTargetContentEncoding(ICharSequence contentEncoding)
    {
        return contentEncoding;
    }

    /**
     * Generate a new instance of an {@link EmbeddedChannel} capable of compressing data
     * @param ctx the context.
     * @param wrapper Defines what type of encoder should be used
     */
    private EmbeddedChannel NewCompressionChannel(IChannelHandlerContext ctx, ZlibWrapper wrapper)
    {
        IChannel channel = ctx.Channel;
        if (supportsCompressionOptions)
        {
            if (wrapper == ZlibWrapper.GZIP && gzipCompressionOptions != null)
            {
                return new EmbeddedChannel.Builder()
                        .ChannelId(channel.Id)
                        .HasDisconnect(channel.Metadata.HasDisconnect())
                        .Config(channel.Config)
                        .Handlers(ZlibCodecFactory.NewZlibEncoder(wrapper,
                                gzipCompressionOptions.CompressionLevel(),
                                gzipCompressionOptions.WindowBits(),
                                gzipCompressionOptions.MemLevel())
                        )
                        .Build();
            }
            else if (wrapper == ZlibWrapper.ZLIB && deflateOptions != null)
            {
                return new EmbeddedChannel.Builder()
                        .ChannelId(channel.Id)
                        .HasDisconnect(channel.Metadata.HasDisconnect())
                        .Config(channel.Config)
                        .Handlers(ZlibCodecFactory.NewZlibEncoder(wrapper,
                                deflateOptions.CompressionLevel(),
                                deflateOptions.WindowBits(),
                                deflateOptions.MemLevel())
                        )
                        .Build();
            }
            else
            {
                throw new ArgumentException("Unsupported ZlibWrapper: " + wrapper);
            }
        }
        else
        {
            return new EmbeddedChannel.Builder()
                    .ChannelId(channel.Id)
                    .HasDisconnect(channel.Metadata.HasDisconnect())
                    .Config(channel.Config)
                    .Handlers(ZlibCodecFactory.NewZlibEncoder(wrapper, compressionLevel, windowBits, memLevel))
                    .Build();
        }
    }

    /**
     * Checks if a new compressor object is needed for the stream identified by {@code streamId}. This method will
     * modify the {@code content-encoding} header contained in {@code headers}.
     *
     * @param ctx the context.
     * @param headers Object representing headers which are to be written
     * @param endOfStream Indicates if the stream has ended
     * @return The channel used to compress data.
     * @throws Http2Exception if any problems occur during initialization.
     */
    private EmbeddedChannel NewCompressor(IChannelHandlerContext ctx, IHttp2Headers headers, bool endOfStream)
    {
        if (endOfStream)
        {
            return null;
        }

        ICharSequence encoding = headers.Get(CONTENT_ENCODING);
        if (encoding == null)
        {
            encoding = IDENTITY;
        }
        EmbeddedChannel compressor = NewContentCompressor(ctx, encoding);
        try
        {
            if (compressor != null)
            {
                ICharSequence targetContentEncoding = GetTargetContentEncoding(encoding);
                if (IDENTITY.ContentEqualsIgnoreCase(targetContentEncoding))
                {
                    headers.Remove(CONTENT_ENCODING);
                }
                else
                {
                    headers.Set(CONTENT_ENCODING, targetContentEncoding);
                }

                // The content length will be for the decompressed data. Since we will compress the data
                // this content-length will not be correct. Instead of queuing messages or delaying sending
                // header frames...just remove the content-length header
                headers.Remove(CONTENT_LENGTH);
            }

            EmbeddedChannel result = compressor;
            compressor = null;
            return result;
        }
        finally
        {
            if (compressor != null)
            {
                compressor.FinishAndReleaseAll();
            }
        }
    }

    /**
     * Called after the super class has written the headers and created any associated stream objects.
     * @param compressor The compressor associated with the stream identified by {@code streamId}.
     * @param streamId The stream id for which the headers were written.
     * @return {@code true} if ownership of {@code compressor} was transferred to the stream.
     */
    private bool BindCompressorToStream(EmbeddedChannel compressor, int streamId)
    {
        if (compressor != null)
        {
            IHttp2Stream stream = Connection().Stream(streamId);
            if (stream != null)
            {
                stream.SetProperty(propertyKey, compressor);
                return true;
            }
        }
        return false;
    }

    /**
     * Release remaining content from {@link EmbeddedChannel} and remove the compressor from the {@link Http2Stream}.
     *
     * @param stream The stream for which {@code compressor} is the compressor for
     * @param compressor The compressor for {@code stream}
     */
    internal void Cleanup(IHttp2Stream stream, EmbeddedChannel compressor)
    {
        compressor.FinishAndReleaseAll();
        stream.RemoveProperty<EmbeddedChannel>(propertyKey);
    }

    /**
     * Read the next compressed {@link ByteBuf} from the {@link EmbeddedChannel} or {@code null} if one does not exist.
     *
     * @param compressor The channel to read from
     * @return The next decoded {@link ByteBuf} from the {@link EmbeddedChannel} or {@code null} if one does not exist
     */
    private static ByteBuf NextReadableBuf(EmbeddedChannel compressor)
    {
        for (;;)
        {
            ByteBuf buf = compressor.ReadOutbound<ByteBuf>();
            if (buf == null)
            {
                return null;
            }
            if (!buf.IsReadable())
            {
                buf.Release();
                continue;
            }
            return buf;
        }
    }
}
