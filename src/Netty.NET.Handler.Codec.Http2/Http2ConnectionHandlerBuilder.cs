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

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Builder which builds {@link Http2ConnectionHandler} objects.
 */
// CLR: the @Override methods widen protected members of AbstractHttp2ConnectionHandlerBuilder to public; a C# override
// "can't change the accessibility of the virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), so they are public members
// declared new that call base (see AbstractHttp2ConnectionHandlerBuilder). The @Deprecated
// initialHuffmanDecodeCapacity(int) has no @deprecated text and is ported with the [Obsolete] text of the member it
// exposes (owner decision 2026-11-16). SensitivityDetector is IHttp2HeadersEncoder.ISensitivityDetector.
public sealed class Http2ConnectionHandlerBuilder
        : AbstractHttp2ConnectionHandlerBuilder<Http2ConnectionHandler, Http2ConnectionHandlerBuilder>
{

    public new Http2ConnectionHandlerBuilder ValidateHeaders(bool validateHeaders)
    {
        return base.ValidateHeaders(validateHeaders);
    }

    public new Http2ConnectionHandlerBuilder ValidateRequiredPseudoHeaders(bool validateRequiredPseudoHeaders)
    {
        return base.ValidateRequiredPseudoHeaders(validateRequiredPseudoHeaders);
    }

    public new Http2ConnectionHandlerBuilder InitialSettings(Http2Settings settings)
    {
        return base.InitialSettings(settings);
    }

    public new Http2Settings InitialSettings()
    {
        return base.InitialSettings();
    }

    public new Http2ConnectionHandlerBuilder FrameListener(IHttp2FrameListener frameListener)
    {
        return base.FrameListener(frameListener);
    }

    public new Http2ConnectionHandlerBuilder GracefulShutdownTimeoutMillis(long gracefulShutdownTimeoutMillis)
    {
        return base.GracefulShutdownTimeoutMillis(gracefulShutdownTimeoutMillis);
    }

    public new Http2ConnectionHandlerBuilder Server(bool isServer)
    {
        return base.Server(isServer);
    }

    public new Http2ConnectionHandlerBuilder Connection(IHttp2Connection connection)
    {
        return base.Connection(connection);
    }

    public new Http2ConnectionHandlerBuilder MaxReservedStreams(int maxReservedStreams)
    {
        return base.MaxReservedStreams(maxReservedStreams);
    }

    public new Http2ConnectionHandlerBuilder Codec(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder)
    {
        return base.Codec(decoder, encoder);
    }

    public new Http2ConnectionHandlerBuilder FrameLogger(Http2FrameLogger frameLogger)
    {
        return base.FrameLogger(frameLogger);
    }

    public new Http2ConnectionHandlerBuilder EncoderEnforceMaxConcurrentStreams(
            bool encoderEnforceMaxConcurrentStreams)
    {
        return base.EncoderEnforceMaxConcurrentStreams(encoderEnforceMaxConcurrentStreams);
    }

    public new Http2ConnectionHandlerBuilder EncoderIgnoreMaxHeaderListSize(bool encoderIgnoreMaxHeaderListSize)
    {
        return base.EncoderIgnoreMaxHeaderListSize(encoderIgnoreMaxHeaderListSize);
    }

    public new Http2ConnectionHandlerBuilder HeaderSensitivityDetector(IHttp2HeadersEncoder.ISensitivityDetector headerSensitivityDetector)
    {
        return base.HeaderSensitivityDetector(headerSensitivityDetector);
    }

    [Obsolete("Huffman decoding no longer depends on having a decode capacity.")]
    public new Http2ConnectionHandlerBuilder InitialHuffmanDecodeCapacity(int initialHuffmanDecodeCapacity)
    {
        return base.InitialHuffmanDecodeCapacity(initialHuffmanDecodeCapacity);
    }

    public new Http2ConnectionHandlerBuilder DecoupleCloseAndGoAway(bool decoupleCloseAndGoAway)
    {
        return base.DecoupleCloseAndGoAway(decoupleCloseAndGoAway);
    }

    public new Http2ConnectionHandlerBuilder FlushPreface(bool flushPreface)
    {
        return base.FlushPreface(flushPreface);
    }

    public new int DecoderEnforceMaxConsecutiveEmptyDataFrames()
    {
        return base.DecoderEnforceMaxConsecutiveEmptyDataFrames();
    }

    public new Http2ConnectionHandlerBuilder DecoderEnforceMaxConsecutiveEmptyDataFrames(int maxConsecutiveEmptyFrames)
    {
        return base.DecoderEnforceMaxConsecutiveEmptyDataFrames(maxConsecutiveEmptyFrames);
    }

    public new Http2ConnectionHandlerBuilder DecoderEnforceMaxRstFramesPerWindow(
            int maxRstFramesPerWindow, int secondsPerWindow)
    {
        return base.DecoderEnforceMaxRstFramesPerWindow(maxRstFramesPerWindow, secondsPerWindow);
    }

    public new Http2ConnectionHandlerBuilder EncoderEnforceMaxRstFramesPerWindow(
            int maxRstFramesPerWindow, int secondsPerWindow)
    {
        return base.EncoderEnforceMaxRstFramesPerWindow(maxRstFramesPerWindow, secondsPerWindow);
    }

    public new Http2ConnectionHandler Build()
    {
        return base.Build();
    }

    protected override Http2ConnectionHandler Build(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                                           Http2Settings initialSettings)
    {
        return new Http2ConnectionHandler(decoder, encoder, initialSettings, DecoupleCloseAndGoAway(), FlushPreface());
    }
}
