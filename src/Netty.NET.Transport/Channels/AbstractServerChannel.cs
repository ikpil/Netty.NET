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
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels;

// CLR: AbstractChannel's transport members replace AbstractUnsafe, so DefaultServerUnsafe.connect becomes the
// DoConnectAsync override; the returned Task fails as the Java promise does. The exception texts are the ones the
// local server channel used before this class was ported.
/**
 * A skeletal server-side {@link Channel} implementation.  A server-side
 * {@link Channel} does not allow the following operations:
 * <ul>
 * <li>{@link #connect(SocketAddress, ChannelPromise)}</li>
 * <li>{@link #disconnect(ChannelPromise)}</li>
 * <li>{@link #write(Object, ChannelPromise)}</li>
 * <li>{@link #flush()}</li>
 * <li>and the shortcut methods which calls the methods mentioned above
 * </ul>
 */
public abstract class AbstractServerChannel : AbstractChannel, IServerChannel
{
    private static readonly ChannelMetadata METADATA = new ChannelMetadata(false, 16);

    /**
     * Creates a new instance.
     */
    protected AbstractServerChannel() : base(null, METADATA)
    {
    }

    protected override EndPoint RemoteAddress0()
    {
        return null;
    }

    protected override Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    protected override Task DoWrite(object msg)
    {
        return Task.FromException(new NotSupportedException("Server channels cannot write."));
    }

    protected sealed override object FilterOutboundMessage(object msg)
    {
        throw new NotSupportedException("Server channels cannot write.");
    }

    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        return Task.FromException(new NotSupportedException("Server channels cannot connect."));
    }
}
