/*
 * Copyright 2019 The Netty Project
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
using System.Globalization;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http.WebSocketx.WebSocketServerProtocolConfig;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * WebSocket server configuration.
 */
// CLR: java.net.URI is System.Uri (see WebSocketClientHandshaker): URI.create(String) is new Uri(string,
// UriKind.RelativeOrAbsolute), which accepts the relative references java.net.URI accepts and throws UriFormatException
// where URI.create throws IllegalArgumentException, and the URI's toString() is OriginalString. The package-private
// DEFAULT_* constants are internal consts, and the static imports of WebSocketServerProtocolConfig's
// DEFAULT_HANDSHAKE_TIMEOUT_MILLIS/DEFAULT_FORCE_CLOSE_TIMEOUT_MILLIS are using static. The private Builder
// constructors are internal, because a C# enclosing class cannot call the private members of its nested class
// (docs/handler-porting.md, Access and names). checkPositive is the Common ObjectUtil.CheckPositive
// (ArgumentException). The Java toString() text is kept: booleans print as true/false, numbers use the invariant
// culture and a null value prints "null" as in Java string concatenation.
public sealed class WebSocketClientProtocolConfig
{

    internal const bool DEFAULT_PERFORM_MASKING = true;
    internal const bool DEFAULT_ALLOW_MASK_MISMATCH = false;
    internal const bool DEFAULT_HANDLE_CLOSE_FRAMES = true;
    internal const bool DEFAULT_DROP_PONG_FRAMES = true;
    internal const bool DEFAULT_GENERATE_ORIGIN_HEADER = true;
    internal const bool DEFAULT_WITH_UTF8_VALIDATOR = true;

    private readonly Uri webSocketUri;
    private readonly string subprotocol;
    private readonly WebSocketVersion version;
    private readonly bool allowExtensions;
    private readonly HttpHeaders customHeaders;
    private readonly int maxFramePayloadLength;
    private readonly bool performMasking;
    private readonly bool allowMaskMismatch;
    private readonly bool handleCloseFrames;
    private readonly WebSocketCloseStatus sendCloseFrame;
    private readonly bool dropPongFrames;
    private readonly long handshakeTimeoutMillis;
    private readonly long forceCloseTimeoutMillis;
    private readonly bool absoluteUpgradeUrl;
    private readonly bool generateOriginHeader;
    private readonly bool withUTF8Validator;

    private WebSocketClientProtocolConfig(
        Uri webSocketUri,
        string subprotocol,
        WebSocketVersion version,
        bool allowExtensions,
        HttpHeaders customHeaders,
        int maxFramePayloadLength,
        bool performMasking,
        bool allowMaskMismatch,
        bool handleCloseFrames,
        WebSocketCloseStatus sendCloseFrame,
        bool dropPongFrames,
        long handshakeTimeoutMillis,
        long forceCloseTimeoutMillis,
        bool absoluteUpgradeUrl,
        bool generateOriginHeader,
        bool withUTF8Validator
    )
    {
        this.webSocketUri = webSocketUri;
        this.subprotocol = subprotocol;
        this.version = version;
        this.allowExtensions = allowExtensions;
        this.customHeaders = customHeaders;
        this.maxFramePayloadLength = maxFramePayloadLength;
        this.performMasking = performMasking;
        this.allowMaskMismatch = allowMaskMismatch;
        this.forceCloseTimeoutMillis = forceCloseTimeoutMillis;
        this.handleCloseFrames = handleCloseFrames;
        this.sendCloseFrame = sendCloseFrame;
        this.dropPongFrames = dropPongFrames;
        this.handshakeTimeoutMillis = ObjectUtil.CheckPositive(handshakeTimeoutMillis, "handshakeTimeoutMillis");
        this.absoluteUpgradeUrl = absoluteUpgradeUrl;
        this.generateOriginHeader = generateOriginHeader;
        this.withUTF8Validator = withUTF8Validator;
    }

    public Uri WebSocketUri()
    {
        return webSocketUri;
    }

