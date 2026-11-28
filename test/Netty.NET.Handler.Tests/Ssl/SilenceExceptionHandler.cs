/*
 * Copyright 2026 The Netty Project
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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Tests.Ssl;

public class SilenceExceptionHandler : ChannelInboundHandlerAdapter
{
    // CLR: CopyOnWriteArrayList<Predicate<Throwable>> is a ConcurrentQueue<Predicate<Exception>>: rules are appended by the
    // test thread and iterated on the event loop, and both types iterate a snapshot in insertion order ("The enumeration
    // represents a moment-in-time snapshot of the contents of the queue",
    // https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentqueue-1.getenumerator?view=net-10.0;
    // CopyOnWriteArrayList: "The iterator will not reflect additions, removals, or changes to the list since the iterator
    // was created", https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/CopyOnWriteArrayList.html).
    private readonly ConcurrentQueue<Predicate<Exception>> rules;

    public SilenceExceptionHandler()
    {
        this.rules = new ConcurrentQueue<Predicate<Exception>>();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        foreach (Predicate<Exception> rule in rules)
        {
            if (rule(cause))
            {
                return;
            }
        }

        ctx.FireExceptionCaught(cause);
    }

    // CLR: Class<? extends Throwable> is a Type; Class.isInstance is Type.IsInstanceOfType and getCause() is InnerException.
    public SilenceExceptionHandler RootCauseInstanceOf(Type type)
    {
        rules.Enqueue(thr =>
        {
            Exception root = thr;

            while (root.InnerException != null)
            {
                root = root.InnerException;
            }

            return type.IsInstanceOfType(root);
        });
        return this;
    }

    public SilenceExceptionHandler MessageContains(string message)
    {
        // CLR: String.contains compares chars; string.Contains(string) "performs an ordinal (case-sensitive and
        // culture-insensitive) comparison" (https://learn.microsoft.com/en-us/dotnet/api/system.string.contains?view=net-10.0).
        rules.Enqueue(thr =>
        {
            return thr.Message.Contains(message);
        });
        return this;
    }
}
