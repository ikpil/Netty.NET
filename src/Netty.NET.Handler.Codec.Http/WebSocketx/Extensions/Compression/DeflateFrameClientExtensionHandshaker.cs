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
using System.Collections.Immutable;
using System.Globalization;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression.DeflateFrameServerExtensionHandshaker;

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;

/**
 * <a href="https://tools.ietf.org/id/draft-tyoshino-hybi-websocket-perframe-deflate-06.txt">perframe-deflate</a>
 * handshake implementation.
 */
// CLR: the three @Deprecated constructors (boolean), (int, boolean) and (int, boolean,
// WebSocketExtensionFilterProvider) name their replacements and are not ported. IllegalArgumentException is
// ArgumentException with the number in the invariant culture; checkPositiveOrZero and checkNotNull are the Common
// ObjectUtil checks. Collections.emptyMap() is ImmutableDictionary<string, string>.Empty, isEmpty() is Count == 0,
// and the implicit RSV1 of the implemented interface is IWebSocketExtension.RSV1.
public sealed class DeflateFrameClientExtensionHandshaker : IWebSocketClientExtensionHandshaker
{

    private readonly int compressionLevel;
    private readonly bool useWebkitExtensionName;
    private readonly IWebSocketExtensionFilterProvider extensionFilterProvider;
    private readonly int maxAllocation;

    /**
     * Constructor with default configuration.
     *
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public DeflateFrameClientExtensionHandshaker(bool useWebkitExtensionName, int maxAllocation)
        : this(6, useWebkitExtensionName, maxAllocation)
    {
    }

    /**
     * Constructor with custom configuration.
     *
     * @param compressionLevel
     *            Compression level between 0 and 9 (default is 6).
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public DeflateFrameClientExtensionHandshaker(int compressionLevel, bool useWebkitExtensionName,
                                                 int maxAllocation)
        : this(compressionLevel, useWebkitExtensionName, IWebSocketExtensionFilterProvider.DEFAULT, maxAllocation)
    {
    }

    /**
     * Constructor with custom configuration.
     *
     * @param compressionLevel
     *            Compression level between 0 and 9 (default is 6).
     * @param extensionFilterProvider
     *            provides client extension filters for per frame deflate encoder and decoder.
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public DeflateFrameClientExtensionHandshaker(int compressionLevel, bool useWebkitExtensionName,
            IWebSocketExtensionFilterProvider extensionFilterProvider, int maxAllocation)
    {
        if (compressionLevel < 0 || compressionLevel > 9)
        {
            throw new ArgumentException(
                    "compressionLevel: " + compressionLevel.ToString(CultureInfo.InvariantCulture) +
                    " (expected: 0-9)");
        }

        this.compressionLevel = compressionLevel;
        this.useWebkitExtensionName = useWebkitExtensionName;
        this.extensionFilterProvider = CheckNotNull(extensionFilterProvider, "extensionFilterProvider");
        this.maxAllocation = CheckPositiveOrZero(maxAllocation, "maxAllocation");
    }

    public WebSocketExtensionData NewRequestData()
    {
        return new WebSocketExtensionData(
                useWebkitExtensionName ? X_WEBKIT_DEFLATE_FRAME_EXTENSION : DEFLATE_FRAME_EXTENSION,
                ImmutableDictionary<string, string>.Empty);
    }

    public IWebSocketClientExtension HandshakeExtension(WebSocketExtensionData extensionData)
    {
        if (!X_WEBKIT_DEFLATE_FRAME_EXTENSION.Equals(extensionData.Name()) &&
            !DEFLATE_FRAME_EXTENSION.Equals(extensionData.Name()))
        {
            return null;
        }

        if (extensionData.Parameters().Count == 0)
        {
            return new DeflateFrameClientExtension(compressionLevel, extensionFilterProvider, maxAllocation);
        }
        else
        {
            return null;
        }
    }

    private class DeflateFrameClientExtension : IWebSocketClientExtension
    {

        private readonly int compressionLevel;
        private readonly IWebSocketExtensionFilterProvider extensionFilterProvider;
        private readonly int maxAllocation;

        internal DeflateFrameClientExtension(int compressionLevel,
                                             IWebSocketExtensionFilterProvider extensionFilterProvider,
                                             int maxAllocation)
        {
            this.compressionLevel = compressionLevel;
            this.extensionFilterProvider = extensionFilterProvider;
            this.maxAllocation = maxAllocation;
        }

        public int Rsv()
        {
            return IWebSocketExtension.RSV1;
        }

        public WebSocketExtensionEncoder NewExtensionEncoder()
        {
            return new PerFrameDeflateEncoder(compressionLevel, 15, false,
                                              extensionFilterProvider.EncoderFilter());
        }

        public WebSocketExtensionDecoder NewExtensionDecoder()
        {
            return new PerFrameDeflateDecoder(false, extensionFilterProvider.DecoderFilter(), maxAllocation);
        }
    }

}
