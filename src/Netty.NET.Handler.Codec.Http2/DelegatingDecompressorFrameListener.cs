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
using System.Diagnostics;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Handler.Codec.Compression;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpHeaderValues;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * An HTTP2 frame listener that will decompress data frames according to the {@code content-encoding} header for each
 * stream. The decompression provided by this class will be applied to the data for the entire stream.
 */
// CLR: the two @Deprecated constructors (connection, listener) and (connection, listener, strict), whose @deprecated
// texts name the (..., int maxAllocation) overloads, are not ported (owner decision 2026-11-16); their comments are
// recorded in the manifest. The inner class ConsumedBytesConverter receives the enclosing listener (a C# nested class
// has no enclosing instance), the anonymous Http2ConnectionAdapter of the constructor is the nested
// StreamRemovedListener, and the anonymous ChannelInboundHandlerAdapter of Http2Decompressor is the nested
// DecompressorHandler, which reads and writes the decompressor's fields. The package-private decompressor(stream)
// returns the private Http2Decompressor, so it is private ("The return type and parameter types of a method shall be
// at least as accessible as the method itself", https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/
// language-specification/basic-concepts, Accessibility constraints; only the class and its nested classes call it).
// zstd-jni is library-not-ported (owner decision 2026-11-23; docs/codec-compression-porting.md), so Zstd and ZstdDecoder have no C# type and the "Zstd.isAvailable() && ZSTD"
// branch is left out: a "zstd" content-encoding is passed through undecoded (null), as in Java when
// Zstd.isAvailable() is false (codec-http's HttpContentDecompressor does the same, docs/codec-http-porting.md).
// Brotli.IsAvailable() is always true (the BCL BrotliDecoder); new BrotliDecoder(maxAllocation) keeps the Java call,
// which passes maxAllocation as the input buffer size (0 selects the default). EmbeddedChannel.builder() is new
// EmbeddedChannel.Builder() (docs/codec-http-porting.md). java.nio.channels.ClosedChannelException is the Transport
// ClosedChannelException, which EmbeddedChannel records for a closed channel as Java does. A Java assert is
// Debug.Assert, Math.ceil is Math.Ceiling (the (int) conversion saturates in both, docs/codec-compression-porting.md),
// Throwable is Exception, and "catch (Http2Exception e) { throw e; }" is "throw;". EmbeddedChannel rethrows the
// exception a handler recorded unchanged (ExceptionDispatchInfo, src/Netty.NET.Transport/Channels/Embedded/
// EmbeddedChannel.cs CheckException), so "t instanceof Http2Exception" sees the listener's exception as in Java.
// "throws Http2Exception" has no checked-exception counterpart.
public class DelegatingDecompressorFrameListener : Http2FrameListenerDecorator
{

    private readonly IHttp2Connection connection;
    private readonly bool strict;
    private bool flowControllerInitialized;
    private readonly IHttp2Connection.IPropertyKey propertyKey;
    private readonly int maxAllocation;

    /**
     * Create a new instance.
     *
     * @param connection the connection to read data which should be decompressed
     * @param listener the delegate listener used by {@link Http2FrameListenerDecorator}
     * @param maxAllocation maximum size of the decompression buffer. Must be &gt;= 0.
     *                      If zero, maximum size is not limited by decoder.
     *                      Some compression codecs will output buffers up to 64 KiB in size,
     *                      even if {@code maxAllocation} is configured lower.
     */
    public DelegatingDecompressorFrameListener(IHttp2Connection connection, IHttp2FrameListener listener,
                                               int maxAllocation)
        : this(connection, listener, true, maxAllocation)
    {
    }

