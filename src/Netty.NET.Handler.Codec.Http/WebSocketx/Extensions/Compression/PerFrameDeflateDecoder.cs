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

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;

/**
 * Per-frame implementation of deflate decompressor.
 */
// CLR: the package-private class and its constructors are internal; WebSocketExtension.RSV1 is the constant of
// IWebSocketExtension and WebSocketExtensionFilter.NEVER_SKIP the static field of IWebSocketExtensionFilter.
internal class PerFrameDeflateDecoder : DeflateDecoder
{

    /**
     * Constructor
     *
     * @param noContext true to disable context takeover.
     * @param maxAllocation
     *          maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    internal PerFrameDeflateDecoder(bool noContext, int maxAllocation)
        : base(noContext, IWebSocketExtensionFilter.NEVER_SKIP, maxAllocation)
    {
    }

    /**
     * Constructor
     *
     * @param noContext true to disable context takeover.
     * @param extensionDecoderFilter extension decoder filter for per frame deflate decoder.
     * @param maxAllocation
     *            maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    internal PerFrameDeflateDecoder(bool noContext, IWebSocketExtensionFilter extensionDecoderFilter,
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
            return false;
        }

        return (msg is TextWebSocketFrame || msg is BinaryWebSocketFrame ||
                msg is ContinuationWebSocketFrame) &&
               (wsFrame.Rsv() & IWebSocketExtension.RSV1) > 0;
    }

    protected override int NewRsv(WebSocketFrame msg)
    {
        return msg.Rsv() ^ IWebSocketExtension.RSV1;
    }

    protected override bool AppendFrameTail(WebSocketFrame msg)
    {
        return true;
    }

}
