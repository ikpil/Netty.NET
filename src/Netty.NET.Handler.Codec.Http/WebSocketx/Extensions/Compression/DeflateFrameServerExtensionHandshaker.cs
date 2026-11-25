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

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;

/**
 * <a href="https://tools.ietf.org/id/draft-tyoshino-hybi-websocket-perframe-deflate-06.txt">perframe-deflate</a>
 * handshake implementation.
 */
// CLR: the package-private X_WEBKIT_DEFLATE_FRAME_EXTENSION and DEFLATE_FRAME_EXTENSION are internal constants. The
// four @Deprecated constructors (), (int), (int, WebSocketExtensionFilterProvider) and (int,
// WebSocketExtensionFilterProvider, int) name their replacements and are not ported (owner decision 2026-11-16).
// IllegalArgumentException is ArgumentException with the number in the invariant culture; checkInRange,
// checkPositiveOrZero and checkNotNull are the Common ObjectUtil checks. Collections.emptyMap() is
// ImmutableDictionary<string, string>.Empty, isEmpty() is Count == 0, and the implicit RSV1 of the implemented
// interface is IWebSocketExtension.RSV1. A window size other than 15 or a memory level other than 8 makes the encoder
// fail at the first compressed frame, because jzlib is library-not-ported (see DeflateEncoder).
public sealed class DeflateFrameServerExtensionHandshaker : IWebSocketServerExtensionHandshaker
{

    public const int DEFAULT_COMPRESSION_LEVEL = 6;
    public const int MIN_WINDOW_SIZE = 8;
    public const int MAX_WINDOW_SIZE = 15;
    public const int DEFAULT_MEM_LEVEL = 8;
    public const int MIN_MEM_LEVEL = 1;
    public const int MAX_MEM_LEVEL = 9;

    internal const string X_WEBKIT_DEFLATE_FRAME_EXTENSION = "x-webkit-deflate-frame";
    internal const string DEFLATE_FRAME_EXTENSION = "deflate-frame";

    private readonly int compressionLevel;
    private readonly int windowSize;
    private readonly int memLevel;
    private readonly IWebSocketExtensionFilterProvider extensionFilterProvider;
    private readonly int maxAllocation;

    /**
     * Constructor with custom configuration.
     *
     * @param compressionLevel
     *            Compression level between 0 and 9 (default is 6).
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public DeflateFrameServerExtensionHandshaker(int compressionLevel, int maxAllocation)
        : this(compressionLevel, MAX_WINDOW_SIZE, DEFAULT_MEM_LEVEL, IWebSocketExtensionFilterProvider.DEFAULT,
                maxAllocation)
    {
    }

    /**
     * Constructor with custom configuration including compressor memory limits.
     *
     * @param compressionLevel
     *            Compression level between 0 and 9 (default is 6).
     * @param windowSize
     *            zlib window size in bits (8-15) for the server-side deflater. Lower values reduce
     *            per-connection memory at the cost of compression ratio.
     * @param memLevel
     *            zlib memory level for the server-side deflater (1-9). Lower values reduce per-connection
     *            memory at the cost of compression ratio.
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public DeflateFrameServerExtensionHandshaker(int compressionLevel, int windowSize, int memLevel,
                                                 int maxAllocation)
        : this(compressionLevel, windowSize, memLevel, IWebSocketExtensionFilterProvider.DEFAULT, maxAllocation)
    {
    }

    /**
     * Constructor with full custom configuration.
     *
     * @param compressionLevel
     *            Compression level between 0 and 9 (default is 6).
     * @param windowSize
     *            zlib window size in bits (8-15) for the server-side deflater.
     * @param memLevel
     *            zlib memory level for the server-side deflater (1-9).
     * @param extensionFilterProvider
     *            provides server extension filters for per frame deflate encoder and decoder.
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public DeflateFrameServerExtensionHandshaker(int compressionLevel, int windowSize, int memLevel,
            IWebSocketExtensionFilterProvider extensionFilterProvider,
            int maxAllocation)
    {
        if (compressionLevel < 0 || compressionLevel > 9)
        {
            throw new ArgumentException(
                    "compressionLevel: " + compressionLevel.ToString(CultureInfo.InvariantCulture) +
                    " (expected: 0-9)");
        }
        this.compressionLevel = compressionLevel;
        this.windowSize = CheckInRange(windowSize, MIN_WINDOW_SIZE, MAX_WINDOW_SIZE, "windowSize");
        this.memLevel = CheckInRange(memLevel, MIN_MEM_LEVEL, MAX_MEM_LEVEL, "memLevel");
        this.extensionFilterProvider = CheckNotNull(extensionFilterProvider, "extensionFilterProvider");
        this.maxAllocation = CheckPositiveOrZero(maxAllocation, "maxAllocation");
    }

    public IWebSocketServerExtension HandshakeExtension(WebSocketExtensionData extensionData)
    {
        if (!X_WEBKIT_DEFLATE_FRAME_EXTENSION.Equals(extensionData.Name()) &&
            !DEFLATE_FRAME_EXTENSION.Equals(extensionData.Name()))
        {
            return null;
        }

        if (extensionData.Parameters().Count == 0)
        {
            return new DeflateFrameServerExtension(compressionLevel, windowSize, memLevel, extensionData.Name(),
                                                   extensionFilterProvider, maxAllocation);
        }
        else
        {
            return null;
        }
    }

    private class DeflateFrameServerExtension : IWebSocketServerExtension
    {

        private readonly string extensionName;
        private readonly int compressionLevel;
        private readonly int windowSize;
        private readonly int memLevel;
        private readonly IWebSocketExtensionFilterProvider extensionFilterProvider;
        private readonly int maxAllocation;

        internal DeflateFrameServerExtension(int compressionLevel, int windowSize, int memLevel, string extensionName,
                IWebSocketExtensionFilterProvider extensionFilterProvider,
                int maxAllocation)
        {
            this.extensionName = extensionName;
            this.compressionLevel = compressionLevel;
            this.windowSize = windowSize;
            this.memLevel = memLevel;
            this.extensionFilterProvider = extensionFilterProvider;
            this.maxAllocation = maxAllocation;
        }

        public int Rsv()
        {
            return IWebSocketExtension.RSV1;
        }

        public WebSocketExtensionEncoder NewExtensionEncoder()
        {
            return new PerFrameDeflateEncoder(compressionLevel, windowSize, memLevel, false,
                                              extensionFilterProvider.EncoderFilter());
        }

        public WebSocketExtensionDecoder NewExtensionDecoder()
        {
            return new PerFrameDeflateDecoder(false, extensionFilterProvider.DecoderFilter(), maxAllocation);
        }

        public WebSocketExtensionData NewReponseData()
        {
            return new WebSocketExtensionData(extensionName, ImmutableDictionary<string, string>.Empty);
        }
    }

}