    /**
     * Create a new instance.
     *
     * @param connection the connection to read data which should be decompressed
     * @param listener the delegate listener used by {@link Http2FrameListenerDecorator}
     * @param strict if `true`, {@link ZlibWrapper#ZLIB} will be used for the decoder,
     *               otherwise the decoder can fallback to {@link ZlibWrapper#NONE}
     * @param maxAllocation maximum size of the decompression buffer. Must be &gt;= 0.
     *                      If zero, maximum size is not limited by decoder.
     *                      Some compression codecs will output buffers up to 64 KiB in size,
     *                      even if {@code maxAllocation} is configured lower.
     */
    public DelegatingDecompressorFrameListener(IHttp2Connection connection, IHttp2FrameListener listener,
                    bool strict, int maxAllocation)
        : base(listener)
    {
        this.connection = connection;
        this.strict = strict;
        this.maxAllocation = CheckPositiveOrZero(maxAllocation, "maxAllocation");

        propertyKey = connection.NewKey();
        connection.AddListener(new StreamRemovedListener(this));
    }

    // CLR: the anonymous Http2ConnectionAdapter of the constructor.
    private sealed class StreamRemovedListener : Http2ConnectionAdapter
    {
        private readonly DelegatingDecompressorFrameListener frameListener;

        internal StreamRemovedListener(DelegatingDecompressorFrameListener frameListener)
        {
            this.frameListener = frameListener;
        }

        public override void OnStreamRemoved(IHttp2Stream stream)
        {
            Http2Decompressor decompressor = frameListener.Decompressor(stream);
            if (decompressor != null)
            {
                decompressor.Cleanup();
            }
        }
    }

