/*
 * Copyright 2016 The Netty Project
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

using Netty.NET.Transport.Channels;
using Netty.NET.Handler.Codec.Http;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Translates HTTP/1.x object reads into HTTP/2 frames.
 */
// CLR: FullHttpMessage is IFullHttpMessage, whose content() is the Content property; "throws
// Http2Exception"/"throws Exception" have no checked-exception counterpart.
public class InboundHttpToHttp2Adapter : ChannelInboundHandlerAdapter
{
    private readonly IHttp2Connection connection;
    private readonly IHttp2FrameListener listener;

    public InboundHttpToHttp2Adapter(IHttp2Connection connection, IHttp2FrameListener listener)
    {
        this.connection = connection;
        this.listener = listener;
    }

    private static int GetStreamId(IHttp2Connection connection, HttpHeaders httpHeaders)
    {
        return httpHeaders.GetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(),
                                  connection.Remote().IncrementAndGetNextStreamId());
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IFullHttpMessage)
        {
            Handle(ctx, connection, listener, (IFullHttpMessage) msg);
        }
        else
        {
            base.ChannelRead(ctx, msg);
        }
    }

    // note that this may behave strangely when used for the initial upgrade
    // message when using h2c, since that message is ineligible for flow
    // control, but there is not yet an API for signaling that.
    internal static void Handle(IChannelHandlerContext ctx, IHttp2Connection connection,
                              IHttp2FrameListener listener, IFullHttpMessage message)
    {
        try
        {
            int streamId = GetStreamId(connection, message.Headers());
            IHttp2Stream stream = connection.Stream(streamId);
            if (stream == null)
            {
                stream = connection.Remote().CreateStream(streamId, false);
            }

            message.Headers().Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), HttpScheme.HTTP.Name());
            IHttp2Headers messageHeaders = HttpConversionUtil.ToHttp2Headers(message, true);
            bool hasContent = message.Content.IsReadable();
            bool hasTrailers = !message.TrailingHeaders().IsEmpty();
            listener.OnHeadersRead(
                    ctx, streamId, messageHeaders, 0, !(hasContent || hasTrailers));

            if (hasContent)
            {
                listener.OnDataRead(ctx, streamId, message.Content, 0, !hasTrailers);
            }

            if (hasTrailers)
            {
                IHttp2Headers headers = HttpConversionUtil.ToHttp2Headers(message.TrailingHeaders(), true);
                listener.OnHeadersRead(ctx, streamId, headers, 0, true);
            }

            stream.CloseRemoteSide();
        }
        finally
        {
            message.Release();
        }
    }
}
