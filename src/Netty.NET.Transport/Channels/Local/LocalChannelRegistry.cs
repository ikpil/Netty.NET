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
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels.Local;

internal sealed class LocalChannelRegistry
{
    private static readonly ConcurrentDictionary<LocalAddress, IChannel> boundChannels = new();

    internal static LocalAddress Register(
            IChannel channel, LocalAddress oldLocalAddress, EndPoint localAddress)
    {
        if (oldLocalAddress != null)
        {
            throw new ChannelException("already bound");
        }
        if (!(localAddress is LocalAddress))
        {
            throw new ChannelException("unsupported address type: " + StringUtil.SimpleClassName(localAddress));
        }

        LocalAddress addr = (LocalAddress)localAddress;
        if (LocalAddress.ANY.Equals(addr))
        {
            addr = new LocalAddress(channel);
        }

        // CLR: GetOrAdd with an observed factory is the atomic putIfAbsent; a channel bound already is "in use" too.
        bool added = false;
        IChannel boundChannel = boundChannels.GetOrAdd(addr, _ =>
        {
            added = true;
            return channel;
        });
        if (!added || boundChannel != channel)
        {
            throw new ChannelException("address already in use by: " + boundChannel);
        }

        return addr;
    }

    internal static IChannel Get(EndPoint localAddress)
    {
        // CLR: ConcurrentHashMap.get(Object) returns null for a key of another type.
        return localAddress is LocalAddress key && boundChannels.TryGetValue(key, out IChannel channel) ? channel : null;
    }

    internal static void Unregister(LocalAddress localAddress)
    {
        boundChannels.TryRemove(localAddress, out _);
    }

    private LocalChannelRegistry()
    {
        // Unused
    }
}
