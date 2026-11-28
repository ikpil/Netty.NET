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
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Stomp.WebSocket;

// CLR: equals(Object) and hashCode() are Equals(object) and GetHashCode(); getClass() is GetType(). The hash keeps the
// source's 31 * result + h steps in an unchecked context, which wraps on overflow as Java int arithmetic does (the
// string hashes differ from Java's, as String.GetHashCode is not String.hashCode()).
public sealed class StompSubscription
{

    private readonly string id;
    private readonly string destination;
    private readonly IChannel channel;

    public StompSubscription(string id, string destination, IChannel channel)
    {
        this.id = ObjectUtil.CheckNotNull(id, "id");
        this.destination = ObjectUtil.CheckNotNull(destination, "destination");
        this.channel = ObjectUtil.CheckNotNull(channel, "channel");
    }

    public string Id()
    {
        return id;
    }

    public string Destination()
    {
        return destination;
    }

    public IChannel Channel()
    {
        return channel;
    }

    public override bool Equals(object obj)
    {
        if (this == obj)
        {
            return true;
        }

        if (obj == null || GetType() != obj.GetType())
        {
            return false;
        }

        StompSubscription that = (StompSubscription) obj;

        if (!id.Equals(that.id))
        {
            return false;
        }

        if (!destination.Equals(that.destination))
        {
            return false;
        }

        return channel.Equals(that.channel);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int result = id.GetHashCode();
            result = 31 * result + destination.GetHashCode();
            result = 31 * result + channel.GetHashCode();
            return result;
        }
    }
}
