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
 * Per-frame implementation of deflate compressor.
 */
// CLR: the package-private class and its constructors are internal; WebSocketExtension.RSV1 is the constant of
// IWebSocketExtension, WebSocketExtensionFilter.NEVER_SKIP the static field of IWebSocketExtensionFilter and
// content().readableBytes() the ReadableBytes property of Content.
internal class PerFrameDeflateEncoder : DeflateEncoder
{

    /**
     * Constructor
     *
     * @param compressionLevel compression level of the compressor.
     * @param windowSize       maximum size of the window compressor buffer.
     * @param noContext        true to disable context takeover.
     */
    internal PerFrameDeflateEncoder(int compressionLevel, int windowSize, bool noContext)
        : base(compressionLevel, windowSize, noContext, IWebSocketExtensionFilter.NEVER_SKIP)
    {
    }

    /**
     * Constructor
     *
     * @param compressionLevel compression level of the compressor.
     * @param windowSize       maximum size of the window compressor buffer.
     * @param noContext        true to disable context takeover.
     * @param extensionEncoderFilter extension encoder filter for per frame deflate encoder.
     */
    internal PerFrameDeflateEncoder(int compressionLevel, int windowSize, bool noContext,
                           IWebSocketExtensionFilter extensionEncoderFilter)
        : base(compressionLevel, windowSize, noContext, extensionEncoderFilter)
    {
    }

    /**
     * Constructor
     *
     * @param compressionLevel compression level of the compressor.
     * @param windowSize       maximum size of the window compressor buffer.
     * @param memLevel         internal compression state memory level (1..9).
     * @param noContext        true to disable context takeover.
     * @param extensionEncoderFilter extension encoder filter for per frame deflate encoder.
     */
    internal PerFrameDeflateEncoder(int compressionLevel, int windowSize, int memLevel, bool noContext,
                           IWebSocketExtensionFilter extensionEncoderFilter)
        : base(compressionLevel, windowSize, memLevel, noContext, extensionEncoderFilter)
    {
    }

    public override bool AcceptOutboundMessage(object msg)
    {
        if (!base.AcceptOutboundMessage(msg))
        {
            return false;
        }

        WebSocketFrame wsFrame = (WebSocketFrame) msg;
        if (ExtensionEncoderFilter().MustSkip(wsFrame))
        {
            return false;
        }

        return (msg is TextWebSocketFrame || msg is BinaryWebSocketFrame ||
                msg is ContinuationWebSocketFrame) &&
               wsFrame.Content.ReadableBytes > 0 &&
               (wsFrame.Rsv() & IWebSocketExtension.RSV1) == 0;
    }

    protected override int Rsv(WebSocketFrame msg)
    {
        return msg.Rsv() | IWebSocketExtension.RSV1;
    }

    protected override bool RemoveFrameTail(WebSocketFrame msg)
    {
        return true;
    }

}
