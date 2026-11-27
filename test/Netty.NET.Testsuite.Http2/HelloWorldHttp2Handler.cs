/*
 * Copyright 2017 The Netty Project
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
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;

namespace Netty.NET.Testsuite.Http2;

/**
 * A simple handler that responds with the message "Hello World!".
 */
// CLR: Http2FrameListener is IHttp2FrameListener, implemented by the public members; Http2Headers,
// Http2ConnectionDecoder and Http2ConnectionEncoder take the I prefix, as in codec-http2. FullHttpRequest is
// IFullHttpRequest. request.headers().get(HOST) returns a String that Java keeps as a CharSequence, and request.uri()
// is passed as one; both are wrapped in Common's StringCharSequence (docs/codec-http-porting.md, Shared CLR
// adaptations). CharsetUtil.UTF_8 is Encoding.UTF8. encoder().writeHeaders/writeData are WriteHeadersAsync and
// WriteDataAsync, whose Tasks are discarded as Java discards the futures; ctx.newPromise() is a new
// TaskCompletionSource, as codec-http2's Http2CodecUtil.NewPromise(). cause.printStackTrace() writes the exception to
// Console.Error, as codec-http2's LastInboundHandler. The package-private static field and constructor are internal.
public sealed class HelloWorldHttp2Handler : Http2ConnectionHandler, IHttp2FrameListener
{

    internal static readonly ByteBuf RESPONSE_BYTES = UnreleasableBuffer(
            CopiedBuffer("Hello World", Encoding.UTF8)).AsReadOnly();

    internal HelloWorldHttp2Handler(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                           Http2Settings initialSettings)
        : base(decoder, encoder, initialSettings)
    {
    }

    private static IHttp2Headers Http1HeadersToHttp2Headers(IFullHttpRequest request)
    {
        string host = request.Headers().Get(HttpHeaderNames.HOST);
        IHttp2Headers http2Headers = new DefaultHttp2Headers()
                .Method(HttpMethod.GET.AsciiName())
                .Path(new StringCharSequence(request.Uri()))
                .Scheme(HttpScheme.HTTP.Name());
        if (host != null)
        {
            http2Headers.Authority(new StringCharSequence(host));
        }
        return http2Headers;
    }

    /**
     * Handles the cleartext HTTP upgrade event. If an upgrade occurred, sends a simple response via HTTP/2
     * on stream 1 (the stream specifically reserved for cleartext HTTP upgrade).
     */
    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is HttpServerUpgradeHandler.UpgradeEvent)
        {
            HttpServerUpgradeHandler.UpgradeEvent upgradeEvent =
                    (HttpServerUpgradeHandler.UpgradeEvent) evt;
            OnHeadersRead(ctx, 1, Http1HeadersToHttp2Headers(upgradeEvent.UpgradeRequest()), 0 , true);
        }
        base.UserEventTriggered(ctx, evt);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        base.ExceptionCaught(ctx, cause);
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }

    /**
     * Sends a "Hello World" DATA frame to the client.
     */
    private void SendResponse(IChannelHandlerContext ctx, int streamId, ByteBuf payload)
    {
        // Send a frame for the response status
        IHttp2Headers headers = new DefaultHttp2Headers().Status(OK.CodeAsText());
        _ = Encoder().WriteHeadersAsync(ctx, streamId, headers, 0, false, new TaskCompletionSource());
        _ = Encoder().WriteDataAsync(ctx, streamId, payload, 0, true, new TaskCompletionSource());

        // no need to call flush as channelReadComplete(...) will take care of it.
    }

    public int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding, bool endOfStream)
    {
        int processed = data.ReadableBytes + padding;
        if (endOfStream)
        {
            SendResponse(ctx, streamId, data.Retain());
        }
        return processed;
    }

    public void OnHeadersRead(IChannelHandlerContext ctx, int streamId,
                              IHttp2Headers headers, int padding, bool endOfStream)
    {
        if (endOfStream)
        {
            ByteBuf content = ctx.Alloc().Buffer();
            content.WriteBytes(RESPONSE_BYTES.Duplicate());
            ByteBufUtil.WriteAscii(content, " - via HTTP/2");
            SendResponse(ctx, streamId, content);
        }
    }

    public void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int streamDependency,
                              short weight, bool exclusive, int padding, bool endOfStream)
    {
        OnHeadersRead(ctx, streamId, headers, padding, endOfStream);
    }

    public void OnPriorityRead(IChannelHandlerContext ctx, int streamId, int streamDependency,
                               short weight, bool exclusive)
    {
    }

    public void OnRstStreamRead(IChannelHandlerContext ctx, int streamId, long errorCode)
    {
    }

    public void OnSettingsAckRead(IChannelHandlerContext ctx)
    {
    }

    public void OnSettingsRead(IChannelHandlerContext ctx, Http2Settings settings)
    {
    }

    public void OnPingRead(IChannelHandlerContext ctx, long data)
    {
    }

    public void OnPingAckRead(IChannelHandlerContext ctx, long data)
    {
    }

    public void OnPushPromiseRead(IChannelHandlerContext ctx, int streamId, int promisedStreamId,
                                  IHttp2Headers headers, int padding)
    {
    }

    public void OnGoAwayRead(IChannelHandlerContext ctx, int lastStreamId, long errorCode, ByteBuf debugData)
    {
    }

    public void OnWindowUpdateRead(IChannelHandlerContext ctx, int streamId, int windowSizeIncrement)
    {
    }

    public void OnUnknownFrame(IChannelHandlerContext ctx, byte frameType, int streamId,
                               Http2Flags flags, ByteBuf payload)
    {
    }
}
