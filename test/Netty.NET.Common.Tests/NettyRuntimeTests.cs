/*
 * Copyright 2017 The Netty Project
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
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Xunit;

namespace Netty.NET.Common.Tests;

[Collection("System properties")]
public class NettyRuntimeTests
{
    [Fact]
    public void TestIllegalSet()
    {
        var holder = new AvailableProcessorsHolder();
        foreach (int i in new[] { -1, 0 })
        {
            ArgumentException e = Assert.Throws<ArgumentException>(() => holder.SetAvailableProcessors(i));
            Assert.Contains("(expected: > 0)", e.Message);
        }
    }

    [Fact]
    public void TestMultipleSets()
    {
        var holder = new AvailableProcessorsHolder();
        holder.SetAvailableProcessors(1);
        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => holder.SetAvailableProcessors(2));
        Assert.Contains("availableProcessors is already set to [1], rejecting [2]", e.Message);
    }

    [Fact]
    public void TestSetAfterGet()
    {
        var holder = new AvailableProcessorsHolder();
        holder.AvailableProcessors();
        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => holder.SetAvailableProcessors(1));
        Assert.Contains("availableProcessors is already set", e.Message);
    }

    [Fact]
    public void TestRacingGetAndGet()
    {
        var holder = new AvailableProcessorsHolder();
        using var barrier = new Barrier(3);
        var firstReference = new AtomicReference<Exception>();
        Thread firstGet = new Thread(GetRunnable(holder, barrier, firstReference)) { IsBackground = true };
        firstGet.Start();
        var secondReference = new AtomicReference<Exception>();
        Thread secondGet = new Thread(GetRunnable(holder, barrier, secondReference)) { IsBackground = true };
        secondGet.Start();
        // release the hounds
        AwaitBarrier(barrier);
        // wait for the hounds
        AwaitBarrier(barrier);
        Assert.True(firstGet.Join(TimeSpan.FromSeconds(5)));
        Assert.True(secondGet.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(firstReference.Get());
        Assert.Null(secondReference.Get());
    }

    private static ThreadStart GetRunnable(AvailableProcessorsHolder holder, Barrier barrier,
        AtomicReference<Exception> reference) => () =>
    {
        try
        {
            AwaitBarrier(barrier);
            try { holder.AvailableProcessors(); }
            catch (InvalidOperationException e) { reference.Set(e); }
            AwaitBarrier(barrier);
        }
        catch (Exception e) { reference.Set(e); }
    };

    [Fact]
    public void TestRacingGetAndSet()
    {
        var holder = new AvailableProcessorsHolder();
        using var barrier = new Barrier(3);
        Exception getFailure = null, setFailure = null;
        Thread get = new Thread(() =>
        {
            try
            {
                AwaitBarrier(barrier);
                holder.AvailableProcessors();
                AwaitBarrier(barrier);
            }
            catch (Exception e) { getFailure = e; }
        }) { IsBackground = true };
        get.Start();
        var setException = new AtomicReference<InvalidOperationException>();
        Thread set = new Thread(() =>
        {
            try
            {
                AwaitBarrier(barrier);
                try { holder.SetAvailableProcessors(2048); }
                catch (InvalidOperationException e) { setException.Set(e); }
                AwaitBarrier(barrier);
            }
            catch (Exception e) { setFailure = e; }
        }) { IsBackground = true };
        set.Start();
        // release the hounds
        AwaitBarrier(barrier);
        // wait for the hounds
        AwaitBarrier(barrier);
        Assert.True(get.Join(TimeSpan.FromSeconds(5)));
        Assert.True(set.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(getFailure);
        Assert.Null(setFailure);
        if (setException.Get() == null) Assert.Equal(2048, holder.AvailableProcessors());
        else Assert.NotNull(setException.Get());
    }

    [Fact]
    public void TestGetWithSystemProperty()
    {
        string previous = SystemPropertyUtil.Get("io.netty.availableProcessors");
        try
        {
            Environment.SetEnvironmentVariable("io.netty.availableProcessors", "2048");
            var holder = new AvailableProcessorsHolder();
            Assert.Equal(2048, holder.AvailableProcessors());
        }
        finally { Environment.SetEnvironmentVariable("io.netty.availableProcessors", previous); }
    }

    [Fact]
    [SuppressForbidden("testing fallback to Runtime#availableProcessors")]
    public void TestGet()
    {
        string previous = SystemPropertyUtil.Get("io.netty.availableProcessors");
        try
        {
            Environment.SetEnvironmentVariable("io.netty.availableProcessors", null);
            var holder = new AvailableProcessorsHolder();
            Assert.Equal(Environment.ProcessorCount, holder.AvailableProcessors());
        }
        finally { Environment.SetEnvironmentVariable("io.netty.availableProcessors", previous); }
    }

    private static void AwaitBarrier(Barrier barrier) => Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(5)));
}
