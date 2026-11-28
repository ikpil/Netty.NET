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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Base64Codec;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Common.ReferenceCountUtil;
using static Netty.NET.Handler.Codec.Base64Codec.Base64Dialect;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Client-side cleartext upgrade codec from HTTP to HTTP/2.
 */
// CLR: HttpClientUpgradeHandler.UpgradeCodec is the nested IUpgradeCodec (I prefix), CharSequence is ICharSequence,
// Collection<CharSequence> is ICollection<ICharSequence> and Collections.singletonList is a ReadOnlyCollection over one
// element (docs/codec-http-porting.md, Shared CLR adaptations). ChannelHandler is IChannelHandler; ctx.pipeline() and
// ctx.name() are the Pipeline and Name properties (docs/transport-porting.md, Decisions); ctx.close() discards the
// returned Task (docs/codec-base-porting.md, Outbound operations). checkNotNull's NullPointerException is
// ArgumentNullException (docs/codec-base-porting.md). The checked "throws" clauses have no counterpart.
public class Http2ClientUpgradeCodec : HttpClientUpgradeHandler.IUpgradeCodec
{

    private static readonly IList<ICharSequence> UPGRADE_HEADERS =
            new ReadOnlyCollection<ICharSequence>(new[] { HTTP_UPGRADE_SETTINGS_HEADER });

    private readonly string handlerName;
    private readonly Http2ConnectionHandler connectionHandler;
    private readonly IChannelHandler upgradeToHandler;
    private readonly IChannelHandler http2MultiplexHandler;

    public Http2ClientUpgradeCodec(Http2FrameCodec frameCodec, IChannelHandler upgradeToHandler)
        : this(null, frameCodec, upgradeToHandler)
    {
    }

    public Http2ClientUpgradeCodec(string handlerName, Http2FrameCodec frameCodec, IChannelHandler upgradeToHandler)
        : this(handlerName, (Http2ConnectionHandler) frameCodec, upgradeToHandler, null)
    {
    }

    /**
     * Creates the codec using a default name for the connection handler when adding to the
     * pipeline.
     *
     * @param connectionHandler the HTTP/2 connection handler
     */
    public Http2ClientUpgradeCodec(Http2ConnectionHandler connectionHandler) : this((string) null, connectionHandler)
    {
    }

    /**
     * Creates the codec using a default name for the connection handler when adding to the
     * pipeline.
     *
     * @param connectionHandler the HTTP/2 connection handler
     * @param http2MultiplexHandler the Http2 Multiplexer handler to work with Http2FrameCodec
     */
    public Http2ClientUpgradeCodec(Http2ConnectionHandler connectionHandler,
        Http2MultiplexHandler http2MultiplexHandler) : this((string) null, connectionHandler, http2MultiplexHandler)
    {
    }

    /**
     * Creates the codec providing an upgrade to the given handler for HTTP/2.
     *
     * @param handlerName the name of the HTTP/2 connection handler to be used in the pipeline,
     *                    or {@code null} to auto-generate the name
     * @param connectionHandler the HTTP/2 connection handler
     */
    public Http2ClientUpgradeCodec(string handlerName, Http2ConnectionHandler connectionHandler)
        : this(handlerName, connectionHandler, connectionHandler, null)
    {
    }

    /**
     * Creates the codec providing an upgrade to the given handler for HTTP/2.
     *
     * @param handlerName the name of the HTTP/2 connection handler to be used in the pipeline,
     *                    or {@code null} to auto-generate the name
     * @param connectionHandler the HTTP/2 connection handler
     */
    public Http2ClientUpgradeCodec(string handlerName, Http2ConnectionHandler connectionHandler,
        Http2MultiplexHandler http2MultiplexHandler)
        : this(handlerName, connectionHandler, connectionHandler, http2MultiplexHandler)
    {
    }

    private Http2ClientUpgradeCodec(string handlerName, Http2ConnectionHandler connectionHandler, IChannelHandler
        upgradeToHandler, Http2MultiplexHandler http2MultiplexHandler)
    {
        this.handlerName = handlerName;
        this.connectionHandler = CheckNotNull(connectionHandler, "connectionHandler");
        this.upgradeToHandler = CheckNotNull(upgradeToHandler, "upgradeToHandler");
        this.http2MultiplexHandler = http2MultiplexHandler;
    }

    public virtual ICharSequence Protocol()
    {
        return HTTP_UPGRADE_PROTOCOL_NAME;
    }

    public virtual ICollection<ICharSequence> SetUpgradeHeaders(IChannelHandlerContext ctx,
        IHttpRequest upgradeRequest)
    {
        ICharSequence settingsValue = GetSettingsHeaderValue(ctx);
        upgradeRequest.Headers().Set(HTTP_UPGRADE_SETTINGS_HEADER, settingsValue);
        return UPGRADE_HEADERS;
    }

    public virtual void UpgradeTo(IChannelHandlerContext ctx, IFullHttpResponse upgradeResponse)
    {
        try
        {
            // Add the handler to the pipeline.
            ctx.Pipeline.AddAfter(ctx.Name, handlerName, upgradeToHandler);

            // Add the Http2 Multiplex handler as this handler handle events produced by the connectionHandler.
            // See https://github.com/netty/netty/issues/9495
            if (http2MultiplexHandler != null)
            {
                string name = ctx.Pipeline.Context(connectionHandler).Name;
                ctx.Pipeline.AddAfter(name, null, http2MultiplexHandler);
            }

            // Reserve local stream 1 for the response.
            connectionHandler.OnHttpClientUpgrade();
        }
        catch (Http2Exception e)
        {
            ctx.FireExceptionCaught(e);
            _ = ctx.CloseAsync();
        }
    }

    /**
     * Converts the current settings for the handler to the Base64-encoded representation used in
     * the HTTP2-Settings upgrade header.
     */
    private ICharSequence GetSettingsHeaderValue(IChannelHandlerContext ctx)
    {
        ByteBuf buf = null;
        ByteBuf encodedBuf = null;

        try
        {
            // Get the local settings for the handler.
            Http2Settings settings = connectionHandler.Decoder().LocalSettings();

            // Serialize the payload of the SETTINGS frame.
            int payloadLength = SETTING_ENTRY_LENGTH * settings.Count;
            buf = ctx.Alloc().Buffer(payloadLength);

            // CLR: CharObjectMap.PrimitiveEntry<Long> is KeyValuePair<char, long>: Http2Settings is a
            // Dictionary<char, long> (owner decision 2026-11-19, confirmed 2026-11-25), whose entries enumerate in
            // insertion order where Java's CharObjectHashMap enumerates in hash-slot order, so the entries of the
            // header may come in another order (neither order is defined: docs/codec-http-porting.md, Shared CLR
            // adaptations, LinkedHashMap/HashMap row). Long.intValue() is "a narrowing primitive conversion"
            // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Long.html); the unchecked
            // (int) cast likewise truncates "by discarding its "extra" most significant bits"
            // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions).
            foreach (KeyValuePair<char, long> entry in settings)
            {
                buf.WriteChar(entry.Key);
                buf.WriteInt(unchecked((int) entry.Value));
            }

            // Base64 encode the payload and then convert to a string for the header.
            encodedBuf = Base64.Encode(buf, URL_SAFE);
            // CLR: the String result is returned as a CharSequence, so it is wrapped in StringCharSequence
            // (docs/codec-http-porting.md, Shared CLR adaptations); UTF_8 is Encoding.UTF8.
            return new StringCharSequence(encodedBuf.ToString(Encoding.UTF8));
        }
        finally
        {
            Release(buf);
            Release(encodedBuf);
        }
    }
}
