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

using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels;

/**
 * Skeletal implementation of {@link EventLoop}.
 */
public abstract class AbstractEventLoop : AbstractEventExecutor, IEventLoop
{
    protected AbstractEventLoop()
    {
    }

    protected AbstractEventLoop(IEventLoopGroup parent) : base(parent)
    {
    }

    public override IEventLoopGroup Parent()
    {
        return (IEventLoopGroup)base.Parent();
    }

    public override IEventLoop Next()
    {
        return (IEventLoop)base.Next();
    }

    // CLR: the register(Channel) member that Java leaves to subclasses is declared abstract here because a CLR
    // abstract class must name every unimplemented interface member.
    public abstract Task RegisterAsync(IChannel channel, CancellationToken cancellationToken = default);
}
