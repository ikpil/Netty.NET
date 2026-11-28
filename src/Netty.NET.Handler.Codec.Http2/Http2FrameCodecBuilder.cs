/*
 * Copyright 2017 The Netty Project
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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Builder for the {@link Http2FrameCodec}.
 */
// CLR: the @Override methods widen protected members of AbstractHttp2ConnectionHandlerBuilder to public; a C#
// override "can't change the accessibility of the virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), so they are public
// members declared new that call base, as in Http2ConnectionHandlerBuilder (src/Netty.NET.Handler.Codec.Http2/
// Http2ConnectionHandlerBuilder.cs); build() is public new and build(decoder, encoder, initialSettings) a
// protected override. The @Deprecated initialHuffmanDecodeCapacity(int) has no @deprecated text and is ported with
// the [Obsolete] text of the member it exposes. The package-private constructor and frameWriter(Http2FrameWriter)
// are internal (the tests use them); SensitivityDetector is IHttp2HeadersEncoder.ISensitivityDetector and the Long
// maxHeaderListSize() long?.
public class Http2FrameCodecBuilder :
        AbstractHttp2ConnectionHandlerBuilder<Http2FrameCodec, Http2FrameCodecBuilder>
{

    private IHttp2FrameWriter frameWriter;

    /**
     * Allows overriding behavior of existing builder.
     * <p>
     * Users of this constructor are responsible for invoking {@link #server(boolean)} method or overriding
     * {@link #isServer()} method to give the builder information if the {@link Http2Connection}(s) it creates are in
     * server or client mode.
     *
     * @see AbstractHttp2ConnectionHandlerBuilder
     */
    protected Http2FrameCodecBuilder()
    {
    }

    internal Http2FrameCodecBuilder(bool server)
    {
        Server(server);
        // For backwards compatibility we should disable to timeout by default at this layer.
        GracefulShutdownTimeoutMillis(0);
    }

    /**
     * Creates a builder for an HTTP/2 client.
     */
    public static Http2FrameCodecBuilder ForClient()
    {
        return new Http2FrameCodecBuilder(false);
    }

    /**
     * Creates a builder for an HTTP/2 server.
     */
    public static Http2FrameCodecBuilder ForServer()
    {
        return new Http2FrameCodecBuilder(true);
    }

    // For testing only.
    internal Http2FrameCodecBuilder FrameWriter(IHttp2FrameWriter frameWriter)
    {
        this.frameWriter = CheckNotNull(frameWriter, "frameWriter");
        return this;
    }

    public new Http2Settings InitialSettings()
    {
        return base.InitialSettings();
    }

    public new Http2FrameCodecBuilder InitialSettings(Http2Settings settings)
    {
        return base.InitialSettings(settings);
    }

    public new long GracefulShutdownTimeoutMillis()
    {
        return base.GracefulShutdownTimeoutMillis();
    }

    public new Http2FrameCodecBuilder GracefulShutdownTimeoutMillis(long gracefulShutdownTimeoutMillis)
    {
        return base.GracefulShutdownTimeoutMillis(gracefulShutdownTimeoutMillis);
    }

    public new bool IsServer()
    {
        return base.IsServer();
    }

    public new int MaxReservedStreams()
    {
        return base.MaxReservedStreams();
    }

    public new Http2FrameCodecBuilder MaxReservedStreams(int maxReservedStreams)
    {
        return base.MaxReservedStreams(maxReservedStreams);
    }

    public new bool IsValidateHeaders()
    {
        return base.IsValidateHeaders();
    }

    public new Http2FrameCodecBuilder ValidateHeaders(bool validateHeaders)
    {
        return base.ValidateHeaders(validateHeaders);
    }

    public new Http2FrameCodecBuilder ValidateRequiredPseudoHeaders(bool validateRequiredPseudoHeaders)
    {
        return base.ValidateRequiredPseudoHeaders(validateRequiredPseudoHeaders);
    }

    public new Http2FrameLogger FrameLogger()
    {
        return base.FrameLogger();
    }

    public new Http2FrameCodecBuilder FrameLogger(Http2FrameLogger frameLogger)
    {
        return base.FrameLogger(frameLogger);
    }

    public new bool EncoderEnforceMaxConcurrentStreams()
    {
        return base.EncoderEnforceMaxConcurrentStreams();
    }

    public new Http2FrameCodecBuilder EncoderEnforceMaxConcurrentStreams(bool encoderEnforceMaxConcurrentStreams)
    {
        return base.EncoderEnforceMaxConcurrentStreams(encoderEnforceMaxConcurrentStreams);
    }

    public new int EncoderEnforceMaxQueuedControlFrames()
    {
        return base.EncoderEnforceMaxQueuedControlFrames();
    }

    public new Http2FrameCodecBuilder EncoderEnforceMaxQueuedControlFrames(int maxQueuedControlFrames)
    {
        return base.EncoderEnforceMaxQueuedControlFrames(maxQueuedControlFrames);
    }

    public new IHttp2HeadersEncoder.ISensitivityDetector HeaderSensitivityDetector()
    {
        return base.HeaderSensitivityDetector();
    }

    public new Http2FrameCodecBuilder HeaderSensitivityDetector(IHttp2HeadersEncoder.ISensitivityDetector headerSensitivityDetector)
    {
        return base.HeaderSensitivityDetector(headerSensitivityDetector);
    }

    public new Http2FrameCodecBuilder EncoderIgnoreMaxHeaderListSize(bool ignoreMaxHeaderListSize)
    {
        return base.EncoderIgnoreMaxHeaderListSize(ignoreMaxHeaderListSize);
    }

    [Obsolete("Huffman decoding no longer depends on having a decode capacity.")]
    public new Http2FrameCodecBuilder InitialHuffmanDecodeCapacity(int initialHuffmanDecodeCapacity)
    {
        return base.InitialHuffmanDecodeCapacity(initialHuffmanDecodeCapacity);
    }

    public new Http2FrameCodecBuilder AutoAckSettingsFrame(bool autoAckSettings)
    {
        return base.AutoAckSettingsFrame(autoAckSettings);
    }

    public new Http2FrameCodecBuilder AutoAckPingFrame(bool autoAckPingFrame)
    {
        return base.AutoAckPingFrame(autoAckPingFrame);
    }

    public new Http2FrameCodecBuilder DecoupleCloseAndGoAway(bool decoupleCloseAndGoAway)
    {
        return base.DecoupleCloseAndGoAway(decoupleCloseAndGoAway);
    }

    public new Http2FrameCodecBuilder FlushPreface(bool flushPreface)
    {
        return base.FlushPreface(flushPreface);
    }

    public new int DecoderEnforceMaxConsecutiveEmptyDataFrames()
    {
        return base.DecoderEnforceMaxConsecutiveEmptyDataFrames();
    }

    public new Http2FrameCodecBuilder DecoderEnforceMaxConsecutiveEmptyDataFrames(int maxConsecutiveEmptyFrames)
    {
        return base.DecoderEnforceMaxConsecutiveEmptyDataFrames(maxConsecutiveEmptyFrames);
    }

    public new Http2FrameCodecBuilder DecoderEnforceMaxRstFramesPerWindow(int maxRstFramesPerWindow, int secondsPerWindow)
    {
        return base.DecoderEnforceMaxRstFramesPerWindow(maxRstFramesPerWindow, secondsPerWindow);
    }

    public new Http2FrameCodecBuilder EncoderEnforceMaxRstFramesPerWindow(int maxRstFramesPerWindow, int secondsPerWindow)
    {
        return base.EncoderEnforceMaxRstFramesPerWindow(maxRstFramesPerWindow, secondsPerWindow);
    }

    public new int DecoderEnforceMaxSmallContinuationFrames()
    {
        return base.DecoderEnforceMaxSmallContinuationFrames();
    }

    public new Http2FrameCodecBuilder DecoderEnforceMaxSmallContinuationFrames(int maxConsecutiveContinuationsFrames)
    {
        return base.DecoderEnforceMaxSmallContinuationFrames(maxConsecutiveContinuationsFrames);
    }

    /**
     * Build a {@link Http2FrameCodec} object.
     */
    public new Http2FrameCodec Build()
    {
        IHttp2FrameWriter frameWriter = this.frameWriter;
        if (frameWriter != null)
        {
            // This is to support our tests and will never be executed by the user as frameWriter(...)
            // is package-private.
            DefaultHttp2Connection connection = new DefaultHttp2Connection(IsServer(), MaxReservedStreams());
            long? maxHeaderListSize = InitialSettings().MaxHeaderListSize();
            IHttp2FrameReader frameReader = new DefaultHttp2FrameReader(maxHeaderListSize == null ?
                    new DefaultHttp2HeadersDecoder(IsValidateHeaders()) :
                    new DefaultHttp2HeadersDecoder(IsValidateHeaders(), maxHeaderListSize.Value),
                    DecoderEnforceMaxSmallContinuationFrames());

            if (FrameLogger() != null)
            {
                frameWriter = new Http2OutboundFrameLogger(frameWriter, FrameLogger());
                frameReader = new Http2InboundFrameLogger(frameReader, FrameLogger());
            }

            IHttp2ConnectionEncoder encoder = new DefaultHttp2ConnectionEncoder(connection, frameWriter);

            if (EncoderEnforceMaxConcurrentStreams())
            {
                encoder = new StreamBufferingEncoder(encoder);
            }

            IHttp2ConnectionDecoder decoder = new DefaultHttp2ConnectionDecoder(connection, encoder, frameReader,
                    PromisedRequestVerifier(), IsAutoAckSettingsFrame(), IsAutoAckPingFrame(), IsValidateHeaders(),
                    IsValidateRequiredPseudoHeaders());
            int maxConsecutiveEmptyDataFrames = DecoderEnforceMaxConsecutiveEmptyDataFrames();
            if (maxConsecutiveEmptyDataFrames > 0)
            {
                decoder = new Http2EmptyDataFrameConnectionDecoder(decoder, maxConsecutiveEmptyDataFrames);
            }

            return Build(decoder, encoder, InitialSettings());
        }

        return base.Build();
    }

    protected override Http2FrameCodec Build(
            IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder, Http2Settings initialSettings)
    {
        Http2FrameCodec codec = new Http2FrameCodec(encoder, decoder, initialSettings,
                DecoupleCloseAndGoAway(), FlushPreface());
        codec.GracefulShutdownTimeoutMillis(GracefulShutdownTimeoutMillis());
        return codec;
    }
}
