/*
 * Copyright 2012 The Netty Project
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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels.Groups;

// CLR: the aggregate Task and the immutable channel-to-Task map jointly represent the source future.
// Use await/WaitAsync for completion. Task continuations do not inherit event-loop affinity, and native
// Task.Wait has no JVM deadlock guard; the channel owner must never synchronously wait for its own I/O.
/**
 * The result of an asynchronous {@link ChannelGroup} operation.
 * {@link ChannelGroupFuture} is composed of {@link ChannelFuture}s which
 * represent the outcome of the individual I/O operations that affect the
 * {@link Channel}s in the {@link ChannelGroup}.
 *
 * <p>
 * All I/O operations in {@link ChannelGroup} are asynchronous.  It means any
 * I/O calls will return immediately with no guarantee that the requested I/O
 * operations have been completed at the end of the call.  Instead, you will be
 * returned with a {@link ChannelGroupFuture} instance which tells you when the
 * requested I/O operations have succeeded, failed, or cancelled.
 * <p>
 * Various methods are provided to let you check if the I/O operations has been
 * completed, wait for the completion, and retrieve the result of the I/O
 * operation. It also allows you to add more than one
 * {@link ChannelGroupFutureListener} so you can get notified when the I/O
 * operation have been completed.
 *
 * <h3>Prefer {@link #addListener(GenericFutureListener)} to {@link #await()}</h3>
 *
 * It is recommended to prefer {@link #addListener(GenericFutureListener)} to
 * {@link #await()} wherever possible to get notified when I/O operations are
 * done and to do any follow-up tasks.
 * <p>
 * {@link #addListener(GenericFutureListener)} is non-blocking.  It simply
 * adds the specified {@link ChannelGroupFutureListener} to the
 * {@link ChannelGroupFuture}, and I/O thread will notify the listeners when
 * the I/O operations associated with the future is done.
 * {@link ChannelGroupFutureListener} yields the best performance and resource
 * utilization because it does not block at all, but it could be tricky to
 * implement a sequential logic if you are not used to event-driven programming.
 * <p>
 * By contrast, {@link #await()} is a blocking operation.  Once called, the
 * caller thread blocks until all I/O operations are done.  It is easier to
 * implement a sequential logic with {@link #await()}, but the caller thread
 * blocks unnecessarily until all I/O operations are done and there's relatively
 * expensive cost of inter-thread notification.  Moreover, there's a chance of
 * dead lock in a particular circumstance, which is described below.
 *
 * <h3>Do not call {@link #await()} inside {@link ChannelHandler}</h3>
 * <p>
 * The event handler methods in {@link ChannelHandler} is often called by
 * an I/O thread.  If {@link #await()} is called by an event handler
 * method, which is called by the I/O thread, the I/O operation it is waiting
 * for might never be complete because {@link #await()} can block the I/O
 * operation it is waiting for, which is a dead lock.
 * <pre>
 * // BAD - NEVER DO THIS
 * {@code @Override}
 * public void messageReceived({@link ChannelHandlerContext} ctx, ShutdownMessage msg) {
 *     {@link ChannelGroup} allChannels = MyServer.getAllChannels();
 *     {@link ChannelGroupFuture} future = allChannels.close();
 *     future.awaitUninterruptibly();
 *     // Perform post-shutdown operation
 *     // ...
 *
 * }
 *
 * // GOOD
 * {@code @Override}
 * public void messageReceived(ChannelHandlerContext ctx, ShutdownMessage msg) {
 *     {@link ChannelGroup} allChannels = MyServer.getAllChannels();
 *     {@link ChannelGroupFuture} future = allChannels.close();
 *     future.addListener(new {@link ChannelGroupFutureListener}() {
 *         public void operationComplete({@link ChannelGroupFuture} future) {
 *             // Perform post-closure operation
 *             // ...
 *         }
 *     });
 * }
 * </pre>
 * <p>
 * In spite of the disadvantages mentioned above, there are certainly the cases
 * where it is more convenient to call {@link #await()}. In such a case, please
 * make sure you do not call {@link #await()} in an I/O thread.  Otherwise,
 * {@link IllegalStateException} will be raised to prevent a dead lock.
 */
// CLR: the group is the operation receiver; no second future wrapper stores another group reference.
/**
 * Returns the {@link ChannelGroup} which is associated with this future.
 */
public partial class DefaultChannelGroup
{
    /**
     * Returns the {@link ChannelFuture} of the individual I/O operation which
     * is associated with the specified {@link Channel}.
     *
     * @return the matching {@link ChannelFuture} if found.
     *         {@code null} otherwise.
     */
    /**
     * Returns the {@link Iterator} that enumerates all {@link ChannelFuture}s
     * which are associated with this future.  Please note that the returned
     * {@link Iterator} is unmodifiable, which means a {@link ChannelFuture}
     * cannot be removed from this future.
     */
    // CLR: lookup uses the dictionary indexer/TryGetValue, and iteration uses its read-only Values.
    /**
     * Returns {@code true} if and only if the I/O operations associated with
     * this future were partially successful with some failure.
     */
    /**
     * Returns {@code true} if and only if the I/O operations associated with
     * this future have failed partially with some success.
     */
    // CLR: partial success is 0 < successful Tasks < Count. Partial failure is
    // 0 < completed non-successful Tasks < Count (including canceled child Tasks).
    /**
     * Creates a new instance.
     */
    // CLR exposes channel lookup, immutable iteration and live partial completion through standard
    // dictionary/Task APIs. Callers keep the returned Task for aggregate await and inspect these Tasks
    // for progress; neither the map nor its task producers can be modified through the public API.
    private static IReadOnlyDictionary<IChannel, Task> Snapshot(List<KeyValuePair<IChannel, Task>> tasks) =>
        new ReadOnlyDictionary<IChannel, Task>(new Dictionary<IChannel, Task>(tasks, ReferenceEqualityComparer.Instance));

    // CLR: aggregate success is Task.IsCompletedSuccessfully; the async method owns its completion producer.
    /**
     * The default {@link ChannelGroupFuture} implementation.
     */
    /**
     * Returns {@code true} if and only if all I/O operations associated with
     * this future were successful without any failure.
     */
    private static async Task CompleteAsync(List<KeyValuePair<IChannel, Task>> operations)
    {
        // Done on arrival?
        if (operations.Count == 0)
            return;
        try { await Task.WhenAll(operations.Select(pair => pair.Value)).ConfigureAwait(false); }
        catch
        {
            // WhenAll waits for every selected channel. Keep the complete set of channel/cause pairs,
            // including canceled child tasks, rather than exposing just the exception rethrown by await.
            var failures = new List<KeyValuePair<IChannel, Exception>>();
            foreach (var pair in operations)
            {
                if (pair.Value.IsCompletedSuccessfully) continue;
                try { pair.Value.GetAwaiter().GetResult(); }
                catch (Exception error)
                {
                    failures.Add(new(pair.Key, pair.Value.Exception is { InnerExceptions.Count: > 1 } multiple ? multiple : error));
                }
            }
            throw new ChannelGroupException(failures);
        }
    }
}
