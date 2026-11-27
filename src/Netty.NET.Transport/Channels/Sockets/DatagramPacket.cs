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
using System.Net;
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Channels.Sockets;

/**
 * The message container that is used for {@link DatagramChannel} to communicate with the remote peer.
 */
// CLR: Java overrides retain/touch only for the covariant return type; the C# members hide the envelope's members
// with the DatagramPacket return type and the IByteBufHolder members are implemented explicitly.
public class DatagramPacket : DefaultAddressedEnvelope<ByteBuf, IPEndPoint>, IByteBufHolder
{
    /**
     * Create a new instance with the specified packet {@code data} and {@code recipient} address.
     */
    public DatagramPacket(ByteBuf data, IPEndPoint recipient) : base(data, recipient)
    {
    }

    /**
     * Create a new instance with the specified packet {@code data}, {@code recipient} address, and {@code sender}
     * address.
     */
    public DatagramPacket(ByteBuf data, IPEndPoint recipient, IPEndPoint sender) : base(data, recipient, sender)
    {
    }

    public virtual DatagramPacket Copy()
    {
        return Replace(Content.Copy());
    }

    public virtual DatagramPacket Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public virtual DatagramPacket RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public virtual DatagramPacket Replace(ByteBuf content)
    {
        return new DatagramPacket(content, Recipient, Sender);
    }

    public new DatagramPacket Retain()
    {
        base.Retain();
        return this;
    }

    public new DatagramPacket Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public new DatagramPacket Touch()
    {
        base.Touch();
        return this;
    }

    public new DatagramPacket Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    IByteBufHolder IByteBufHolder.Copy()
    {
        return Copy();
    }
    IByteBufHolder IByteBufHolder.Duplicate()
    {
        return Duplicate();
    }
    IByteBufHolder IByteBufHolder.RetainedDuplicate()
    {
        return RetainedDuplicate();
    }
    IByteBufHolder IByteBufHolder.Replace(ByteBuf content)
    {
        return Replace(content);
    }
    IByteBufHolder IByteBufHolder.Retain()
    {
        return Retain();
    }
    IByteBufHolder IByteBufHolder.Retain(int increment)
    {
        return Retain(increment);
    }
    IByteBufHolder IByteBufHolder.Touch()
    {
        return Touch();
    }
    IByteBufHolder IByteBufHolder.Touch(object hint)
    {
        return Touch(hint);
    }
}