    public string Subprotocol()
    {
        return subprotocol;
    }

    public WebSocketVersion Version()
    {
        return version;
    }

    public bool AllowExtensions()
    {
        return allowExtensions;
    }

    public HttpHeaders CustomHeaders()
    {
        return customHeaders;
    }

    public int MaxFramePayloadLength()
    {
        return maxFramePayloadLength;
    }

    public bool PerformMasking()
    {
        return performMasking;
    }

    public bool AllowMaskMismatch()
    {
        return allowMaskMismatch;
    }

    public bool HandleCloseFrames()
    {
        return handleCloseFrames;
    }

    public WebSocketCloseStatus SendCloseFrame()
    {
        return sendCloseFrame;
    }

    public bool DropPongFrames()
    {
        return dropPongFrames;
    }

    public long HandshakeTimeoutMillis()
    {
        return handshakeTimeoutMillis;
    }

    public long ForceCloseTimeoutMillis()
    {
        return forceCloseTimeoutMillis;
    }

    public bool AbsoluteUpgradeUrl()
    {
        return absoluteUpgradeUrl;
    }

    public bool GenerateOriginHeader()
    {
        return generateOriginHeader;
    }

    public bool WithUTF8Validator()
    {
        return withUTF8Validator;
    }

    public override string ToString()
    {
        return "WebSocketClientProtocolConfig" +
               " {webSocketUri=" + (webSocketUri?.OriginalString ?? "null") +
               ", subprotocol=" + (subprotocol ?? "null") +
               ", version=" + (version?.ToString() ?? "null") +
               ", allowExtensions=" + (allowExtensions ? "true" : "false") +
               ", customHeaders=" + (customHeaders?.ToString() ?? "null") +
               ", maxFramePayloadLength=" + maxFramePayloadLength.ToString(CultureInfo.InvariantCulture) +
               ", performMasking=" + (performMasking ? "true" : "false") +
               ", allowMaskMismatch=" + (allowMaskMismatch ? "true" : "false") +
               ", handleCloseFrames=" + (handleCloseFrames ? "true" : "false") +
               ", sendCloseFrame=" + (sendCloseFrame?.ToString() ?? "null") +
               ", dropPongFrames=" + (dropPongFrames ? "true" : "false") +
               ", handshakeTimeoutMillis=" + handshakeTimeoutMillis.ToString(CultureInfo.InvariantCulture) +
               ", forceCloseTimeoutMillis=" + forceCloseTimeoutMillis.ToString(CultureInfo.InvariantCulture) +
               ", absoluteUpgradeUrl=" + (absoluteUpgradeUrl ? "true" : "false") +
               ", generateOriginHeader=" + (generateOriginHeader ? "true" : "false") +
               "}";
    }

    public Builder ToBuilder()
    {
        return new Builder(this);
    }

    public static Builder NewBuilder()
    {
        return new Builder(
                new Uri("https://localhost/", UriKind.RelativeOrAbsolute),
                null,
                WebSocketVersion.V13,
                false,
                EmptyHttpHeaders.INSTANCE,
                65536,
                DEFAULT_PERFORM_MASKING,
                DEFAULT_ALLOW_MASK_MISMATCH,
                DEFAULT_HANDLE_CLOSE_FRAMES,
                WebSocketCloseStatus.NORMAL_CLOSURE,
                DEFAULT_DROP_PONG_FRAMES,
                DEFAULT_HANDSHAKE_TIMEOUT_MILLIS,
                DEFAULT_FORCE_CLOSE_TIMEOUT_MILLIS,
                false,
                DEFAULT_GENERATE_ORIGIN_HEADER,
                DEFAULT_WITH_UTF8_VALIDATOR);
    }

