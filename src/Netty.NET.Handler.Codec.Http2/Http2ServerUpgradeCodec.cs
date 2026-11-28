/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Base64Codec;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Base64Codec.Base64Dialect;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2FrameTypes;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Server-side codec for performing a cleartext upgrade from HTTP/1.x to HTTP/2.
 */
// CLR: HttpServerUpgradeHandler.UpgradeCodec is the nested IUpgradeCodec (I prefix), Collection<CharSequence> is
// ICollection<ICharSequence> and Collections.singletonList is a ReadOnlyCollection over one element
// (docs/codec-http-porting.md, Shared CLR adaptations). ChannelHandler is IChannelHandler and the varargs parameters
// are params arrays. ctx.pipeline(), ctx.name() and ctx.channel() are the Pipeline, Name and Channel properties
// (docs/transport-porting.md, Decisions); ctx.close() discards the returned Task (docs/codec-base-porting.md, Outbound
// operations). catch (Throwable) is catch (Exception) (docs/codec-http-porting.md, Shared CLR adaptations). The
// anonymous Http2FrameAdapter of decodeSettings is the private nested SettingsCopier. The checked "throws" clauses
// have no counterpart.
public class Http2ServerUpgradeCodec : HttpServerUpgradeHandler.IUpgradeCodec
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(Http2ServerUpgradeCodec));
    private static readonly IList<ICharSequence> REQUIRED_UPGRADE_HEADERS =
            new ReadOnlyCollection<ICharSequence>(new[] { HTTP_UPGRADE_SETTINGS_HEADER });
    private static readonly IChannelHandler[] EMPTY_HANDLERS = new IChannelHandler[0];

    private readonly string handlerName;
    private readonly Http2ConnectionHandler connectionHandler;
    private readonly IChannelHandler[] handlers;
    private readonly IHttp2FrameReader frameReader;

    private Http2Settings settings;

    /**
     * Creates the codec using a default name for the connection handler when adding to the
     * pipeline.
     *
     * @param connectionHandler the HTTP/2 connection handler
     */
    public Http2ServerUpgradeCodec(Http2ConnectionHandler connectionHandler)
        : this(null, connectionHandler, EMPTY_HANDLERS)
    {
    }

    // CLR: the constructors Http2ServerUpgradeCodec(Http2MultiplexCodec) and Http2ServerUpgradeCodec(String,
    // Http2MultiplexCodec) are absent. They are not @Deprecated themselves, but their parameter type
    // Http2MultiplexCodec is @Deprecated with its replacement named (Http2FrameCodecBuilder together with
    // Http2MultiplexHandler) and is not ported (owner decision 2026-11-16, deprecated-not-ported), so they cannot
    // exist. A codec built by Http2FrameCodecBuilder is upgraded through the Http2FrameCodec constructor below, with
    // the Http2MultiplexHandler as one of its handlers. Their Javadoc comments are kept in place.
    /**
     * Creates the codec using a default name for the connection handler when adding to the
     * pipeline.
     *
     * @param http2Codec the HTTP/2 multiplexing handler.
     */
    // CLR: Http2ServerUpgradeCodec(Http2MultiplexCodec http2Codec) is not ported (see above).

    /**
     * Creates the codec providing an upgrade to the given handler for HTTP/2.
     *
     * @param handlerName the name of the HTTP/2 connection handler to be used in the pipeline,
     *                    or {@code null} to auto-generate the name
     * @param connectionHandler the HTTP/2 connection handler
     */
    public Http2ServerUpgradeCodec(string handlerName, Http2ConnectionHandler connectionHandler)
        : this(handlerName, connectionHandler, EMPTY_HANDLERS)
    {
    }

    /**
     * Creates the codec providing an upgrade to the given handler for HTTP/2.
     *
     * @param handlerName the name of the HTTP/2 connection handler to be used in the pipeline.
     * @param http2Codec the HTTP/2 multiplexing handler.
     */
    // CLR: Http2ServerUpgradeCodec(String handlerName, Http2MultiplexCodec http2Codec) is not ported (see above).

    /**
     * Creates the codec using a default name for the connection handler when adding to the
     * pipeline.
     *
     * @param http2Codec the HTTP/2 frame handler.
     * @param handlers the handlers that will handle the {@link Http2Frame}s.
     */
    public Http2ServerUpgradeCodec(Http2FrameCodec http2Codec, params IChannelHandler[] handlers)
        : this(null, http2Codec, handlers)
    {
    }

    public Http2ServerUpgradeCodec(string handlerName, Http2ConnectionHandler connectionHandler,
            params IChannelHandler[] handlers)
    {
        this.handlerName = handlerName;
        this.connectionHandler = connectionHandler;
        this.handlers = handlers;
        frameReader = new DefaultHttp2FrameReader();
    }

    public virtual ICollection<ICharSequence> RequiredUpgradeHeaders()
    {
        return REQUIRED_UPGRADE_HEADERS;
    }

    public virtual bool PrepareUpgradeResponse(IChannelHandlerContext ctx, IFullHttpRequest upgradeRequest,
            HttpHeaders headers)
    {
        try
        {
            // Decode the HTTP2-Settings header and set the settings on the handler to make
            // sure everything is fine with the request.
            IList<string> upgradeHeaders = upgradeRequest.Headers().GetAll(HTTP_UPGRADE_SETTINGS_HEADER);
            if (upgradeHeaders.Count != 1)
            {
                throw new ArgumentException("There must be 1 and only 1 "
                        + HTTP_UPGRADE_SETTINGS_HEADER + " header.");
            }
            // CLR: the String header value is passed as a CharSequence, so it is wrapped in StringCharSequence
            // (docs/codec-http-porting.md, Shared CLR adaptations).
            settings = DecodeSettingsHeader(ctx, new StringCharSequence(upgradeHeaders[0]));
            // Everything looks good.
            return true;
        }
        catch (Exception cause)
        {
            logger.Info("{} Error during upgrade to HTTP/2", ctx.Channel, cause);
            return false;
        }
    }

    public virtual void UpgradeTo(IChannelHandlerContext ctx, IFullHttpRequest upgradeRequest)
    {
        try
        {
            // Add the HTTP/2 connection handler to the pipeline immediately following the current handler.
            ctx.Pipeline.AddAfter(ctx.Name, handlerName, connectionHandler);

            // Add also all extra handlers as these may handle events / messages produced by the connectionHandler.
            // See https://github.com/netty/netty/issues/9314
            if (handlers != null)
            {
                string name = ctx.Pipeline.Context(connectionHandler).Name;
                for (int i = handlers.Length - 1; i >= 0; i--)
                {
                    ctx.Pipeline.AddAfter(name, null, handlers[i]);
                }
            }
            connectionHandler.OnHttpServerUpgrade(settings);
        }
        catch (Http2Exception e)
        {
            ctx.FireExceptionCaught(e);
            _ = ctx.CloseAsync();
        }
    }

    /**
     * Decodes the settings header and returns a {@link Http2Settings} object.
     */
    private Http2Settings DecodeSettingsHeader(IChannelHandlerContext ctx, ICharSequence settingsHeader)
    {
        // CLR: CharBuffer.wrap(csq) is a read-only buffer whose "content ... will be the content of the given character
        // sequence" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/CharBuffer.html);
        // ByteBufUtil.EncodeString takes that content as a ReadOnlySpan<char> (src/Netty.NET.Buffer/
        // ByteBufUtil.cs), and CharsetUtil.UTF_8 is Encoding.UTF8.
        ByteBuf header = ByteBufUtil.EncodeString(ctx.Alloc(), settingsHeader.ToString(), Encoding.UTF8);
        try
        {
            // Decode the SETTINGS payload.
            ByteBuf payload = Base64.Decode(header, URL_SAFE);

            // Create an HTTP/2 frame for the settings.
            ByteBuf frame = CreateSettingsFrame(ctx, payload);

            // Decode the SETTINGS frame and return the settings object.
            return DecodeSettings(ctx, frame);
        }
        finally
        {
            header.Release();
        }
    }

    /**
     * Decodes the settings frame and returns the settings.
     */
    private Http2Settings DecodeSettings(IChannelHandlerContext ctx, ByteBuf frame)
    {
        try
        {
            Http2Settings decodedSettings = new Http2Settings();
            frameReader.ReadFrame(ctx, frame, new SettingsCopier(decodedSettings));
            return decodedSettings;
        }
        finally
        {
            frame.Release();
        }
    }

    private sealed class SettingsCopier : Http2FrameAdapter
    {
        private readonly Http2Settings decodedSettings;

        internal SettingsCopier(Http2Settings decodedSettings)
        {
            this.decodedSettings = decodedSettings;
        }

        public override void OnSettingsRead(IChannelHandlerContext ctx, Http2Settings settings)
        {
            decodedSettings.CopyFrom(settings);
        }
    }

    /**
     * Creates an HTTP2-Settings header with the given payload. The payload buffer is released.
     */
    private static ByteBuf CreateSettingsFrame(IChannelHandlerContext ctx, ByteBuf payload)
    {
        ByteBuf frame = ctx.Alloc().Buffer(FRAME_HEADER_LENGTH + payload.ReadableBytes);
        WriteFrameHeader(frame, payload.ReadableBytes, SETTINGS, new Http2Flags(), 0);
        frame.WriteBytes(payload);
        payload.Release();
        return frame;
    }
}
