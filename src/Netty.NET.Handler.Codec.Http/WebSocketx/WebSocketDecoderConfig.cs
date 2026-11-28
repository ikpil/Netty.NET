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
 * Frames decoder configuration.
 */
// CLR: The private Builder constructor is internal, because a C# enclosing class cannot call the private members of its
// nested class (docs/handler-porting.md, Access and names), and the package-private DEFAULT is internal. The Java
// toString() text is kept: the booleans print as true/false (Boolean.ToString gives True/False) and the number uses the
// invariant culture. checkNotNull is the Common ObjectUtil.CheckNotNull (ArgumentNullException).
public sealed class WebSocketDecoderConfig
{

    internal static readonly WebSocketDecoderConfig DEFAULT =
        new WebSocketDecoderConfig(65536, true, false, false, true, true);

    private readonly int maxFramePayloadLength;
    private readonly bool expectMaskedFrames;
    private readonly bool allowMaskMismatch;
    private readonly bool allowExtensions;
    private readonly bool closeOnProtocolViolation;
    private readonly bool withUTF8Validator;

    /**
     * Constructor
     *
     * @param maxFramePayloadLength
     *            Maximum length of a frame's payload. Setting this to an appropriate value for you application
     *            helps check for denial of services attacks.
     * @param expectMaskedFrames
     *            Web socket servers must set this to true processed incoming masked payload. Client implementations
     *            must set this to false.
     * @param allowMaskMismatch
     *            Allows to loosen the masking requirement on received frames. When this is set to false then also
     *            frames which are not masked properly according to the standard will still be accepted.
     * @param allowExtensions
     *            Flag to allow reserved extension bits to be used or not
     * @param closeOnProtocolViolation
     *            Flag to send close frame immediately on any protocol violation.ion.
     * @param withUTF8Validator
     *            Allows you to avoid adding of Utf8FrameValidator to the pipeline on the
     *            WebSocketServerProtocolHandler creation. This is useful (less overhead)
     *            when you use only BinaryWebSocketFrame within your web socket connection.
     */
    private WebSocketDecoderConfig(int maxFramePayloadLength, bool expectMaskedFrames, bool allowMaskMismatch,
                                  bool allowExtensions, bool closeOnProtocolViolation,
                                  bool withUTF8Validator)
    {
        this.maxFramePayloadLength = maxFramePayloadLength;
        this.expectMaskedFrames = expectMaskedFrames;
        this.allowMaskMismatch = allowMaskMismatch;
        this.allowExtensions = allowExtensions;
        this.closeOnProtocolViolation = closeOnProtocolViolation;
        this.withUTF8Validator = withUTF8Validator;
    }

    public int MaxFramePayloadLength()
    {
        return maxFramePayloadLength;
    }

    public bool ExpectMaskedFrames()
    {
        return expectMaskedFrames;
    }

    public bool AllowMaskMismatch()
    {
        return allowMaskMismatch;
    }

    public bool AllowExtensions()
    {
        return allowExtensions;
    }

    public bool CloseOnProtocolViolation()
    {
        return closeOnProtocolViolation;
    }

    public bool WithUTF8Validator()
    {
        return withUTF8Validator;
    }

    public override string ToString()
    {
        return "WebSocketDecoderConfig" +
            " [maxFramePayloadLength=" + maxFramePayloadLength.ToString(CultureInfo.InvariantCulture) +
            ", expectMaskedFrames=" + (expectMaskedFrames ? "true" : "false") +
            ", allowMaskMismatch=" + (allowMaskMismatch ? "true" : "false") +
            ", allowExtensions=" + (allowExtensions ? "true" : "false") +
            ", closeOnProtocolViolation=" + (closeOnProtocolViolation ? "true" : "false") +
            ", withUTF8Validator=" + (withUTF8Validator ? "true" : "false") +
            "]";
    }

    public Builder ToBuilder()
    {
        return new Builder(this);
    }

    public static Builder NewBuilder()
    {
        return new Builder(DEFAULT);
    }

    public sealed class Builder
    {
        private int maxFramePayloadLength;
        private bool expectMaskedFrames;
        private bool allowMaskMismatch;
        private bool allowExtensions;
        private bool closeOnProtocolViolation;
        private bool withUTF8Validator;

        internal Builder(WebSocketDecoderConfig decoderConfig)
        {
            ObjectUtil.CheckNotNull(decoderConfig, "decoderConfig");

            maxFramePayloadLength = decoderConfig.MaxFramePayloadLength();
            expectMaskedFrames = decoderConfig.ExpectMaskedFrames();
            allowMaskMismatch = decoderConfig.AllowMaskMismatch();
            allowExtensions = decoderConfig.AllowExtensions();
            closeOnProtocolViolation = decoderConfig.CloseOnProtocolViolation();
            withUTF8Validator = decoderConfig.WithUTF8Validator();
        }

        public Builder MaxFramePayloadLength(int maxFramePayloadLength)
        {
            this.maxFramePayloadLength = maxFramePayloadLength;
            return this;
        }

        public Builder ExpectMaskedFrames(bool expectMaskedFrames)
        {
            this.expectMaskedFrames = expectMaskedFrames;
            return this;
        }

        public Builder AllowMaskMismatch(bool allowMaskMismatch)
        {
            this.allowMaskMismatch = allowMaskMismatch;
            return this;
        }

        public Builder AllowExtensions(bool allowExtensions)
        {
            this.allowExtensions = allowExtensions;
            return this;
        }

        public Builder CloseOnProtocolViolation(bool closeOnProtocolViolation)
        {
            this.closeOnProtocolViolation = closeOnProtocolViolation;
            return this;
        }

        public Builder WithUTF8Validator(bool withUTF8Validator)
        {
            this.withUTF8Validator = withUTF8Validator;
            return this;
        }

        public WebSocketDecoderConfig Build()
        {
            return new WebSocketDecoderConfig(
                    maxFramePayloadLength, expectMaskedFrames, allowMaskMismatch,
                    allowExtensions, closeOnProtocolViolation, withUTF8Validator);
        }
    }
}
