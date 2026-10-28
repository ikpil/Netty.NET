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

using System.Collections.Concurrent;
using System.Net;

namespace Netty.NET.Transport.Channels.Local;

internal static class LocalChannelRegistry
{
    private static readonly ConcurrentDictionary<LocalAddress, IChannel> BoundChannels = new();
    internal static LocalAddress Register(IChannel channel, LocalAddress oldAddress, EndPoint address)
    {
        if (oldAddress != null)
            throw new ChannelException("Already bound.");
        if (address is not LocalAddress local)
            throw new ChannelException("Unsupported address type.");
        if (local.Equals(LocalAddress.ANY))
            local = new LocalAddress(channel);
        if (!BoundChannels.TryAdd(local, channel))
            throw new ChannelException("Address is already in use.");
        return local;
    }
    internal static IChannel Get(EndPoint address) => address is LocalAddress local && BoundChannels.TryGetValue(local, out var channel) ? channel : null;
    internal static void Unregister(LocalAddress address) { if (address != null) BoundChannels.TryRemove(address, out _); }
}
