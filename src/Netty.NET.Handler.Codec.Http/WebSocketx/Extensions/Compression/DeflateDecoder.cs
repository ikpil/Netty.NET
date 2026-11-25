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

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;

/**
 * Deflate implementation of a payload decompressor for
 * <tt>io.netty.handler.codec.http.websocketx.WebSocketFrame</tt>.
 */
// CLR: the package-private abstract class and its constructor, FRAME_TAIL and EMPTY_DEFLATE_BLOCK are internal. The
// (byte) 0xff octets of FRAME_TAIL are the unsigned 0xFF of a C# byte, the same bits (Java byte sign rule: the bytes
// are only written, never compared as numbers). EmbeddedChannel.builder() is new EmbeddedChannel.Builder(), and
// ZlibCodecFactory.newZlibDecoder(ZlibWrapper.NONE, maxAllocation) is codec-compression's JdkZlibDecoder over the BCL
// raw DeflateStream (whole-buffer feeding for NONE, docs/codec-compression-porting.md). readInbound() is
// ReadInbound<ByteBuf>(), compositeBuffer() is CompositeBuffer(), addComponent(true, buf) is AddComponent(buf, true)
// (the Buffer port's parameter order) and numComponents() is the NumComponents property. EMPTY_DEFLATE_BLOCK.equals is
// ByteBuf.Equals (readable bytes). msg.getClass().getName() is GetType().FullName.
internal abstract class DeflateDecoder : WebSocketExtensionDecoder
{

    internal static readonly ByteBuf FRAME_TAIL = Unpooled.UnreleasableBuffer(
            Unpooled.WrappedBuffer(new byte[] {0x00, 0x00, 0xff, 0xff}))
            .AsReadOnly();

    internal static readonly ByteBuf EMPTY_DEFLATE_BLOCK = Unpooled.UnreleasableBuffer(
            Unpooled.WrappedBuffer(new byte[] { 0x00 }))
            .AsReadOnly();

    private readonly bool noContext;
    private readonly IWebSocketExtensionFilter extensionDecoderFilter;
    private readonly int maxAllocation;

    private EmbeddedChannel decoder;

    /**
     * Constructor
     *
     * @param noContext true to disable context takeover.
     * @param extensionDecoderFilter extension decoder filter.
     */
    internal DeflateDecoder(bool noContext, IWebSocketExtensionFilter extensionDecoderFilter, int maxAllocation)
    {
        this.noContext = noContext;
        this.extensionDecoderFilter = CheckNotNull(extensionDecoderFilter, "extensionDecoderFilter");
        this.maxAllocation = maxAllocation;
    }

    /**
     * Returns the extension decoder filter.
     */
    protected IWebSocketExtensionFilter ExtensionDecoderFilter()
    {
        return extensionDecoderFilter;
    }

    protected abstract bool AppendFrameTail(WebSocketFrame msg);

    protected abstract int NewRsv(WebSocketFrame msg);

    protected override void Decode(IChannelHandlerContext ctx, WebSocketFrame msg, IList<object> @out)
    {
        ByteBuf decompressedContent = DecompressContent(ctx, msg);

        WebSocketFrame outMsg;
        if (msg is TextWebSocketFrame)
        {
            outMsg = new TextWebSocketFrame(msg.IsFinalFragment(), NewRsv(msg), decompressedContent);
        }
        else if (msg is BinaryWebSocketFrame)
        {
            outMsg = new BinaryWebSocketFrame(msg.IsFinalFragment(), NewRsv(msg), decompressedContent);
        }
        else if (msg is ContinuationWebSocketFrame)
        {
            outMsg = new ContinuationWebSocketFrame(msg.IsFinalFragment(), NewRsv(msg), decompressedContent);
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

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        Cleanup();
        base.ChannelInactive(ctx);
    }

    private ByteBuf DecompressContent(IChannelHandlerContext ctx, WebSocketFrame msg)
    {
        if (decoder == null)
        {
            if (!(msg is TextWebSocketFrame) && !(msg is BinaryWebSocketFrame))
            {
                throw new CodecException("unexpected initial frame type: " + msg.GetType().FullName);
            }
            decoder = new EmbeddedChannel.Builder()
                    .Handlers(ZlibCodecFactory.NewZlibDecoder(ZlibWrapper.NONE, maxAllocation))
                    .Build();
        }

        bool readable = msg.Content.IsReadable();
        bool emptyDeflateBlock = EMPTY_DEFLATE_BLOCK.Equals(msg.Content);

        decoder.WriteInbound(msg.Content.Retain());
        if (AppendFrameTail(msg))
        {
            decoder.WriteInbound(FRAME_TAIL.Duplicate());
        }

        CompositeByteBuf compositeDecompressedContent = ctx.Alloc().CompositeBuffer();
        for (;;)
        {
            ByteBuf partUncompressedContent = decoder.ReadInbound<ByteBuf>();
            if (partUncompressedContent == null)
            {
                break;
            }
            if (!partUncompressedContent.IsReadable())
            {
                partUncompressedContent.Release();
                continue;
            }
            compositeDecompressedContent.AddComponent(partUncompressedContent, true);
        }
        // Correctly handle empty frames
        // See https://github.com/netty/netty/issues/4348
        if (!emptyDeflateBlock && readable && compositeDecompressedContent.NumComponents <= 0)
        {
            // Sometimes after fragmentation the last frame
            // May contain left-over data that doesn't affect decompression
            if (!(msg is ContinuationWebSocketFrame))
            {
                compositeDecompressedContent.Release();
                throw new CodecException("cannot read uncompressed buffer");
            }
        }

        if (msg.IsFinalFragment() && noContext)
        {
            Cleanup();
        }

        return compositeDecompressedContent;
    }

    private void Cleanup()
    {
        if (decoder != null)
        {
            // Clean-up the previous encoder if not cleaned up correctly.
            decoder.FinishAndReleaseAll();
            decoder = null;
        }
    }
}
