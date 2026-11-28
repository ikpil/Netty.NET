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
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Example.Http2.Http2ExampleUtil;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpUtil;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Example.Http2.Tiles;

/**
 * Handles all the requests for data. It receives a {@link FullHttpRequest},
 * which has been converted by a {@link InboundHttp2ToHttpAdapter} before it
 * arrived here. For further details, check {@link Http2OrHttpHandler} where the
 * pipeline is setup.
 */
// CLR: FullHttpRequest/FullHttpResponse are IFullHttpRequest/IFullHttpResponse, ctx.alloc() is ctx.Alloc() and
// Unpooled.EMPTY_BUFFER is Unpooled.EmptyBuffer; Integer.parseInt is int.Parse with an optional sign and the
// invariant culture. ctx.executor().schedule(Runnable, latency, MILLISECONDS) is ctx.Executor.ScheduleAsync with a
// TimeSpan of the latency (Common's EventExecutorSchedulingExtensions), whose Task is discarded as Java discards
// the ScheduledFuture.
public class Http2RequestHandler : SimpleChannelInboundHandler<IFullHttpRequest>
{

    private const string LATENCY_FIELD_NAME = "latency";
    private const int MIN_LATENCY = 0;
    private const int MAX_LATENCY = 1000;
    private const string IMAGE_COORDINATE_Y = "y";
    private const string IMAGE_COORDINATE_X = "x";

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest request)
    {
        QueryStringDecoder queryString = new QueryStringDecoder(request.Uri());
        string streamId = StreamId(request);
        int latency = ToInt(FirstValue(queryString, LATENCY_FIELD_NAME), 0);
        if (latency < MIN_LATENCY || latency > MAX_LATENCY)
        {
            SendBadRequest(ctx, streamId);
            return;
        }

        string x = FirstValue(queryString, IMAGE_COORDINATE_X);
        string y = FirstValue(queryString, IMAGE_COORDINATE_Y);
        if (x == null || y == null)
        {
            HandlePage(ctx, streamId, latency, request);
        }
        else
        {
            HandleImage(x, y, ctx, streamId, latency, request);
        }
    }

    private static void SendBadRequest(IChannelHandlerContext ctx, string streamId)
    {
        IFullHttpResponse response = new DefaultFullHttpResponse(HTTP_1_1, BAD_REQUEST, EmptyBuffer);
        StreamId(response, streamId);
        _ = ctx.WriteAndFlushAsync(response);
    }

    private void HandleImage(string x, string y, IChannelHandlerContext ctx, string streamId, int latency,
            IFullHttpRequest request)
    {
        ByteBuf image = ImageCache.INSTANCE.Image(ParseInt(x), ParseInt(y));
        IFullHttpResponse response = new DefaultFullHttpResponse(HTTP_1_1, OK, image.Duplicate());
        response.Headers().Set(CONTENT_TYPE, "image/jpeg");
        SendResponse(ctx, streamId, latency, response, request);
    }

    // CLR-only: the statically imported Integer.parseInt.
    private static int ParseInt(string s)
    {
        return int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private void HandlePage(IChannelHandlerContext ctx, string streamId, int latency, IFullHttpRequest request)
    {
        byte[] body = Html.Body(latency);
        ByteBuf content = ctx.Alloc().Buffer(Html.HEADER.Length + body.Length + Html.FOOTER.Length);
        content.WriteBytes(Html.HEADER);
        content.WriteBytes(body);
        content.WriteBytes(Html.FOOTER);
        IFullHttpResponse response = new DefaultFullHttpResponse(HTTP_1_1, OK, content);
        response.Headers().Set(CONTENT_TYPE, "text/html; charset=UTF-8");
        SendResponse(ctx, streamId, latency, response, request);
    }

    protected virtual void SendResponse(IChannelHandlerContext ctx, string streamId, int latency,
            IFullHttpResponse response, IFullHttpRequest request)
    {
        SetContentLength(response, response.Content.ReadableBytes);
        StreamId(response, streamId);
        _ = ctx.Executor.ScheduleAsync(() =>
        {
            _ = ctx.WriteAndFlushAsync(response);
        }, TimeSpan.FromMilliseconds(latency));
    }

    private static string StreamId(IFullHttpRequest request)
    {
        return request.Headers().Get(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text());
    }

    private static void StreamId(IFullHttpResponse response, string streamId)
    {
        response.Headers().Set(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), streamId);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
