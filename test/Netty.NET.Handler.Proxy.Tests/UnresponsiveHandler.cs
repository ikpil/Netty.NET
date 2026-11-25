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

using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Proxy.Tests;

// CLR: @Sharable is the [Sharable] attribute of the Transport.
[Sharable]
internal sealed class UnresponsiveHandler : SimpleChannelInboundHandler<object>
{
    internal static readonly UnresponsiveHandler INSTANCE = new UnresponsiveHandler();

    private UnresponsiveHandler() { }

    protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
    {
        // Ignore
    }
}
