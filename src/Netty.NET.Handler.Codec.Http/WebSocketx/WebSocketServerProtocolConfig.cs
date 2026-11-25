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

using System.Globalization;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * WebSocket server configuration.
 */
// CLR: the package-private DEFAULT_* constants are internal consts. The private Builder constructors are internal,
// because a C# enclosing class cannot call the private members of its nested class (docs/handler-porting.md, Access and
// names). checkPositive is the Common ObjectUtil.CheckPositive (ArgumentException). The Java toString() text is kept:
// booleans print as true/false (Boolean.ToString gives True/False), numbers use the invariant culture and a null value
// prints "null" as in Java string concatenation.
public sealed class WebSocketServerProtocolConfig
{

    internal const long DEFAULT_HANDSHAKE_TIMEOUT_MILLIS = 10000L;
    internal const long DEFAULT_FORCE_CLOSE_TIMEOUT_MILLIS = 10000L;

    private readonly string websocketPath;
    private readonly string subprotocols;
    private readonly bool checkStartsWith;
    private readonly long handshakeTimeoutMillis;
    private readonly long forceCloseTimeoutMillis;
    private readonly bool handleCloseFrames;
    private readonly WebSocketCloseStatus sendCloseFrame;
    private readonly bool dropPongFrames;
    private readonly WebSocketDecoderConfig decoderConfig;

    private WebSocketServerProtocolConfig(
        string websocketPath,
        string subprotocols,
        bool checkStartsWith,
        long handshakeTimeoutMillis,
        long forceCloseTimeoutMillis,
        bool handleCloseFrames,
        WebSocketCloseStatus sendCloseFrame,
        bool dropPongFrames,
        WebSocketDecoderConfig decoderConfig
    )
    {
        this.websocketPath = websocketPath;
        this.subprotocols = subprotocols;
        this.checkStartsWith = checkStartsWith;
        this.handshakeTimeoutMillis = ObjectUtil.CheckPositive(handshakeTimeoutMillis, "handshakeTimeoutMillis");
        this.forceCloseTimeoutMillis = forceCloseTimeoutMillis;
        this.handleCloseFrames = handleCloseFrames;
        this.sendCloseFrame = sendCloseFrame;
        this.dropPongFrames = dropPongFrames;
        this.decoderConfig = decoderConfig == null ? WebSocketDecoderConfig.DEFAULT : decoderConfig;
    }

    public string WebsocketPath()
    {
        return websocketPath;
    }

    public string Subprotocols()
    {
        return subprotocols;
    }

    public bool CheckStartsWith()
    {
        return checkStartsWith;
    }

    public long HandshakeTimeoutMillis()
    {
        return handshakeTimeoutMillis;
    }

