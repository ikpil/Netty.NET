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

using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Compression;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression.DeflateDecoder;

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;

/**
 * Deflate implementation of a payload compressor for
 * <tt>io.netty.handler.codec.http.websocketx.WebSocketFrame</tt>.
 */
// CLR: the package-private abstract class, its constructors and DEFAULT_MEM_LEVEL are internal. The static import of
// PerMessageDeflateDecoder.* brings in FRAME_TAIL and EMPTY_DEFLATE_BLOCK, which it inherits from DeflateDecoder; the
// using static names DeflateDecoder, the class that declares them. EmbeddedChannel.builder() is new
// EmbeddedChannel.Builder(), readOutbound() is ReadOutbound<ByteBuf>(), compositeBuffer() is CompositeBuffer(),
// addComponent(true, buf) is AddComponent(buf, true) and numComponents() is the NumComponents property.
// ZlibCodecFactory.newZlibEncoder(ZlibWrapper.NONE, compressionLevel, windowSize, memLevel) creates codec-compression's
// JdkZlibEncoder on the BCL raw DeflateStream for windowSize 15 and memLevel 8, the values of every default
// configuration; any other window size or memory level needs jzlib, which is library-not-ported (owner decision
// 2026-11-23), so the factory throws NotSupportedException when the first frame is compressed, where Java without jzlib
// on the class path fails with NoClassDefFoundError (src/Netty.NET.Handler.Codec.Compression/ ZlibCodecFactory.cs,
// docs/codec-compression-porting.md). msg.getClass().getName() is GetType().FullName.
internal abstract class DeflateEncoder : WebSocketExtensionEncoder
{

    internal const int DEFAULT_MEM_LEVEL = 8;

    private readonly int compressionLevel;
    private readonly int windowSize;
    private readonly int memLevel;
    private readonly bool noContext;
    private readonly IWebSocketExtensionFilter extensionEncoderFilter;

    private EmbeddedChannel encoder;

    /**
     * Constructor
     * @param compressionLevel compression level of the compressor.
     * @param windowSize maximum size of the window compressor buffer.
     * @param noContext true to disable context takeover.
     * @param extensionEncoderFilter extension encoder filter.
     */
    internal DeflateEncoder(int compressionLevel, int windowSize, bool noContext,
                   IWebSocketExtensionFilter extensionEncoderFilter)
        : this(compressionLevel, windowSize, DEFAULT_MEM_LEVEL, noContext, extensionEncoderFilter)
    {
    }

    /**
     * Constructor
     * @param compressionLevel compression level of the compressor.
     * @param windowSize maximum size of the window compressor buffer.
     * @param memLevel internal compression state memory level (1..9).
     * @param noContext true to disable context takeover.
     * @param extensionEncoderFilter extension encoder filter.
     */
    internal DeflateEncoder(int compressionLevel, int windowSize, int memLevel, bool noContext,
                   IWebSocketExtensionFilter extensionEncoderFilter)
    {
        this.compressionLevel = compressionLevel;
        this.windowSize = windowSize;
        this.memLevel = memLevel;
        this.noContext = noContext;
        this.extensionEncoderFilter = CheckNotNull(extensionEncoderFilter, "extensionEncoderFilter");
    }

    /**
     * Returns the extension encoder filter.
     */
    protected IWebSocketExtensionFilter ExtensionEncoderFilter()
    {
        return extensionEncoderFilter;
    }

    /**
     * @param msg the current frame.
     * @return the rsv bits to set in the compressed frame.
     */
    protected abstract int Rsv(WebSocketFrame msg);

    /**
     * @param msg the current frame.
     * @return true if compressed payload tail needs to be removed.
     */
    protected abstract bool RemoveFrameTail(WebSocketFrame msg);

    protected override void Encode(IChannelHandlerContext ctx, WebSocketFrame msg, IList<object> @out)
    {
        ByteBuf compressedContent;

        if (msg.Content.IsReadable())
        {
            compressedContent = CompressContent(ctx, msg);
        }
        else if (msg.IsFinalFragment())
        {
            // Set empty DEFLATE block manually for unknown buffer size
            // https://tools.ietf.org/html/rfc7692#section-7.2.3.6
            compressedContent = EMPTY_DEFLATE_BLOCK.Duplicate();

            if (noContext)
            {
                // The message ends here, so the next one must start with an empty sliding window,
                // as when the final fragment has content (see compressContent(...)).
                Cleanup();
            }
        }
        else
        {
            throw new CodecException("cannot compress content buffer");
        }

        WebSocketFrame outMsg;

        if (msg is TextWebSocketFrame)
        {
            outMsg = new TextWebSocketFrame(msg.IsFinalFragment(), Rsv(msg), compressedContent);
        }
        else if (msg is BinaryWebSocketFrame)
        {
            outMsg = new BinaryWebSocketFrame(msg.IsFinalFragment(), Rsv(msg), compressedContent);
        }
        else if (msg is ContinuationWebSocketFrame)
        {
            outMsg = new ContinuationWebSocketFrame(msg.IsFinalFragment(), Rsv(msg), compressedContent);
        }
        else
        {
            throw new CodecException("unexpected frame type: " + msg.GetType().FullName);
        }

        @out.Add(outMsg);
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        Cleanup();
        base.HandlerRemoved(ctx);
    }

    private ByteBuf CompressContent(IChannelHandlerContext ctx, WebSocketFrame msg)
    {
        if (encoder == null)
        {
            encoder = new EmbeddedChannel.Builder()
                    .Handlers(ZlibCodecFactory.NewZlibEncoder(
                            ZlibWrapper.NONE, compressionLevel, windowSize, memLevel))
                    .Build();
        }

        encoder.WriteOutbound(msg.Content.Retain());

        CompositeByteBuf fullCompressedContent = ctx.Alloc().CompositeBuffer();

        for (;;)
        {
            ByteBuf partCompressedContent = encoder.ReadOutbound<ByteBuf>();
            if (partCompressedContent == null)
            {
                break;
            }

            if (!partCompressedContent.IsReadable())
            {
                partCompressedContent.Release();
                continue;
            }

            fullCompressedContent.AddComponent(partCompressedContent, true);
        }

        if (fullCompressedContent.NumComponents <= 0)
        {
            fullCompressedContent.Release();
            throw new CodecException("cannot read compressed buffer");
        }

        if (msg.IsFinalFragment() && noContext)
        {
            Cleanup();
        }

        ByteBuf compressedContent;

        if (RemoveFrameTail(msg))
        {
            int realLength = fullCompressedContent.ReadableBytes - FRAME_TAIL.ReadableBytes;
            compressedContent = fullCompressedContent.Slice(0, realLength);
        }
        else
        {
            compressedContent = fullCompressedContent;
        }

        return compressedContent;
    }

    private void Cleanup()
    {
        if (encoder != null)
        {
            // Clean-up the previous encoder if not cleaned up correctly.
            encoder.FinishAndReleaseAll();
            encoder = null;
        }
    }
}
