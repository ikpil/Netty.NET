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
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

public class IoEventLoopGroupSourceTest
{
    private const int Threads = 4;

    [Fact]
    public Task TestIsIoTypeDoesNotAdvanceTheChooser() =>
        AssertChooserNotAdvancedBy(group =>
        {
            Assert.True(group.IsIoType(typeof(LocalCompletionHandler)));
            Assert.False(group.IsIoType(typeof(CompletionIoHandler)));
        });

    [Fact]
    public Task TestIsCompatibleDoesNotAdvanceTheChooser() =>
        AssertChooserNotAdvancedBy(group =>
        {
            Assert.True(group.IsCompatible(typeof(LocalHandle)));
            Assert.False(group.IsCompatible(typeof(SocketHandle)));
        });

    /**
     * Asserts that the given action does not change which {@link IoEventLoop} is returned by the next
     * {@link IoEventLoopGroup#next()} call.
     */
    private static async Task AssertChooserNotAdvancedBy(Action<IIoEventLoopGroup> action)
    {
        var group = new DefaultEventLoopGroup(() => new LocalCompletionHandler(), Threads);
        try
        {
            // Record the round-robin order of the group first, so we know what next() must return afterwards.
            var roundRobin = Enumerable.Range(0, Threads).Select(_ => group.Next()).ToArray();
            Assert.Equal(Threads, roundRobin.Distinct().Count());
            for (int i = 0; i < Threads; i++)
            {
                action(group);
                Assert.Same(roundRobin[i], group.Next());
            }
        }
        finally { await group.StopAsync(); }
    }

    private sealed class LocalCompletionHandler : CompletionIoHandler
    {
        public override bool IsCompatible(Type handleType) => handleType == typeof(LocalHandle);
    }

    private class LocalHandle : IIoHandle
    {
        public void HandleCompletion(IoRegistration registration, object completion) { }
        public void Dispose() { }
    }

    private sealed class SocketHandle : LocalHandle { }
}
