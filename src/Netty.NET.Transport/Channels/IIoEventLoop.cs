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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels;

/**
 * {@link EventLoop} that allows to register / deregister {@link IoHandle} instances.
 */
public interface IIoEventLoop : IEventLoop, IIoEventLoopGroup
{
    IIoEventLoop IIoEventLoopGroup.Next()
    {
        return this;
    }

    IEventLoop IEventLoopGroup.Next()
    {
        return this;
    }

    // CLR: IEventLoop and IEventLoopGroup both declare register(Channel); redeclaring it removes the ambiguity for
    // callers that hold an IIoEventLoop.
    new Task RegisterAsync(IChannel channel, CancellationToken cancellationToken = default);

    /**
     * Register the {@link IoHandle} to the {@link EventLoop} for I/O processing.
     *
     * @param handle        the {@link IoHandle} to register.
     * @return              the {@link Future} that is notified once the operations completes.
     */
    // CLR: Task<IIoRegistration> replaces Future<IoRegistration>.
    new Task<IIoRegistration> RegisterAsync(IIoHandle handle);

    // Force sub-classes to implement.
    new bool IsCompatible(Type handleType);

    // Force sub-classes to implement.
    new bool IsIoType(Type handlerType);
}
