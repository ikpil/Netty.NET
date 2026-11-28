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
using System.Collections.Generic;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;

/**
 * Per-message implementation of deflate decompressor.
 */
// CLR: the package-private class and its constructors are internal; WebSocketExtension.RSV1 is the constant of
// IWebSocketExtension and WebSocketExtensionFilter.NEVER_SKIP the static field of IWebSocketExtensionFilter.
// IllegalStateException is InvalidOperationException; thrown from AcceptInboundMessage, it is wrapped in a
// DecoderException by MessageToMessageDecoder.ChannelRead, as in Java (docs/codec-base-porting.md).
internal class PerMessageDeflateDecoder : DeflateDecoder
{

    private bool compressing;

    /**
     * Constructor
     *
     * @param noContext true to disable context takeover.
     * @param maxAllocation
     *             maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    internal PerMessageDeflateDecoder(bool noContext, int maxAllocation)
        : base(noContext, IWebSocketExtensionFilter.NEVER_SKIP, maxAllocation)
    {
    }

    /**
     * Constructor
     *
     * @param noContext true to disable context takeover.
     * @param extensionDecoderFilter extension decoder for per message deflate decoder.
     * @param maxAllocation
     *            maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    internal PerMessageDeflateDecoder(bool noContext, IWebSocketExtensionFilter extensionDecoderFilter,
                                      int maxAllocation)
        : base(noContext, extensionDecoderFilter, maxAllocation)
    {
    }

    public override bool AcceptInboundMessage(object msg)
    {
        if (!base.AcceptInboundMessage(msg))
        {
            return false;
        }

        WebSocketFrame wsFrame = (WebSocketFrame) msg;
        if (ExtensionDecoderFilter().MustSkip(wsFrame))
        {
            if (compressing)
            {
                throw new InvalidOperationException("Cannot skip per message deflate decoder, compression in progress");
            }

            return false;
        }

        return ((wsFrame is TextWebSocketFrame || wsFrame is BinaryWebSocketFrame) &&
                (wsFrame.Rsv() & IWebSocketExtension.RSV1) > 0) ||
               (wsFrame is ContinuationWebSocketFrame && compressing);
    }

    protected override int NewRsv(WebSocketFrame msg)
    {
        return (msg.Rsv() & IWebSocketExtension.RSV1) > 0?
                msg.Rsv() ^ IWebSocketExtension.RSV1 : msg.Rsv();
    }

    protected override bool AppendFrameTail(WebSocketFrame msg)
    {
        return msg.IsFinalFragment();
    }

    protected override void Decode(IChannelHandlerContext ctx, WebSocketFrame msg,
                          IList<object> @out)
    {
        base.Decode(ctx, msg, @out);

        if (msg.IsFinalFragment())
        {
            compressing = false;
        }
        else if (msg is TextWebSocketFrame || msg is BinaryWebSocketFrame)
        {
            compressing = true;
        }
    }

}
