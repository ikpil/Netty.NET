/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Handler.Codec.Http;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Builder which builds {@link HttpToHttp2ConnectionHandler} objects.
 */
// CLR: the @Override methods widen protected members of AbstractHttp2ConnectionHandlerBuilder to public; a C#
// override "can't change the accessibility of the virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), so they are public
// members declared new that call base ("you explicitly hide a member that a base class provides",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/new-modifier), as
// Http2ConnectionHandlerBuilder does. The @Deprecated initialHuffmanDecodeCapacity(int) override has no
// @deprecated text and is ported with the [Obsolete] text of the member it exposes. SensitivityDetector is
// IHttp2HeadersEncoder.ISensitivityDetector. build(decoder, encoder, initialSettings) is a protected override.
public sealed class HttpToHttp2ConnectionHandlerBuilder :
        AbstractHttp2ConnectionHandlerBuilder<HttpToHttp2ConnectionHandler, HttpToHttp2ConnectionHandlerBuilder>
{

    private HttpScheme httpScheme;

    public new HttpToHttp2ConnectionHandlerBuilder ValidateHeaders(bool validateHeaders)
    {
        return base.ValidateHeaders(validateHeaders);
    }

    public new HttpToHttp2ConnectionHandlerBuilder ValidateRequiredPseudoHeaders(bool validateRequiredPseudoHeaders)
    {
        return base.ValidateRequiredPseudoHeaders(validateRequiredPseudoHeaders);
    }

    public new HttpToHttp2ConnectionHandlerBuilder InitialSettings(Http2Settings settings)
    {
        return base.InitialSettings(settings);
    }

    public new HttpToHttp2ConnectionHandlerBuilder FrameListener(IHttp2FrameListener frameListener)
    {
        return base.FrameListener(frameListener);
    }

    public new HttpToHttp2ConnectionHandlerBuilder GracefulShutdownTimeoutMillis(long gracefulShutdownTimeoutMillis)
    {
        return base.GracefulShutdownTimeoutMillis(gracefulShutdownTimeoutMillis);
    }

    public new HttpToHttp2ConnectionHandlerBuilder Server(bool isServer)
    {
        return base.Server(isServer);
    }

    public new HttpToHttp2ConnectionHandlerBuilder Connection(IHttp2Connection connection)
    {
        return base.Connection(connection);
    }

    public new HttpToHttp2ConnectionHandlerBuilder Codec(IHttp2ConnectionDecoder decoder,
                                                     IHttp2ConnectionEncoder encoder)
    {
        return base.Codec(decoder, encoder);
    }

    public new HttpToHttp2ConnectionHandlerBuilder FrameLogger(Http2FrameLogger frameLogger)
    {
        return base.FrameLogger(frameLogger);
    }

    public new HttpToHttp2ConnectionHandlerBuilder EncoderEnforceMaxConcurrentStreams(
            bool encoderEnforceMaxConcurrentStreams)
    {
        return base.EncoderEnforceMaxConcurrentStreams(encoderEnforceMaxConcurrentStreams);
    }

    public new HttpToHttp2ConnectionHandlerBuilder HeaderSensitivityDetector(
            IHttp2HeadersEncoder.ISensitivityDetector headerSensitivityDetector)
    {
        return base.HeaderSensitivityDetector(headerSensitivityDetector);
    }

    public new HttpToHttp2ConnectionHandlerBuilder EncoderIgnoreMaxHeaderListSize(
            bool encoderIgnoreMaxHeaderListSize)
    {
        return base.EncoderIgnoreMaxHeaderListSize(encoderIgnoreMaxHeaderListSize);
    }

    public new HttpToHttp2ConnectionHandlerBuilder DecoderEnforceMaxConsecutiveEmptyDataFrames(
            int maxConsecutiveEmptyFrames)
    {
        return base.DecoderEnforceMaxConsecutiveEmptyDataFrames(maxConsecutiveEmptyFrames);
    }

    public new HttpToHttp2ConnectionHandlerBuilder DecoderEnforceMaxRstFramesPerWindow(int maxRstFramesPerWindow,
            int secondsPerWindow)
    {
        return base.DecoderEnforceMaxRstFramesPerWindow(maxRstFramesPerWindow, secondsPerWindow);
    }

    public new HttpToHttp2ConnectionHandlerBuilder EncoderEnforceMaxRstFramesPerWindow(int maxRstFramesPerWindow,
            int secondsPerWindow)
    {
        return base.EncoderEnforceMaxRstFramesPerWindow(maxRstFramesPerWindow, secondsPerWindow);
    }

    [Obsolete("Huffman decoding no longer depends on having a decode capacity.")]
    public new HttpToHttp2ConnectionHandlerBuilder InitialHuffmanDecodeCapacity(int initialHuffmanDecodeCapacity)
    {
        return base.InitialHuffmanDecodeCapacity(initialHuffmanDecodeCapacity);
    }

    public new HttpToHttp2ConnectionHandlerBuilder DecoupleCloseAndGoAway(bool decoupleCloseAndGoAway)
    {
        return base.DecoupleCloseAndGoAway(decoupleCloseAndGoAway);
    }

    public new HttpToHttp2ConnectionHandlerBuilder FlushPreface(bool flushPreface)
    {
        return base.FlushPreface(flushPreface);
    }

    /**
     * Add {@code scheme} in {@link Http2Headers} if not already present.
     *
     * @param httpScheme {@link HttpScheme} type
     * @return {@code this}.
     */
    public HttpToHttp2ConnectionHandlerBuilder HttpScheme(HttpScheme httpScheme)
    {
        this.httpScheme = httpScheme;
        return Self();
    }

    public new HttpToHttp2ConnectionHandler Build()
    {
        return base.Build();
    }

    protected override HttpToHttp2ConnectionHandler Build(IHttp2ConnectionDecoder decoder,
                                                 IHttp2ConnectionEncoder encoder, Http2Settings initialSettings)
    {
        return new HttpToHttp2ConnectionHandler(decoder, encoder, initialSettings, IsValidateHeaders(),
                DecoupleCloseAndGoAway(), FlushPreface(), httpScheme);
    }
}