    public sealed class Builder
    {
        private Uri webSocketUri;
        private string subprotocol;
        private WebSocketVersion version;
        private bool allowExtensions;
        private HttpHeaders customHeaders;
        private int maxFramePayloadLength;
        private bool performMasking;
        private bool allowMaskMismatch;
        private bool handleCloseFrames;
        private WebSocketCloseStatus sendCloseFrame;
        private bool dropPongFrames;
        private long handshakeTimeoutMillis;
        private long forceCloseTimeoutMillis;
        private bool absoluteUpgradeUrl;
        private bool generateOriginHeader;
        private bool withUTF8Validator;

        internal Builder(WebSocketClientProtocolConfig clientConfig)
            : this(ObjectUtil.CheckNotNull(clientConfig, "clientConfig").WebSocketUri(),
                 clientConfig.Subprotocol(),
                 clientConfig.Version(),
                 clientConfig.AllowExtensions(),
                 clientConfig.CustomHeaders(),
                 clientConfig.MaxFramePayloadLength(),
                 clientConfig.PerformMasking(),
                 clientConfig.AllowMaskMismatch(),
                 clientConfig.HandleCloseFrames(),
                 clientConfig.SendCloseFrame(),
                 clientConfig.DropPongFrames(),
                 clientConfig.HandshakeTimeoutMillis(),
                 clientConfig.ForceCloseTimeoutMillis(),
                 clientConfig.AbsoluteUpgradeUrl(),
                 clientConfig.GenerateOriginHeader(),
                 clientConfig.WithUTF8Validator())
        {
        }

        internal Builder(Uri webSocketUri,
                        string subprotocol,
                        WebSocketVersion version,
                        bool allowExtensions,
                        HttpHeaders customHeaders,
                        int maxFramePayloadLength,
                        bool performMasking,
                        bool allowMaskMismatch,
                        bool handleCloseFrames,
                        WebSocketCloseStatus sendCloseFrame,
                        bool dropPongFrames,
                        long handshakeTimeoutMillis,
                        long forceCloseTimeoutMillis,
                        bool absoluteUpgradeUrl,
                        bool generateOriginHeader,
                        bool withUTF8Validator)
        {
            this.webSocketUri = webSocketUri;
            this.subprotocol = subprotocol;
            this.version = version;
            this.allowExtensions = allowExtensions;
            this.customHeaders = customHeaders;
            this.maxFramePayloadLength = maxFramePayloadLength;
            this.performMasking = performMasking;
            this.allowMaskMismatch = allowMaskMismatch;
            this.handleCloseFrames = handleCloseFrames;
            this.sendCloseFrame = sendCloseFrame;
            this.dropPongFrames = dropPongFrames;
            this.handshakeTimeoutMillis = handshakeTimeoutMillis;
            this.forceCloseTimeoutMillis = forceCloseTimeoutMillis;
            this.absoluteUpgradeUrl = absoluteUpgradeUrl;
            this.generateOriginHeader = generateOriginHeader;
            this.withUTF8Validator = withUTF8Validator;
        }

        /**
         * URL for web socket communications. e.g "ws://myhost.com/mypath". Subsequent web socket frames will be
         * sent to this URL.
         */
        public Builder WebSocketUri(string webSocketUri)
        {
            return WebSocketUri(new Uri(webSocketUri, UriKind.RelativeOrAbsolute));
        }

        /**
         * URL for web socket communications. e.g "ws://myhost.com/mypath". Subsequent web socket frames will be
         * sent to this URL.
         */
        public Builder WebSocketUri(Uri webSocketUri)
        {
            this.webSocketUri = webSocketUri;
            return this;
        }

        /**
         * Sub protocol request sent to the server.
         */
        public Builder Subprotocol(string subprotocol)
        {
            this.subprotocol = subprotocol;
            return this;
        }

        /**
         * Version of web socket specification to use to connect to the server
         */
        public Builder Version(WebSocketVersion version)
        {
            this.version = version;
            return this;
        }

        /**
         * Allow extensions to be used in the reserved bits of the web socket frame
         */
        public Builder AllowExtensions(bool allowExtensions)
        {
            this.allowExtensions = allowExtensions;
            return this;
        }

