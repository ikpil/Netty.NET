/*
 * Copyright 2024 The Netty Project
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

using System.Net.Sockets;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels.Nio;

/**
 * Allows to create an {@link IoHandle} for a {@link SelectableChannel}, not necessarily created by Netty. This
 * {@link IoHandle} can be used together with {@link NioIoHandler} and so have events dispatched for
 * the {@link SelectableChannel}.
 */
// CLR: S is a System.Net.Sockets.Socket, which the select()-based NioIoHandler accepts like a JDK SelectableChannel.
// java.nio.channels.SelectionKey is jvm-only-replaced: the subclass receives the ready NioIoOps of the event, the
// key state that a selected key reports through readyOps().
// https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.select
public abstract class NioSelectableChannelIoHandle<S> : IIoHandle, INioIoHandle where S : Socket
{
    private readonly S _channel;

    public NioSelectableChannelIoHandle(S channel)
    {
        _channel = ObjectUtil.CheckNotNull(channel, "channel");
    }

    public void Handle(IIoRegistration registration, IIoEvent ioEvent)
    {
        NioIoOps key = ((INioIoEvent)ioEvent).Ops();
        Handle(_channel, key);
    }

    public void Close()
    {
        _channel.Close();
    }

    public Socket SelectableChannel()
    {
        return _channel;
    }

    protected abstract void Handle(S channel, NioIoOps key);

    protected virtual void Deregister(S channel)
    {
        // NOOP.
    }
}
