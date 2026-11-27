/*
 * Copyright 2021 The Netty Project
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
using System.Runtime.ExceptionServices;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: the volatile fields are volatile; assertState() rethrows the recorded cause with its stack trace
// (ExceptionDispatchInfo) and assertNotNull is Assert.NotNull. The addresses are the QuicConnectionAddress values the
// msquic QuicChannel reports (docs/codec-classes-quic-porting.md: System.Net.Quic hides the connection IDs).
internal class QuicChannelValidationHandler : ChannelInboundHandlerAdapter
{
    private volatile bool wasActive;

    private volatile QuicConnectionAddress localAddress;
    private volatile QuicConnectionAddress remoteAddress;
    private volatile Exception cause;

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        this.cause = cause;
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        localAddress = (QuicConnectionAddress) ctx.Channel.LocalAddress;
        remoteAddress = (QuicConnectionAddress) ctx.Channel.RemoteAddress;
        wasActive = true;
        ctx.FireChannelActive();
    }

    internal QuicConnectionAddress LocalAddress()
    {
        return localAddress;
    }

    internal QuicConnectionAddress RemoteAddress()
    {
        return remoteAddress;
    }

    internal void AssertState()
    {
        if (cause != null)
        {
            ExceptionDispatchInfo.Capture(cause).Throw();
        }
        if (wasActive)
        {
            // Validate that the addresses could be retrieved
            Assert.NotNull(localAddress);
            Assert.NotNull(remoteAddress);
        }
    }
}
