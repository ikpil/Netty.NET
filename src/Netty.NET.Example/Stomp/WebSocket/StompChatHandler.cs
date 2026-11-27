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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Stomp;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Stomp.IStompHeaders;

namespace Netty.NET.Example.Stomp.WebSocket;

// CLR: @Sharable is the [Sharable] attribute, and channelRead0 is protected as the abstract
// SimpleChannelInboundHandler.ChannelRead0 it overrides (an override cannot change the accessibility). The static
// import of StompHeaders.* is using static IStompHeaders (the header name constants are static fields of the
// interface), and the Java interfaces StompFrame and Set are IStompFrame and ISet. ConcurrentMap/ConcurrentHashMap is
// ConcurrentDictionary: get(key), which returns null for a missing key, is TryGetValue, and putIfAbsent, which returns
// the previous value or null, is GetOrAdd, whose result is the previous value unless it is the set just added. The
// headers hold ICharSequence values (codec-stomp), so a String value is wrapped in the Common StringCharSequence.
// closeFuture().addListener is a continuation of the channel's CloseFuture on its event loop (EventExecutorTaskScheduler,
// the timing of DefaultPromise.notifyListeners()), and ChannelFutureListener.CLOSE a continuation of the write Task
// that closes the context's channel whatever the outcome. In onUnsubscribe, Iterator.remove() is HashSet.Remove followed
// at once by the return, so the enumerator never advances after the change. String.contains is an ordinal Contains,
// UUID.randomUUID().toString() is Guid.NewGuid().ToString() (the same lowercase 8-4-4-4-12 form), Integer.toString is
// ToString with the invariant culture, and CharsetUtil.UTF_8 is Encoding.UTF8.
[Sharable]
public class StompChatHandler : SimpleChannelInboundHandler<IStompFrame>
{

    private readonly ConcurrentDictionary<string, ISet<StompSubscription>> chatDestinations =
            new ConcurrentDictionary<string, ISet<StompSubscription>>();

    protected override void ChannelRead0(IChannelHandlerContext ctx, IStompFrame inboundFrame)
    {
        DecoderResult decoderResult = inboundFrame.DecoderResult();
        if (decoderResult.IsFailure())
        {
            SendErrorFrame("rejected frame", decoderResult.ToString(), ctx);
            return;
        }

        switch (inboundFrame.Command())
        {
        case StompCommand.STOMP:
        case StompCommand.CONNECT:
            OnConnect(ctx, inboundFrame);
            break;
        case StompCommand.SUBSCRIBE:
            OnSubscribe(ctx, inboundFrame);
            break;
        case StompCommand.SEND:
            OnSend(ctx, inboundFrame);
            break;
        case StompCommand.UNSUBSCRIBE:
            OnUnsubscribe(ctx, inboundFrame);
            break;
        case StompCommand.DISCONNECT:
            OnDisconnect(ctx, inboundFrame);
            break;
        default:
            SendErrorFrame("unsupported command",
                           "Received unsupported command " + inboundFrame.Command(), ctx);
            break;
        }
    }

    private void OnSubscribe(IChannelHandlerContext ctx, IStompFrame inboundFrame)
    {
        string destination = inboundFrame.Headers().GetAsString(DESTINATION);
        string subscriptionId = inboundFrame.Headers().GetAsString(ID);

        if (destination == null || subscriptionId == null)
        {
            SendErrorFrame("missed header", "Required 'destination' or 'id' header missed", ctx);
            return;
        }

        ISet<StompSubscription> subscriptions = Get(destination);
        if (subscriptions == null)
        {
            subscriptions = new HashSet<StompSubscription>();
            ISet<StompSubscription> current = chatDestinations.GetOrAdd(destination, subscriptions);
            ISet<StompSubscription> previousSubscriptions = ReferenceEquals(current, subscriptions) ? null : current;
            if (previousSubscriptions != null)
            {
                subscriptions = previousSubscriptions;
            }
        }

        StompSubscription subscription = new StompSubscription(subscriptionId, destination, ctx.Channel);
        if (subscriptions.Contains(subscription))
        {
            SendErrorFrame("duplicate subscription",
                           "Received duplicate subscription id=" + subscriptionId, ctx);
            return;
        }

        subscriptions.Add(subscription);
        IChannel channel = ctx.Channel;
        _ = channel.CloseFuture.ContinueWith(f =>
                Get(subscription.Destination()).Remove(subscription), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));

