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

using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.IpFilter;

/**
 * This class allows one to ensure that at all times for every IP address there is at most one
 * {@link Channel} connected to the server.
 */
// CLR: ConcurrentHashMap.newKeySet(), "a new Set backed by a ConcurrentHashMap from the given type to Boolean.TRUE"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/ConcurrentHashMap.html#newKeySet()),
// is a ConcurrentDictionary<IPAddress, bool> used as a set: Set.add is TryAdd, which returns "false if the key already
// exists" (https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2.tryadd?view=net-10.0),
// and Set.remove is TryRemove. IPAddress equality "returns true if the two instances contain the same IP address"
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.equals?view=net-10.0), as InetAddress.equals compares
// the address bytes. The closeFuture() listener is a continuation of the CloseFuture Task. Java's DefaultPromise
// notifies its listeners at once when it completes on its event loop (notifyListeners(), pinned
// common/src/main/java/io/netty/util/concurrent/DefaultPromise.java), so the address is removed during the close;
// Transport's CloseFuture is a TaskCompletionSource created with RunContinuationsAsynchronously (AbstractChannel), which
// "Forces continuations added to the current task to be executed asynchronously"
// (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcreationoptions?view=net-10.0), so the
// address is removed on a thread-pool thread shortly after the close completes.
[Sharable]
public class UniqueIpFilter : AbstractRemoteAddressFilter<IPEndPoint>
{
    private readonly ConcurrentDictionary<IPAddress, bool> connected = new ConcurrentDictionary<IPAddress, bool>();

    protected override bool Accept(IChannelHandlerContext ctx, IPEndPoint remoteAddress)
    {
        IPAddress remoteIp = remoteAddress.Address;
        if (!connected.TryAdd(remoteIp, true))
        {
            return false;
        }
        else
        {
            _ = ctx.Channel.CloseFuture.ContinueWith(future => connected.TryRemove(remoteIp, out _),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return true;
        }
    }
}
