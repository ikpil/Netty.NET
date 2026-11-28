/*
 * Copyright 2014 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 *
 */
// CLR: channelRead/exceptionCaught override the Transport ChannelInboundHandlerAdapter members; getMessage() is the
// Message property (never null in .NET, so the null check keeps its Java shape only).
// ctx.writeAndFlush(closeFrame).addListener(ChannelFutureListener.CLOSE) closes ctx.Channel from a synchronous
// continuation of the write Task on TaskScheduler.Default, as codec-base MessageAggregator does
// (docs/codec-base-porting.md, MessageAggregator: "Future listeners are Task continuations that run synchronously").
// With ExecuteSynchronously, "If the antecedent is already complete when the continuation is created, the continuation
// will run on the thread that creates the continuation"
// (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcontinuationoptions?view=net-10.0), as a
// listener added to a completed future runs at once; the EmbeddedChannel completes the flushed write before
// writeAndFlush returns.
public class Utf8FrameValidator : ChannelInboundHandlerAdapter
{

    private readonly bool closeOnProtocolViolation;

    private int fragmentedFramesCount;
    private Utf8Validator utf8Validator;

    public Utf8FrameValidator() : this(true)
    {
    }

    public Utf8FrameValidator(bool closeOnProtocolViolation)
    {
        this.closeOnProtocolViolation = closeOnProtocolViolation;
    }

    // See https://datatracker.ietf.org/doc/html/rfc6455#section-5.5.
    private static bool IsControlFrame(WebSocketFrame frame)
    {
        return frame is CloseWebSocketFrame ||
                frame is PingWebSocketFrame ||
                frame is PongWebSocketFrame;
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is WebSocketFrame)
        {
            WebSocketFrame frame = (WebSocketFrame) msg;

            try
            {
                // Processing for possible fragmented messages for text and binary
                // frames
                if (frame.IsFinalFragment())
                {
                    // Control frames are allowed between fragments
                    // See https://datatracker.ietf.org/doc/html/rfc6455#section-5.5.
                    if (!IsControlFrame(frame))
                    {

                        // Final frame of the sequence.
                        fragmentedFramesCount = 0;

                        // Check text for UTF8 correctness
                        if (frame is TextWebSocketFrame ||
                                (utf8Validator != null && utf8Validator.IsChecking()))
                        {
                            // Check UTF-8 correctness for this payload
                            CheckUTF8String(frame.Content);

                            // This does a second check to make sure UTF-8
                            // correctness for entire text message
                            utf8Validator.Finish();
                        }
                    }
                }
                else
                {
                    // Not final frame so we can expect more frames in the
                    // fragmented sequence
                    if (fragmentedFramesCount == 0)
                    {
                        // First text or binary frame for a fragmented set
                        if (frame is TextWebSocketFrame)
                        {
                            CheckUTF8String(frame.Content);
                        }
                    }
                    else
                    {
                        // Subsequent frames - only check if init frame is text
                        if (utf8Validator != null && utf8Validator.IsChecking())
                        {
                            CheckUTF8String(frame.Content);
                        }
                    }

                    // Increment counter
                    fragmentedFramesCount++;
                }
            }
            catch (CorruptedWebSocketFrameException e)
            {
                ProtocolViolation(ctx, frame, e);
            }
        }

        base.ChannelRead(ctx, msg);
    }

    private void CheckUTF8String(ByteBuf buffer)
    {
        if (utf8Validator == null)
        {
            utf8Validator = new Utf8Validator();
        }

        utf8Validator.Check(buffer);
    }

    private void ProtocolViolation(IChannelHandlerContext ctx, WebSocketFrame frame,
                                   CorruptedWebSocketFrameException ex)
    {
        frame.Release();

        if (closeOnProtocolViolation && ctx.Channel.IsOpen)
        {
            WebSocketCloseStatus closeStatus = ex.CloseStatus();
            string reasonText = ex.Message;
            if (reasonText == null)
            {
                reasonText = closeStatus.ReasonText();
            }

            CloseWebSocketFrame closeFrame = new CloseWebSocketFrame(closeStatus.Code(), reasonText);
            _ = ctx.WriteAndFlushAsync(closeFrame).ContinueWith(_ => ctx.Channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        throw ex;
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        base.ExceptionCaught(ctx, cause);
    }
}