    public override int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding,
                                   bool endOfStream)
    {
        IHttp2Stream stream = connection.Stream(streamId);
        Http2Decompressor decompressor = Decompressor(stream);
        if (decompressor == null)
        {
            // The decompressor may be null if no compatible encoding type was found in this stream's headers
            return listener.OnDataRead(ctx, streamId, data, padding, endOfStream);
        }
        return decompressor.Decompress(ctx, stream, data, padding, endOfStream);
    }

    public override void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding,
                    bool endStream)
    {
        InitDecompressor(ctx, streamId, headers, endStream);
        listener.OnHeadersRead(ctx, streamId, headers, padding, endStream);
    }

    public override void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
                    int streamDependency, short weight, bool exclusive, int padding, bool endStream)
    {
        InitDecompressor(ctx, streamId, headers, endStream);
        listener.OnHeadersRead(ctx, streamId, headers, streamDependency, weight, exclusive, padding, endStream);
    }

    /**
     * Returns a new {@link EmbeddedChannel} that decodes the HTTP2 message content encoded in the specified
     * {@code contentEncoding}.
     *
     * @param contentEncoding the value of the {@code content-encoding} header
     * @return a new {@link ByteToMessageDecoder} if the specified encoding is supported. {@code null} otherwise
     *         (alternatively, you can throw a {@link Http2Exception} to block unknown encoding).
     * @throws Http2Exception If the specified encoding is not supported and warrants an exception
     */
    protected virtual EmbeddedChannel NewContentDecompressor(IChannelHandlerContext ctx, ICharSequence contentEncoding)
    {
        IChannel channel = ctx.Channel;
        if (GZIP.ContentEqualsIgnoreCase(contentEncoding) || X_GZIP.ContentEqualsIgnoreCase(contentEncoding))
        {
            return new EmbeddedChannel.Builder()
                    .ChannelId(channel.Id)
                    .HasDisconnect(channel.Metadata.HasDisconnect())
                    .Config(channel.Config)
                    .Handlers(ZlibCodecFactory.NewZlibDecoder(ZlibWrapper.GZIP, maxAllocation))
                    .Build();
        }
        if (DEFLATE.ContentEqualsIgnoreCase(contentEncoding) || X_DEFLATE.ContentEqualsIgnoreCase(contentEncoding))
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
        if (Brotli.IsAvailable() && BR.ContentEqualsIgnoreCase(contentEncoding))
        {
            return new EmbeddedChannel.Builder()
                    .ChannelId(channel.Id)
                    .HasDisconnect(channel.Metadata.HasDisconnect())
                    .Config(channel.Config)
                    .Handlers(new BrotliDecoder(maxAllocation))
                    .Build();
        }
        if (SNAPPY.ContentEqualsIgnoreCase(contentEncoding))
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

    /**
     * Returns the expected content encoding of the decoded content. This getMethod returns {@code "identity"} by
     * default, which is the case for most decompressors.
     *
     * @param contentEncoding the value of the {@code content-encoding} header
     * @return the expected content encoding of the new content.
     * @throws Http2Exception if the {@code contentEncoding} is not supported and warrants an exception
     */
    protected virtual ICharSequence GetTargetContentEncoding(ICharSequence contentEncoding)
    {
        return IDENTITY;
    }

    /**
     * Checks if a new decompressor object is needed for the stream identified by {@code streamId}.
     * This method will modify the {@code content-encoding} header contained in {@code headers}.
     *
     * @param ctx The context
     * @param streamId The identifier for the headers inside {@code headers}
     * @param headers Object representing headers which have been read
     * @param endOfStream Indicates if the stream has ended
     * @throws Http2Exception If the {@code content-encoding} is not supported
     */
    private void InitDecompressor(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, bool endOfStream)
    {
        IHttp2Stream stream = connection.Stream(streamId);
        if (stream == null)
        {
            return;
        }

        Http2Decompressor decompressor = Decompressor(stream);
        if (decompressor == null && !endOfStream)
        {
            // Determine the content encoding.
            ICharSequence contentEncoding = headers.Get(CONTENT_ENCODING);
            if (contentEncoding == null)
            {
                contentEncoding = IDENTITY;
            }
            EmbeddedChannel channel = NewContentDecompressor(ctx, contentEncoding);
            if (channel != null)
            {
                decompressor = new Http2Decompressor(channel, connection, listener);
                stream.SetProperty(propertyKey, decompressor);
                // Decode the content and remove or replace the existing headers
                // so that the message looks like a decoded message.
                ICharSequence targetContentEncoding = GetTargetContentEncoding(contentEncoding);
                if (IDENTITY.ContentEqualsIgnoreCase(targetContentEncoding))
                {
                    headers.Remove(CONTENT_ENCODING);
                }
                else
                {
                    headers.Set(CONTENT_ENCODING, targetContentEncoding);
                }
            }
        }

        if (decompressor != null)
        {
            // The content length will be for the compressed data. Since we will decompress the data
            // this content-length will not be correct. Instead of queuing messages or delaying sending
            // header frames just remove the content-length header.
            headers.Remove(CONTENT_LENGTH);

            // The first time that we initialize a decompressor, decorate the local flow controller to
            // properly convert consumed bytes.
            if (!flowControllerInitialized)
            {
                flowControllerInitialized = true;
                connection.Local().FlowController(new ConsumedBytesConverter(this, connection.Local().FlowController()));
            }
        }
    }

    private Http2Decompressor Decompressor(IHttp2Stream stream)
    {
        return stream == null ? null : stream.GetProperty<Http2Decompressor>(propertyKey);
    }

    /**
     * A decorator around the local flow controller that converts consumed bytes from uncompressed to compressed.
     */
    private sealed class ConsumedBytesConverter : IHttp2LocalFlowController
    {
        private readonly DelegatingDecompressorFrameListener outer;
        private readonly IHttp2LocalFlowController flowController;

        internal ConsumedBytesConverter(DelegatingDecompressorFrameListener outer,
                                        IHttp2LocalFlowController flowController)
        {
            this.outer = outer;
            this.flowController = CheckNotNull(flowController, "flowController");
        }

        public IHttp2LocalFlowController FrameWriter(IHttp2FrameWriter frameWriter)
        {
            return flowController.FrameWriter(frameWriter);
        }

        public void ChannelHandlerContext(IChannelHandlerContext ctx)
        {
            flowController.ChannelHandlerContext(ctx);
        }

        public void InitialWindowSize(int newWindowSize)
        {
            flowController.InitialWindowSize(newWindowSize);
        }

        public int InitialWindowSize()
        {
            return flowController.InitialWindowSize();
        }

        public int WindowSize(IHttp2Stream stream)
        {
            return flowController.WindowSize(stream);
        }

        public void IncrementWindowSize(IHttp2Stream stream, int delta)
        {
            flowController.IncrementWindowSize(stream, delta);
        }

        public void ReceiveFlowControlledFrame(IHttp2Stream stream, ByteBuf data, int padding,
                bool endOfStream)
        {
            flowController.ReceiveFlowControlledFrame(stream, data, padding, endOfStream);
        }

        public bool ConsumeBytes(IHttp2Stream stream, int numBytes)
        {
            Http2Decompressor decompressor = outer.Decompressor(stream);
            if (decompressor != null)
            {
                // Convert the decompressed bytes to compressed (on the wire) bytes.
                numBytes = decompressor.ConsumeBytes(stream.Id(), numBytes);
            }
            try
            {
                return flowController.ConsumeBytes(stream, numBytes);
            }
            catch (Http2Exception)
            {
                throw;
            }
            catch (Exception t)
            {
                // The stream should be closed at this point. We have already changed our state tracking the compressed
                // bytes, and there is no guarantee we can recover if the underlying flow controller throws.
                throw StreamError(stream.Id(), INTERNAL_ERROR, t, "Error while returning bytes to flow control window");
            }
        }

        public int UnconsumedBytes(IHttp2Stream stream)
        {
            return flowController.UnconsumedBytes(stream);
        }

        public int InitialWindowSize(IHttp2Stream stream)
        {
            return flowController.InitialWindowSize(stream);
        }
    }

    /**
     * Provides the state for stream {@code DATA} frame decompression.
     */
    private sealed class Http2Decompressor
    {
        private readonly EmbeddedChannel decompressor;

        private int compressed;
        private int decompressed;
        private IHttp2Stream stream;
        private int padding;
        private bool dataDecompressed;
        private IChannelHandlerContext targetCtx;

        internal Http2Decompressor(EmbeddedChannel decompressor, IHttp2Connection connection,
                                   IHttp2FrameListener listener)
        {
            this.decompressor = decompressor;
            this.decompressor.Pipeline.AddLast(new DecompressorHandler(this, connection, listener));
        }

        // CLR: the anonymous ChannelInboundHandlerAdapter of the constructor; it reads and writes the fields of the
        // Http2Decompressor it was created for, as the Java inner class does.
        private sealed class DecompressorHandler : ChannelInboundHandlerAdapter
        {
            private readonly Http2Decompressor owner;
            private readonly IHttp2Connection connection;
            private readonly IHttp2FrameListener listener;

            internal DecompressorHandler(Http2Decompressor owner, IHttp2Connection connection,
                                         IHttp2FrameListener listener)
            {
                this.owner = owner;
                this.connection = connection;
                this.listener = listener;
            }

            public override void ChannelRead(IChannelHandlerContext ctx, object msg)
            {
                ByteBuf buf = (ByteBuf) msg;
                if (!buf.IsReadable())
                {
                    buf.Release();
                    return;
                }

                try
                {
                    // Also take padding into account.
                    owner.IncrementDecompressedBytes(owner.padding);

                    owner.IncrementDecompressedBytes(buf.ReadableBytes);
                    // Immediately return the bytes back to the flow controller. ConsumedBytesConverter will convert
                    // from the decompressed amount which the user knows about to the compressed amount which flow
                    // control knows about.
                    connection.Local().FlowController().ConsumeBytes(owner.stream,
                            listener.OnDataRead(owner.targetCtx, owner.stream.Id(), buf, owner.padding, false));
                    owner.padding = 0; // Padding is only communicated once on the first iteration.

                    owner.dataDecompressed = true;
                }
                finally
                {
                    buf.Release();
                }
            }

            public override void ChannelInactive(IChannelHandlerContext ctx)
            {
                listener.OnDataRead(owner.targetCtx, owner.stream.Id(), Unpooled.EmptyBuffer, owner.padding, true);
            }
        }

        /**
         * Release remaining content from the {@link EmbeddedChannel}.
         */
        internal void Cleanup()
        {
            decompressor.FinishAndReleaseAll();
        }

        internal int Decompress(IChannelHandlerContext ctx, IHttp2Stream stream, ByteBuf data, int padding,
                                bool endOfStream)
        {
            int compressedBytes = data.ReadableBytes + padding;
            IncrementCompressedBytes(compressedBytes);
            try
            {
                this.stream = stream;
                this.padding = padding;
                this.dataDecompressed = false;
                this.targetCtx = ctx;

                if (!decompressor.IsOpen)
                {
                    // Directly throw an exception in this case which we will handle in the catch block below.
                    // This is required as otherwise it will impossible for us to know if the used EmbeddedChannel
                    // did throw because it was closed already or because of other reasons. We need this knowledge
                    // to know if we need to call data.release() ourselves after it was retained or not. This is needed
                    // as EmbeddedChannel will not release the buffer if it throws because it was not open.
                    // This mimics what EmbeddedChannel will throw.
                    throw new ClosedChannelException();
                }
                // call retain here as it will call release after its written to the channel
                decompressor.WriteInbound(data.Retain());
                if (endOfStream)
                {
                    decompressor.Finish();

                    if (!dataDecompressed)
                    {
                        // No new decompressed data was extracted from the compressed data. This means the application
                        // could not be provided with data and thus could not return how many bytes were processed.
                        // We will assume there is more data coming which will complete the decompression block.
                        // To allow for more data we return all bytes to the flow control window (so the peer can
                        // send more data).
                        IncrementDecompressedBytes(compressedBytes);
                        return compressedBytes;
                    }
                }
                // We consume bytes each time we call the listener to ensure if multiple frames are decompressed
                // that the bytes are accounted for immediately. Otherwise the user may see an inconsistent state of
                // flow control.
                return 0;
            }
            catch (Exception t)
            {
                // Http2Exception might be thrown by writeInbound(...) or finish().
                if (t is Http2Exception)
                {
                    throw;
                }
                throw StreamError(stream.Id(), INTERNAL_ERROR, t,
                        "Decompressor error detected while delegating data read on streamId %d", stream.Id());
            }
        }
        /**
         * Increment the number of bytes received prior to doing any decompression.
         */
        private void IncrementCompressedBytes(int delta)
        {
            Debug.Assert(delta >= 0);
            compressed += delta;
        }

        /**
         * Increment the number of bytes after the decompression process.
         */
        private void IncrementDecompressedBytes(int delta)
        {
            Debug.Assert(delta >= 0);
            decompressed += delta;
        }

        /**
         * Determines the ratio between {@code numBytes} and {@link Http2Decompressor#decompressed}.
         * This ratio is used to decrement {@link Http2Decompressor#decompressed} and
         * {@link Http2Decompressor#compressed}.
         * @param streamId the stream ID
         * @param decompressedBytes The number of post-decompressed bytes to return to flow control
         * @return The number of pre-decompressed bytes that have been consumed.
         */
        internal int ConsumeBytes(int streamId, int decompressedBytes)
        {
            CheckPositiveOrZero(decompressedBytes, "decompressedBytes");
            if (decompressed - decompressedBytes < 0)
            {
                throw StreamError(streamId, INTERNAL_ERROR,
                        "Attempting to return too many bytes for stream %d. decompressed: %d " +
                                "decompressedBytes: %d", streamId, decompressed, decompressedBytes);
            }
            double consumedRatio = decompressedBytes / (double) decompressed;
            int consumedCompressed = Math.Min(compressed, (int) Math.Ceiling(compressed * consumedRatio));
            if (compressed - consumedCompressed < 0)
            {
                throw StreamError(streamId, INTERNAL_ERROR,
                        "overflow when converting decompressed bytes to compressed bytes for stream %d." +
                                "decompressedBytes: %d decompressed: %d compressed: %d consumedCompressed: %d",
                        streamId, decompressedBytes, decompressed, compressed, consumedCompressed);
            }
            decompressed -= decompressedBytes;
            compressed -= consumedCompressed;

            return consumedCompressed;
        }
    }
}
