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
using System.Text;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Stomp;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Stomp;

/**
 * STOMP client inbound handler implementation, which just passes received messages to listener
 */
// CLR: the Java interfaces StompFrame and StompHeaders are IStompFrame and IStompHeaders, whose header name constants
// are static fields of the interface. The headers hold ICharSequence values (codec-stomp), so a String value is wrapped
// in the Common StringCharSequence, as the codec-stomp tests do. "some payload".getBytes() encodes with the platform
// default charset, UTF-8 since JDK 18 (JEP 400), so it is Encoding.UTF8.GetBytes. IllegalStateException is
// InvalidOperationException, an enum's ToString() is its constant name as Enum.toString() is, writeAndFlush/close
// discard their Tasks as Java discards the futures, and cause.printStackTrace() writes Exception.ToString() to
// Console.Error.
public class StompClientHandler : SimpleChannelInboundHandler<IStompFrame>
{

    private enum ClientState
    {
        AUTHENTICATING,
        AUTHENTICATED,
        SUBSCRIBED,
        DISCONNECTING
    }

    private ClientState state;

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        state = ClientState.AUTHENTICATING;
        IStompFrame connFrame = new DefaultStompFrame(StompCommand.CONNECT);
        connFrame.Headers().Set(IStompHeaders.ACCEPT_VERSION, new StringCharSequence("1.2"));
        connFrame.Headers().Set(IStompHeaders.HOST, new StringCharSequence(StompClient.HOST));
        connFrame.Headers().Set(IStompHeaders.LOGIN, new StringCharSequence(StompClient.LOGIN));
        connFrame.Headers().Set(IStompHeaders.PASSCODE, new StringCharSequence(StompClient.PASSCODE));
        _ = ctx.WriteAndFlushAsync(connFrame);
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IStompFrame frame)
    {
        string subscrReceiptId = "001";
        string disconReceiptId = "002";
        switch (frame.Command())
        {
            case StompCommand.CONNECTED:
                IStompFrame subscribeFrame = new DefaultStompFrame(StompCommand.SUBSCRIBE);
                subscribeFrame.Headers().Set(IStompHeaders.DESTINATION, new StringCharSequence(StompClient.TOPIC));
                subscribeFrame.Headers().Set(IStompHeaders.RECEIPT, new StringCharSequence(subscrReceiptId));
                subscribeFrame.Headers().Set(IStompHeaders.ID, new StringCharSequence("1"));
                Console.WriteLine("connected, sending subscribe frame: " + subscribeFrame);
                state = ClientState.AUTHENTICATED;
                _ = ctx.WriteAndFlushAsync(subscribeFrame);
                break;
            case StompCommand.RECEIPT:
                string receiptHeader = frame.Headers().GetAsString(IStompHeaders.RECEIPT_ID);
                if (state == ClientState.AUTHENTICATED && receiptHeader.Equals(subscrReceiptId))
                {
                    IStompFrame msgFrame = new DefaultStompFrame(StompCommand.SEND);
                    msgFrame.Headers().Set(IStompHeaders.DESTINATION, new StringCharSequence(StompClient.TOPIC));
                    msgFrame.Content.WriteBytes(Encoding.UTF8.GetBytes("some payload"));
                    Console.WriteLine("subscribed, sending message frame: " + msgFrame);
                    state = ClientState.SUBSCRIBED;
                    _ = ctx.WriteAndFlushAsync(msgFrame);
                }
                else if (state == ClientState.DISCONNECTING && receiptHeader.Equals(disconReceiptId))
                {
                    Console.WriteLine("disconnected");
                    _ = ctx.CloseAsync();
                }
                else
                {
                    throw new InvalidOperationException("received: " + frame + ", while internal state is " + state);
                }
                break;
            case StompCommand.MESSAGE:
                if (state == ClientState.SUBSCRIBED)
                {
                    Console.WriteLine("received frame: " + frame);
                    IStompFrame disconnFrame = new DefaultStompFrame(StompCommand.DISCONNECT);
                    disconnFrame.Headers().Set(IStompHeaders.RECEIPT, new StringCharSequence(disconReceiptId));
                    Console.WriteLine("sending disconnect frame: " + disconnFrame);
                    state = ClientState.DISCONNECTING;
                    _ = ctx.WriteAndFlushAsync(disconnFrame);
                }
                break;
            default:
                break;
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