        /**
         * Map of custom headers to add to the client request
         */
        public Builder CustomHeaders(HttpHeaders customHeaders)
        {
            this.customHeaders = customHeaders;
            return this;
        }

        /**
         * Maximum length of a frame's payload
         */
        public Builder MaxFramePayloadLength(int maxFramePayloadLength)
        {
            this.maxFramePayloadLength = maxFramePayloadLength;
            return this;
        }

        /**
         * Whether to mask all written websocket frames. This must be set to true in order to be fully compatible
         * with the websocket specifications. Client applications that communicate with a non-standard server
         * which doesn't require masking might set this to false to achieve a higher performance.
         */
        public Builder PerformMasking(bool performMasking)
        {
            this.performMasking = performMasking;
            return this;
        }

        /**
         * When set to true, frames which are not masked properly according to the standard will still be accepted.
         */
        public Builder AllowMaskMismatch(bool allowMaskMismatch)
        {
            this.allowMaskMismatch = allowMaskMismatch;
            return this;
        }

        /**
         * {@code true} if close frames should not be forwarded and just close the channel
         */
        public Builder HandleCloseFrames(bool handleCloseFrames)
        {
            this.handleCloseFrames = handleCloseFrames;
            return this;
        }

        /**
         * Close frame to send, when close frame was not send manually. Or {@code null} to disable proper close.
         */
        public Builder SendCloseFrame(WebSocketCloseStatus sendCloseFrame)
        {
            this.sendCloseFrame = sendCloseFrame;
            return this;
        }

        /**
         * {@code true} if pong frames should not be forwarded
         */
        public Builder DropPongFrames(bool dropPongFrames)
        {
            this.dropPongFrames = dropPongFrames;
            return this;
        }

        /**
         * Handshake timeout in mills, when handshake timeout, will trigger user
         * event {@link ClientHandshakeStateEvent#HANDSHAKE_TIMEOUT}
         */
        public Builder HandshakeTimeoutMillis(long handshakeTimeoutMillis)
        {
            this.handshakeTimeoutMillis = handshakeTimeoutMillis;
            return this;
        }

        /**
         * Close the connection if it was not closed by the server after timeout specified
         */
        public Builder ForceCloseTimeoutMillis(long forceCloseTimeoutMillis)
        {
            this.forceCloseTimeoutMillis = forceCloseTimeoutMillis;
            return this;
        }

        /**
         * Use an absolute url for the Upgrade request, typically when connecting through an HTTP proxy over clear HTTP
         */
        public Builder AbsoluteUpgradeUrl(bool absoluteUpgradeUrl)
        {
            this.absoluteUpgradeUrl = absoluteUpgradeUrl;
            return this;
        }

        /**
         * Allows to generate the `Origin`|`Sec-WebSocket-Origin` header value for handshake request
         * according the given webSocketURI. Usually it's not necessary and can be disabled,
         * but for backward compatibility is set to {@code true} as default.
         */
        public Builder GenerateOriginHeader(bool generateOriginHeader)
        {
            this.generateOriginHeader = generateOriginHeader;
            return this;
        }

        /**
         * Toggles UTF8 validation for payload of text websocket frames. By default validation is enabled.
         */
        public Builder WithUTF8Validator(bool withUTF8Validator)
        {
            this.withUTF8Validator = withUTF8Validator;
            return this;
        }

        /**
         * Build unmodifiable client protocol configuration.
         */
        public WebSocketClientProtocolConfig Build()
        {
            return new WebSocketClientProtocolConfig(
                webSocketUri,
                subprotocol,
                version,
                allowExtensions,
                customHeaders,
                maxFramePayloadLength,
                performMasking,
                allowMaskMismatch,
                handleCloseFrames,
                sendCloseFrame,
                dropPongFrames,
                handshakeTimeoutMillis,
                forceCloseTimeoutMillis,
                absoluteUpgradeUrl,
                generateOriginHeader,
                withUTF8Validator
            );
        }
    }
}
