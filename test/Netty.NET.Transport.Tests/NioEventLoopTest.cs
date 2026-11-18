/*
 * Copyright 2019 The Netty Project
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
using System.Diagnostics.CodeAnalysis;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// CLR: io.netty.testsuite.transport.NioEventLoopTest. The transport module's io.netty.channel.nio.NioEventLoopTest is
// ported as NioEventLoopSourceTest, so the two same-named sources stay distinct in this one test namespace.
// The source runs on the @Deprecated NioEventLoopGroup/NioEventLoop, which are deprecated-not-ported; each is replaced
// by what its @deprecated tag names: MultiThreadIoEventLoopGroup with NioIoHandler.newFactory(), whose children are
// SingleThreadIoEventLoop.
public class NioEventLoopTest : AbstractSingleThreadEventLoopTest
{
    // CLR: the source returns true because NioEventLoop overrides registeredChannelsIterator() with a selection-key
    // iterator. The replacement SingleThreadIoEventLoop does not override it, so in the pinned source it throws
    // UnsupportedOperationException from SingleThreadEventLoop, and the CLR port does the same; the iteration tests
    // are therefore skipped here. registeredChannels() is likewise -1, so testChannelsRegistered registers and
    // deregisters without counting; NioEventLoopSourceTest counts through NioIoHandler.numRegistered().
    protected override bool SupportsChannelIteration()
    {
        return false;
    }

    protected override IEventLoopGroup NewEventLoopGroup()
    {
        return new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
    }

    protected override IEventLoopGroup NewAutoScalingEventLoopGroup()
    {
        // CLR: SelectorProvider.provider() is JVM-only (jvm-only-replaced); NioIoHandler.NewFactory takes the
        // SelectStrategyFactory alone.
        return new MultiThreadIoEventLoopGroup(SCALING_MAX_THREADS, (Action<Action>) null, AUTO_SCALING_CHOOSER_FACTORY,
                                     NioIoHandler.NewFactory(DefaultSelectStrategyFactory.INSTANCE));
    }

    protected override IChannel NewChannel()
    {
        return new NioSocketChannel();
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    protected override Type ServerChannelClass()
    {
        return typeof(NioServerSocketChannel);
    }
}
