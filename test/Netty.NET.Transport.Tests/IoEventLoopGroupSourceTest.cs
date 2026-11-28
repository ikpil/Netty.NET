/*
 * Copyright 2026 The Netty Project
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
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Tests;

public class IoEventLoopGroupSourceTest
{
    private const int THREADS = 4;

    [Fact]
    public void TestIsIoTypeDoesNotAdvanceTheChooser()
    {
        AssertChooserNotAdvancedBy(group =>
        {
            Assert.True(group.IsIoType(typeof(LocalIoHandler)));
            Assert.False(group.IsIoType(typeof(NioIoHandler)));
        });
    }

    [Fact]
    public void TestIsCompatibleDoesNotAdvanceTheChooser()
    {
        AssertChooserNotAdvancedBy(group =>
        {
            Assert.True(group.IsCompatible(typeof(ILocalIoHandle)));
            Assert.False(group.IsCompatible(typeof(INioIoHandle)));
        });
    }

    /**
     * Asserts that the given action does not change which {@link IoEventLoop} is returned by the next
     * {@link IoEventLoopGroup#next()} call.
     */
    private static void AssertChooserNotAdvancedBy(Action<IIoEventLoopGroup> action)
    {
        IIoEventLoopGroup group = new MultiThreadIoEventLoopGroup(THREADS, LocalIoHandler.NewFactory());

        try
        {
            // Record the round-robin order of the group first, so we know what next() must return afterwards.
            IIoEventLoop[] roundRobin = new IIoEventLoop[THREADS];

            for (int i = 0; i < THREADS; i++)
            {
                roundRobin[i] = group.Next();
            }

            for (int i = 0; i < THREADS; i++)
            {
                action(group);
                Assert.True(ReferenceEquals(roundRobin[i], group.Next()),
                        "querying the IO type of the group must not advance the chooser used by next()");
            }
        }
        finally
        {
            group.ShutdownGracefullyAsync();
        }
    }
}