    public long ForceCloseTimeoutMillis()
    {
        return forceCloseTimeoutMillis;
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

    public WebSocketDecoderConfig DecoderConfig()
    {
        return decoderConfig;
    }

    public override string ToString()
    {
        return "WebSocketServerProtocolConfig" +
            " {websocketPath=" + (websocketPath ?? "null") +
            ", subprotocols=" + (subprotocols ?? "null") +
            ", checkStartsWith=" + (checkStartsWith ? "true" : "false") +
            ", handshakeTimeoutMillis=" + handshakeTimeoutMillis.ToString(CultureInfo.InvariantCulture) +
            ", forceCloseTimeoutMillis=" + forceCloseTimeoutMillis.ToString(CultureInfo.InvariantCulture) +
            ", handleCloseFrames=" + (handleCloseFrames ? "true" : "false") +
            ", sendCloseFrame=" + (sendCloseFrame?.ToString() ?? "null") +
            ", dropPongFrames=" + (dropPongFrames ? "true" : "false") +
            ", decoderConfig=" + decoderConfig +
            "}";
    }

    public Builder ToBuilder()
    {
        return new Builder(this);
    }

    public static Builder NewBuilder()
    {
        return new Builder("/", null, false, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS,
            DEFAULT_FORCE_CLOSE_TIMEOUT_MILLIS, true, WebSocketCloseStatus.NORMAL_CLOSURE, true,
            WebSocketDecoderConfig.DEFAULT);
    }

    public sealed class Builder
    {
        private string websocketPath;
        private string subprotocols;
        private bool checkStartsWith;
        private long handshakeTimeoutMillis;
        private long forceCloseTimeoutMillis;
        private bool handleCloseFrames;
        private WebSocketCloseStatus sendCloseFrame;
        private bool dropPongFrames;
        private WebSocketDecoderConfig decoderConfig;
        private WebSocketDecoderConfig.Builder decoderConfigBuilder;

        internal Builder(WebSocketServerProtocolConfig serverConfig)
            : this(ObjectUtil.CheckNotNull(serverConfig, "serverConfig").WebsocketPath(),
                 serverConfig.Subprotocols(),
                 serverConfig.CheckStartsWith(),
                 serverConfig.HandshakeTimeoutMillis(),
                 serverConfig.ForceCloseTimeoutMillis(),
                 serverConfig.HandleCloseFrames(),
                 serverConfig.SendCloseFrame(),
                 serverConfig.DropPongFrames(),
                 serverConfig.DecoderConfig()
            )
        {
        }

        internal Builder(string websocketPath,
                        string subprotocols,
                        bool checkStartsWith,
                        long handshakeTimeoutMillis,
                        long forceCloseTimeoutMillis,
                        bool handleCloseFrames,
                        WebSocketCloseStatus sendCloseFrame,
                        bool dropPongFrames,
                        WebSocketDecoderConfig decoderConfig)
        {
            this.websocketPath = websocketPath;
            this.subprotocols = subprotocols;
            this.checkStartsWith = checkStartsWith;
            this.handshakeTimeoutMillis = handshakeTimeoutMillis;
            this.forceCloseTimeoutMillis = forceCloseTimeoutMillis;
            this.handleCloseFrames = handleCloseFrames;
            this.sendCloseFrame = sendCloseFrame;
            this.dropPongFrames = dropPongFrames;
            this.decoderConfig = decoderConfig;
        }

        /**
         * URI path component to handle websocket upgrade requests on.
         */
        public Builder WebsocketPath(string websocketPath)
        {
            this.websocketPath = websocketPath;
            return this;
        }

        /**
         * CSV of supported protocols
         */
        public Builder Subprotocols(string subprotocols)
        {
            this.subprotocols = subprotocols;
            return this;
        }

        /**
         * {@code true} to handle all requests, where URI path component starts from
         * {@link WebSocketServerProtocolConfig#websocketPath()}, {@code false} for exact match (default).
         */
        public Builder CheckStartsWith(bool checkStartsWith)
        {
            this.checkStartsWith = checkStartsWith;
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
         * Close the connection if it was not closed by the client after timeout specified
         */
        public Builder ForceCloseTimeoutMillis(long forceCloseTimeoutMillis)
        {
            this.forceCloseTimeoutMillis = forceCloseTimeoutMillis;
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
         * Frames decoder configuration.
         */
        public Builder DecoderConfig(WebSocketDecoderConfig decoderConfig)
        {
            this.decoderConfig = decoderConfig == null ? WebSocketDecoderConfig.DEFAULT : decoderConfig;
            this.decoderConfigBuilder = null;
            return this;
        }

        private WebSocketDecoderConfig.Builder DecoderConfigBuilder()
        {
            if (decoderConfigBuilder == null)
            {
                decoderConfigBuilder = decoderConfig.ToBuilder();
            }
            return decoderConfigBuilder;
        }

        public Builder MaxFramePayloadLength(int maxFramePayloadLength)
        {
            DecoderConfigBuilder().MaxFramePayloadLength(maxFramePayloadLength);
            return this;
        }

        public Builder ExpectMaskedFrames(bool expectMaskedFrames)
        {
            DecoderConfigBuilder().ExpectMaskedFrames(expectMaskedFrames);
            return this;
        }

        public Builder AllowMaskMismatch(bool allowMaskMismatch)
        {
            DecoderConfigBuilder().AllowMaskMismatch(allowMaskMismatch);
            return this;
        }

        public Builder AllowExtensions(bool allowExtensions)
        {
            DecoderConfigBuilder().AllowExtensions(allowExtensions);
            return this;
        }

        public Builder CloseOnProtocolViolation(bool closeOnProtocolViolation)
        {
            DecoderConfigBuilder().CloseOnProtocolViolation(closeOnProtocolViolation);
            return this;
        }

        public Builder WithUTF8Validator(bool withUTF8Validator)
        {
            DecoderConfigBuilder().WithUTF8Validator(withUTF8Validator);
            return this;
        }

        /**
         * Build unmodifiable server protocol configuration.
         */
        public WebSocketServerProtocolConfig Build()
        {
            return new WebSocketServerProtocolConfig(
                websocketPath,
                subprotocols,
                checkStartsWith,
                handshakeTimeoutMillis,
                forceCloseTimeoutMillis,
                handleCloseFrames,
                sendCloseFrame,
                dropPongFrames,
                decoderConfigBuilder == null ? decoderConfig : decoderConfigBuilder.Build()
            );
        }
    }
}