        string receiptId = inboundFrame.Headers().GetAsString(RECEIPT);
        if (receiptId != null)
        {
            IStompFrame receiptFrame = new DefaultStompFrame(StompCommand.RECEIPT);
            receiptFrame.Headers().Set(RECEIPT_ID, new StringCharSequence(receiptId));
            _ = ctx.WriteAndFlushAsync(receiptFrame);
        }
    }

    private void OnSend(IChannelHandlerContext ctx, IStompFrame inboundFrame)
    {
        string destination = inboundFrame.Headers().GetAsString(DESTINATION);
        if (destination == null)
        {
            SendErrorFrame("missed header", "required 'destination' header missed", ctx);
            return;
        }

        ISet<StompSubscription> subscriptions = Get(destination);
        foreach (StompSubscription subscription in subscriptions)
        {
            _ = subscription.Channel().WriteAndFlushAsync(TransformToMessage(inboundFrame, subscription));
        }
    }

    private void OnUnsubscribe(IChannelHandlerContext ctx, IStompFrame inboundFrame)
    {
        string subscriptionId = inboundFrame.Headers().GetAsString(SUBSCRIPTION);
        foreach (KeyValuePair<string, ISet<StompSubscription>> entry in chatDestinations)
        {
            foreach (StompSubscription subscription in entry.Value)
            {
                if (subscription.Id().Equals(subscriptionId) && subscription.Channel().Equals(ctx.Channel))
                {
                    entry.Value.Remove(subscription);
                    return;
                }
            }
        }
    }

    private static void OnConnect(IChannelHandlerContext ctx, IStompFrame inboundFrame)
    {
        string acceptVersions = inboundFrame.Headers().GetAsString(ACCEPT_VERSION);
        StompVersion handshakeAcceptVersion = ctx.Channel.Attr(StompVersion.CHANNEL_ATTRIBUTE_KEY).Get();
        if (acceptVersions == null || !acceptVersions.Contains(handshakeAcceptVersion.Version(), StringComparison.Ordinal))
        {
            SendErrorFrame("invalid version",
                           "Received invalid version, expected " + handshakeAcceptVersion.Version(), ctx);
            return;
        }

        IStompFrame connectedFrame = new DefaultStompFrame(StompCommand.CONNECTED);
        connectedFrame.Headers()
                      .Set(VERSION, new StringCharSequence(handshakeAcceptVersion.Version()))
                      .Set(SERVER, new StringCharSequence("Netty-Server"))
                      .Set(HEART_BEAT, new StringCharSequence("0,0"));
        _ = ctx.WriteAndFlushAsync(connectedFrame);
    }

    private static void OnDisconnect(IChannelHandlerContext ctx, IStompFrame inboundFrame)
    {
        string receiptId = inboundFrame.Headers().GetAsString(RECEIPT);
        if (receiptId == null)
        {
            _ = ctx.CloseAsync();
            return;
        }

        IStompFrame receiptFrame = new DefaultStompFrame(StompCommand.RECEIPT);
        receiptFrame.Headers().Set(RECEIPT_ID, new StringCharSequence(receiptId));
        CloseOnComplete(ctx, ctx.WriteAndFlushAsync(receiptFrame));
    }

    private static void SendErrorFrame(string message, string description, IChannelHandlerContext ctx)
    {
        IStompFrame errorFrame = new DefaultStompFrame(StompCommand.ERROR);
        errorFrame.Headers().Set(MESSAGE, new StringCharSequence(message));

        if (description != null)
        {
            errorFrame.Content.WriteCharSequence(description, Encoding.UTF8);
        }

        CloseOnComplete(ctx, ctx.WriteAndFlushAsync(errorFrame));
    }

    private static IStompFrame TransformToMessage(IStompFrame sendFrame, StompSubscription subscription)
    {
        IStompFrame messageFrame = new DefaultStompFrame(StompCommand.MESSAGE, sendFrame.Content.RetainedDuplicate());
        string id = Guid.NewGuid().ToString();
        messageFrame.Headers()
                    .Set(MESSAGE_ID, new StringCharSequence(id))
                    .Set(SUBSCRIPTION, new StringCharSequence(subscription.Id()))
                    .Set(CONTENT_LENGTH, new StringCharSequence(messageFrame.Content.ReadableBytes.ToString(CultureInfo.InvariantCulture)));

        ICharSequence contentType = sendFrame.Headers().Get(CONTENT_TYPE);
        if (contentType != null)
        {
            messageFrame.Headers().Set(CONTENT_TYPE, contentType);
        }

        return messageFrame;
    }

    // CLR: chatDestinations.get(key), which returns null when the map holds no value for the key.
    private ISet<StompSubscription> Get(string destination)
    {
        return chatDestinations.TryGetValue(destination, out ISet<StompSubscription> subscriptions) ? subscriptions : null;
    }

    // CLR: addListener(ChannelFutureListener.CLOSE) on a ctx.writeAndFlush future, which closes future.channel(),
    // the context's channel.
    private static void CloseOnComplete(IChannelHandlerContext ctx, Task future)
    {
        IChannel channel = ctx.Channel;
        _ = future.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
    }
}
