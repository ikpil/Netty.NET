/*
 * Copyright 2020 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Codec.Http3.Http3FrameValidationUtils;
using static Netty.NET.Handler.Codec.Http3.Http3RequestStreamValidationUtils;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is an internal sealed class. BooleanSupplier is Func<bool> and the lambda
// () -> false of newServerValidator a static lambda. write(ctx, frame, promise) and channelRead(ctx, frame) override
// the internal virtual members of Http3FrameTypeDuplexValidationHandler (the promise is the TaskCompletionSource that
// its WriteAsync returns); ctx.write(frame, promise) is Http3CodecUtils.Write. HttpMethod.HEAD.asciiName() is codec-http's
// HttpMethod.HEAD.AsciiName(). The isSharable() override is the IsSharable property of ChannelHandlerAdapter.
internal sealed class Http3RequestStreamValidationHandler : Http3FrameTypeDuplexValidationHandler<IHttp3RequestStreamFrame>
{
    private readonly bool server;
    private readonly Func<bool> goAwayReceivedSupplier;
    private readonly QpackAttributes qpackAttributes;
    private readonly QpackDecoder qpackDecoder;
    private readonly IHttp3RequestStreamCodecState decodeState;
    private readonly IHttp3RequestStreamCodecState encodeState;

    private bool clientHeadRequest;
    private long expectedLength = -1;
    private long seenLength;

    internal static IChannelHandler NewServerValidator(QpackAttributes qpackAttributes, QpackDecoder decoder,
                                                       IHttp3RequestStreamCodecState encodeState,
                                                       IHttp3RequestStreamCodecState decodeState)
    {
        return new Http3RequestStreamValidationHandler(true, static () => false, qpackAttributes, decoder,
                encodeState, decodeState);
    }

    internal static IChannelHandler NewClientValidator(Func<bool> goAwayReceivedSupplier, QpackAttributes qpackAttributes,
                                                       QpackDecoder decoder, IHttp3RequestStreamCodecState encodeState,
                                                       IHttp3RequestStreamCodecState decodeState)
    {
        return new Http3RequestStreamValidationHandler(false, goAwayReceivedSupplier, qpackAttributes, decoder,
                encodeState, decodeState);
    }

    private Http3RequestStreamValidationHandler(bool server, Func<bool> goAwayReceivedSupplier,
                                                QpackAttributes qpackAttributes, QpackDecoder qpackDecoder,
                                                IHttp3RequestStreamCodecState encodeState,
                                                IHttp3RequestStreamCodecState decodeState)
        : base(typeof(IHttp3RequestStreamFrame))
    {
        this.server = server;
        this.goAwayReceivedSupplier = goAwayReceivedSupplier;
        this.qpackAttributes = qpackAttributes;
        this.qpackDecoder = qpackDecoder;
        this.decodeState = decodeState;
        this.encodeState = encodeState;
    }

    internal override void Write(IChannelHandlerContext ctx, IHttp3RequestStreamFrame frame, TaskCompletionSource promise)
    {
        if (!server)
        {
            if (!ValidateClientWrite(frame, promise, ctx, goAwayReceivedSupplier, encodeState))
            {
                return;
            }
            if (frame is IHttp3HeadersFrame)
            {
                clientHeadRequest = HttpMethod.HEAD.AsciiName().Equals(((IHttp3HeadersFrame) frame).Headers().Method());
            }
        }
        Http3CodecUtils.Write(ctx, frame, promise);
    }

    internal override void ChannelRead(IChannelHandlerContext ctx, IHttp3RequestStreamFrame frame)
    {
        if (frame is IHttp3PushPromiseFrame)
        {
            if (server)
            {
                // Server should not receive a push promise
                // https://quicwg.org/base-drafts/draft-ietf-quic-http.html#name-push_promise
                FrameTypeUnexpected(ctx, frame);
            }
            else
            {
                ctx.FireChannelRead(frame);
            }
            return;
        }

        if (frame is IHttp3HeadersFrame)
        {
            IHttp3HeadersFrame headersFrame = (IHttp3HeadersFrame) frame;
            long maybeContentLength = ValidateHeaderFrameRead(headersFrame, ctx, decodeState);
            if (maybeContentLength >= 0)
            {
                expectedLength = maybeContentLength;
            }
            else if (maybeContentLength == INVALID_FRAME_READ)
            {
                return;
            }
        }

        if (frame is IHttp3DataFrame)
        {
            IHttp3DataFrame dataFrame = (IHttp3DataFrame) frame;
            long maybeContentLength = ValidateDataFrameRead(dataFrame, ctx, expectedLength, seenLength,
                    clientHeadRequest);
            if (maybeContentLength >= 0)
            {
                seenLength = maybeContentLength;
            }
            else if (maybeContentLength == INVALID_FRAME_READ)
            {
                return;
            }
        }

        ctx.FireChannelRead(frame);
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt == ChannelInputShutdownReadComplete.INSTANCE)
        {
            SendStreamAbandonedIfRequired(ctx, qpackAttributes, qpackDecoder, decodeState);
            if (!ValidateOnStreamClosure(ctx, expectedLength, seenLength, clientHeadRequest))
            {
                return;
            }
        }
        ctx.FireUserEventTriggered(evt);
    }

    // This handle keeps state so we can't share it.
    public override bool IsSharable => false;
}
