/*
 * Copyright 2020 The Netty Project
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
using System.Threading;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Http2.HelloWorld.Frame.Client;

/**
 * Handles HTTP/2 stream frame responses. This is a useful approach if you specifically want to check
 * the main HTTP/2 response DATA/HEADERs, but in this example it's used purely to see whether
 * our request (for a specific stream id) has had a final response (for that same stream id).
 */
// CLR: CountDownLatch is System.Threading.CountdownEvent (countDown() is Signal(), await(5, SECONDS) is Wait of a
// 5 second TimeSpan) and InterruptedException is ThreadInterruptedException; Http2StreamFrame, Http2DataFrame and
// Http2HeadersFrame take the I prefix, as in codec-http2. System.out.println/System.err.println are
// Console.WriteLine/Console.Error.WriteLine, and getMessage() is Message.
public sealed class Http2ClientStreamFrameResponseHandler : SimpleChannelInboundHandler<IHttp2StreamFrame>
{

    private readonly CountdownEvent latch = new CountdownEvent(1);

    protected override void ChannelRead0(IChannelHandlerContext ctx, IHttp2StreamFrame msg)
    {
        Console.WriteLine("Received HTTP/2 'stream' frame: " + msg);

        // isEndStream() is not from a common interface, so we currently must check both
        if (msg is IHttp2DataFrame && ((IHttp2DataFrame) msg).IsEndStream())
        {
            latch.Signal();
        }
        else if (msg is IHttp2HeadersFrame && ((IHttp2HeadersFrame) msg).IsEndStream())
        {
            latch.Signal();
        }
    }

    /**
     * Waits for the latch to be decremented (i.e. for an end of stream message to be received), or for
     * the latch to expire after 5 seconds.
     * @return true if a successful HTTP/2 end of stream message was received.
     */
    public bool ResponseSuccessfullyCompleted()
    {
        try
        {
            return latch.Wait(TimeSpan.FromSeconds(5));
        }
        catch (ThreadInterruptedException ie)
        {
            Console.Error.WriteLine("Latch exception: " + ie.Message);
            return false;
        }
    }

}
