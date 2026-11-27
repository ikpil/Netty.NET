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
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Http2.HelloWorld.Client;

/**
 * Process {@link io.netty.handler.codec.http.FullHttpResponse} translated from HTTP/2 frames
 */
// CLR: ChannelFuture/ChannelPromise are Task/TaskCompletionSource (docs/transport-porting.md); Map.Entry and its
// AbstractMap.SimpleEntry are KeyValuePair (getKey()/getValue() are Key/Value), and ConcurrentHashMap is
// ConcurrentDictionary, whose enumeration tolerates the removal of the current entry as the map's iterator does.
// put(...) returns the previous entry or null: a nullable KeyValuePair set by the update delegate of AddOrUpdate.
// awaitResponses(long, TimeUnit) blocks with awaitUninterruptibly(timeout, unit); it is AwaitResponsesAsync(TimeSpan),
// which awaits Task.WhenAny of each Task and a delay of the timeout (as Http2SettingsHandler.AwaitSettingsAsync), and
// isSuccess()/cause() are IsCompletedSuccessfully and the InnerException of the faulted Task (a TaskCanceledException for
// a canceled one). msg.headers().getInt(...) returns int? (Integer, null when absent). new String(arr, 0, length, UTF_8)
// is Encoding.UTF8.GetString; IllegalStateException is InvalidOperationException and new RuntimeException(cause) an
// Exception whose message is cause.toString() (Common's ThrowableUtil.ThrowableToString). The protected channelRead0
// stays protected; FullHttpResponse is IFullHttpResponse and content() the Content property.
public class HttpResponseHandler : SimpleChannelInboundHandler<IFullHttpResponse>
{

    private readonly ConcurrentDictionary<int, KeyValuePair<Task, TaskCompletionSource>> streamidPromiseMap;

    public HttpResponseHandler()
    {
        // Use a concurrent map because we add and iterate from the main thread (just for the purposes of the example),
        // but Netty also does a get on the map when messages are received in a EventLoop thread.
        streamidPromiseMap = new ConcurrentDictionary<int, KeyValuePair<Task, TaskCompletionSource>>();
    }

    /**
     * Create an association between an anticipated response stream id and a {@link io.netty.channel.ChannelPromise}
     *
     * @param streamId The stream for which a response is expected
     * @param writeFuture A future that represent the request write operation
     * @param promise The promise object that will be used to wait/notify events
     * @return The previous object associated with {@code streamId}
     * @see HttpResponseHandler#awaitResponses(long, java.util.concurrent.TimeUnit)
     */
    public KeyValuePair<Task, TaskCompletionSource>? Put(int streamId, Task writeFuture, TaskCompletionSource promise)
    {
        KeyValuePair<Task, TaskCompletionSource> entry = new KeyValuePair<Task, TaskCompletionSource>(writeFuture, promise);
        KeyValuePair<Task, TaskCompletionSource>? previous = null;
        streamidPromiseMap.AddOrUpdate(streamId, entry, (_, old) =>
        {
            previous = old;
            return entry;
        });
        return previous;
    }

    /**
     * Wait (sequentially) for a time duration for each anticipated response
     *
     * @param timeout Value of time to wait for each response
     * @param unit Units associated with {@code timeout}
     * @see HttpResponseHandler#put(int, io.netty.channel.ChannelFuture, io.netty.channel.ChannelPromise)
     */
    public async Task AwaitResponsesAsync(TimeSpan timeout)
    {
        foreach (KeyValuePair<int, KeyValuePair<Task, TaskCompletionSource>> entry in streamidPromiseMap)
        {
            Task writeFuture = entry.Value.Key;
            if (await Task.WhenAny(writeFuture, Task.Delay(timeout)) != writeFuture)
            {
                throw new InvalidOperationException("Timed out waiting to write for stream id " + entry.Key);
            }
            if (!writeFuture.IsCompletedSuccessfully)
            {
                throw NewRuntimeException(writeFuture);
            }
            TaskCompletionSource promise = entry.Value.Value;
            if (await Task.WhenAny(promise.Task, Task.Delay(timeout)) != promise.Task)
            {
                throw new InvalidOperationException("Timed out waiting for response on stream id " + entry.Key);
            }
            if (!promise.Task.IsCompletedSuccessfully)
            {
                throw NewRuntimeException(promise.Task);
            }
            Console.WriteLine("---Stream id: " + entry.Key + " received---");
            streamidPromiseMap.TryRemove(entry.Key, out _);
        }
    }

    // CLR-only: new RuntimeException(future.cause()) of a failed or canceled Task.
    private static Exception NewRuntimeException(Task future)
    {
        Exception cause = future.IsCanceled ? new TaskCanceledException(future) : future.Exception.InnerException;
        return new Exception(ThrowableUtil.ThrowableToString(cause), cause);
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpResponse msg)
    {
        int? streamId = msg.Headers().GetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text());
        if (streamId == null)
        {
            Console.Error.WriteLine("HttpResponseHandler unexpected message received: " + msg);
            return;
        }

        if (!streamidPromiseMap.TryGetValue(streamId.Value, out KeyValuePair<Task, TaskCompletionSource> entry))
        {
            Console.Error.WriteLine("Message received for unknown stream id " + streamId);
        }
        else
        {
            // Do stuff with the message (for now just print it)
            ByteBuf content = msg.Content;
            if (content.IsReadable())
            {
                int contentLength = content.ReadableBytes;
                byte[] arr = new byte[contentLength];
                content.ReadBytes(arr);
                Console.WriteLine(Encoding.UTF8.GetString(arr, 0, contentLength));
            }

            entry.Value.SetResult();
        }
    }
}
