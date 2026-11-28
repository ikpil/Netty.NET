/*
 * Copyright 2021 The Netty Project
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

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http3.Http3FrameValidationUtils;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: ChannelOutboundHandlerAdapter is the Transport's adapter, whose write returns a Task. write(ctx, msg,
// promise) is WriteAsync(ctx, msg, cancellationToken): it creates the promise (Http3CodecUtils.NewPromise()), runs
// the Java body and returns the promise's Task, and super.write(ctx, msg, promise) is Http3CodecUtils.Write(ctx,
// msg, promise) (ChannelOutboundHandlerAdapter.write is ctx.write); as in Java, the promise of an unexpected frame
// is left incomplete after the connection error.
internal sealed class Http3RequestStreamEncodeStateValidator : ChannelOutboundHandlerAdapter,
        IHttp3RequestStreamCodecState
{
    internal enum State
    {
        None,
        Headers,
        FinalHeaders,
        Trailers
    }
    private State state = State.None;

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        TaskCompletionSource promise = Http3CodecUtils.NewPromise();

        if (!(msg is IHttp3RequestStreamFrame))
        {
            Http3CodecUtils.Write(ctx, msg, promise);
            return promise.Task;
        }

        IHttp3RequestStreamFrame frame = (IHttp3RequestStreamFrame) msg;
        State? nextState = EvaluateFrame(state, frame);
        if (nextState == null)
        {
            FrameTypeUnexpected(ctx, msg);
            return promise.Task;
        }

        state = nextState.Value;
        Http3CodecUtils.Write(ctx, msg, promise);
        return promise.Task;
    }

    public bool Started()
    {
        return IsStreamStarted(state);
    }

    public bool ReceivedFinalHeaders()
    {
        return IsFinalHeadersReceived(state);
    }

    public bool Terminated()
    {
        return IsTrailersReceived(state);
    }

    /**
     * Evaluates the passed frame and returns the following:
     * <ul>
     *     <li>Modified {@link State} if the state should be changed.</li>
     *     <li>Same {@link State} as the passed {@code state} if no state change is necessary</li>
     *     <li>{@code null} if the frame is unexpected</li>
     * </ul>
     *
     * @param state Current state.
     * @param frame to evaluate.
     * @return Next {@link State} or {@code null} if the frame is invalid.
     */
    internal static State? EvaluateFrame(State state, IHttp3RequestStreamFrame frame)
    {
        if (frame is IHttp3PushPromiseFrame || frame is IHttp3UnknownFrame)
        {
            // always allow push promise frames.
            return state;
        }

        switch (state)
        {
            case State.None:
            case State.Headers:
                if (!(frame is IHttp3HeadersFrame))
                {
                    return null;
                }

                return IsInformationalResponse((IHttp3HeadersFrame) frame) ? State.Headers : State.FinalHeaders;
            case State.FinalHeaders:
                if (frame is IHttp3HeadersFrame)
                {
                    if (IsInformationalResponse((IHttp3HeadersFrame) frame))
                    {
                        // Information response after final response headers
                        return null;
                    }

                    // trailers
                    return State.Trailers;
                }

                return state;
            case State.Trailers:
                return null;
            default:
                throw new UnreachableException("Unexpected frame state: " + state);
        }
    }

    internal static bool IsStreamStarted(State state)
    {
        return state != State.None;
    }

    internal static bool IsFinalHeadersReceived(State state)
    {
        return IsStreamStarted(state) && state != State.Headers;
    }

    internal static bool IsTrailersReceived(State state)
    {
        return state == State.Trailers;
    }

    private static bool IsInformationalResponse(IHttp3HeadersFrame headersFrame)
    {
        return HttpStatusClass.ValueOf(headersFrame.Headers().Status()) == HttpStatusClass.INFORMATIONAL;
    }
}
