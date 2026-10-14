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
using System.Threading;

namespace Netty.NET.Buffer.Tests;

public partial class DefaultByteBufAllocatorTest
{
    [Theory]
    [InlineData(null, false)] [InlineData("0", false)] [InlineData("-1", false)]
    [InlineData("invalid", false)] [InlineData("1", true)] [InlineData("256", true)] [InlineData("1024", true)]
    public void ThreadLocalDirectSettingsSnapshotWithoutInitializingGlobalAllocator(string size, bool enabled)
    {
        using var scope = new Scope("invalid", "true");
        scope.Set("io.netty.threadLocalDirectBufferSize", size);
        // If the transport utility initialized the global allocator, this poison
        // pooled geometry would reject its unknown-selector fallback.
        scope.Set("io.netty.allocator.directMemoryCacheAlignment", "3");
        object b = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
        Assert.Equal(enabled, b != null);
        if (b != null)
        {
            Assert.True(Property<bool>(b, "IsDirect")); Assert.Equal(256, Property<int>(b, "Capacity"));
            Assert.Equal(1, Property<int>(b, "ReferenceCount")); Call(b, "Release", 1);
            Assert.Equal(0, scope.NativeBytes); // Ordinary threads do not retain idle storage.
        }
        scope.Set("io.netty.threadLocalDirectBufferSize", enabled ? "0" : "1024");
        object again = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
        Assert.Equal(enabled, again != null); if (again != null) Call(again, "Release", 1);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ThreadLocalDirectGuaranteedFastScopeCachesAndDrainsOnSuccessOrFailure(bool fail)
    {
        using var scope = new Scope(null, null); scope.Set("io.netty.threadLocalDirectBufferSize", "1024");
        Action action = () =>
        {
            object first = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
            Call(first, "SetByte", 0, 72); Call(first, "Release", 1); Assert.Equal(256, scope.NativeBytes);
            object next = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
            Assert.NotSame(first, next); Assert.Equal((byte)72, Call(next, "GetByte", 0));
            Call(next, "Release", 1);
            if (fail) throw new InvalidOperationException("scope failure");
        };
        Exception error = Record.Exception(() => scope.CommonTypeCall("Concurrent.FastThreadLocalThread", "RunWithFastThreadLocal", action));
        Assert.Equal(fail, error != null); Assert.Equal(0, scope.NativeBytes);
        scope.CommonTypeCall("Concurrent.FastThreadLocalThread", "RunWithFastThreadLocal", (Action)(() =>
        {
            object b = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
            Assert.Equal((byte)0, Call(b, "GetByte", 0)); Call(b, "Release", 1);
        }));
        Assert.Equal(0, scope.NativeBytes);
    }

    [Fact]
    public void ThreadLocalDirectActualFastThreadTerminationDrainsIdleStorageButKeepsLiveResultValid()
    {
        using var scope = new Scope(null, null); scope.Set("io.netty.threadLocalDirectBufferSize", "1024");
        object live = null; Exception failure = null;
        Action work = () =>
        {
            try
            {
                object idle = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
                live = scope.Type("ByteBufUtil").GetMethod("ThreadLocalDirectBuffer").Invoke(null, null);
                Call(idle, "Release", 1); Call(live, "WriteByte", 71); Assert.Equal(512, scope.NativeBytes);
            }
            catch (Exception e) { failure = e; }
        };
        Type type = scope._common.GetType("Netty.NET.Common.Concurrent.FastThreadLocalThread", true);
        object fast = Activator.CreateInstance(type, work, null, 0, null);
        Thread thread = (Thread)type.GetProperty("Thread").GetValue(fast);
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10))); Assert.Null(failure);
        Assert.Equal(256, scope.NativeBytes); Assert.Equal((byte)71, Call(live, "ReadByte")); Call(live, "Release", 1);
        Assert.Equal(0, scope.NativeBytes);
    }
}
