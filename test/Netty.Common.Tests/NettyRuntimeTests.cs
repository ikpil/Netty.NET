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

using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.Common.Tests;

public class NettyRuntimeTests {

    [Fact]
    public void testIllegalSet() {
        final NettyRuntime.AvailableProcessorsHolder holder = new NettyRuntime.AvailableProcessorsHolder();
        for (final int i : new int[] { -1, 0 }) {
            try {
                holder.setAvailableProcessors(i);
                Assert.Fail();
            } catch (final ArgumentException e) {
                assertThat(e.getMessage()).contains("(expected: > 0)");
            }
        }
    }

    [Fact]
    public void testMultipleSets() {
        final NettyRuntime.AvailableProcessorsHolder holder = new NettyRuntime.AvailableProcessorsHolder();
        holder.setAvailableProcessors(1);
        try {
            holder.setAvailableProcessors(2);
            Assert.Fail();
        } catch (final IllegalStateException e) {
            assertThat(e.getMessage()).contains("availableProcessors is already set to [1], rejecting [2]");
        }
    }

    [Fact]
    public void testSetAfterGet() {
        final NettyRuntime.AvailableProcessorsHolder holder = new NettyRuntime.AvailableProcessorsHolder();
        holder.availableProcessors();
        try {
            holder.setAvailableProcessors(1);
            Assert.Fail();
        } catch (final IllegalStateException e) {
            assertThat(e.getMessage()).contains("availableProcessors is already set");
        }
    }

    [Fact]
    public void testRacingGetAndGet() {
        final NettyRuntime.AvailableProcessorsHolder holder = new NettyRuntime.AvailableProcessorsHolder();
        final CyclicBarrier barrier = new CyclicBarrier(3);

        final AtomicReference<IllegalStateException> firstReference = new AtomicReference<IllegalStateException>();
        final IRunnable firstTarget = getRunnable(holder, barrier, firstReference);
        final Thread firstGet = new Thread(firstTarget);
        firstGet.start();

        final AtomicReference<IllegalStateException> secondRefernce = new AtomicReference<IllegalStateException>();
        final IRunnable secondTarget = getRunnable(holder, barrier, secondRefernce);
        final Thread secondGet = new Thread(secondTarget);
        secondGet.start();

        // release the hounds
        await(barrier);

        // wait for the hounds
        await(barrier);

        firstGet.join();
        secondGet.join();

        Assert.Null(firstReference.get());
        Assert.Null(secondRefernce.get());
    }

    private static IRunnable getRunnable(
            final NettyRuntime.AvailableProcessorsHolder holder,
            final CyclicBarrier barrier,
            final AtomicReference<IllegalStateException> reference) {
        return new IRunnable() {
            @Override
            public void run() {
                await(barrier);
                try {
                    holder.availableProcessors();
                } catch (final IllegalStateException e) {
                    reference.set(e);
                }
                await(barrier);
            }
        };
    }

    [Fact]
    public void testRacingGetAndSet() {
        final NettyRuntime.AvailableProcessorsHolder holder = new NettyRuntime.AvailableProcessorsHolder();
        final CyclicBarrier barrier = new CyclicBarrier(3);
        final Thread get = new Thread(new IRunnable() {
            @Override
            public void run() {
                await(barrier);
                holder.availableProcessors();
                await(barrier);
            }
        });
        get.start();

        final AtomicReference<IllegalStateException> setException = new AtomicReference<IllegalStateException>();
        final Thread set = new Thread(new IRunnable() {
            @Override
            public void run() {
                await(barrier);
                try {
                    holder.setAvailableProcessors(2048);
                } catch (final IllegalStateException e) {
                    setException.set(e);
                }
                await(barrier);
            }
        });
        set.start();

        // release the hounds
        await(barrier);

        // wait for the hounds
        await(barrier);

        get.join();
        set.join();

        if (setException.get() == null) {
            Assert.Equal(2048, holder.availableProcessors());
        } else {
            Assert.NotNull(setException.get());
        }
    }

    [Fact]
    public void testGetWithSystemProperty() {
        final string availableProcessorsSystemProperty = SystemPropertyUtil.get("io.netty.availableProcessors");
        try {
            System.setProperty("io.netty.availableProcessors", "2048");
            final NettyRuntime.AvailableProcessorsHolder holder = new NettyRuntime.AvailableProcessorsHolder();
            Assert.Equal(2048, holder.availableProcessors());
        } finally {
            if (availableProcessorsSystemProperty != null) {
                System.setProperty("io.netty.availableProcessors", availableProcessorsSystemProperty);
            } else {
                System.clearProperty("io.netty.availableProcessors");
            }
        }
    }

    [Fact]
    [SuppressForbidden("testing fallback to Runtime#availableProcessors")]
    public void testGet() {
        final string availableProcessorsSystemProperty = SystemPropertyUtil.get("io.netty.availableProcessors");
        try {
            System.clearProperty("io.netty.availableProcessors");
            final NettyRuntime.AvailableProcessorsHolder holder = new NettyRuntime.AvailableProcessorsHolder();
            Assert.Equal(Runtime.getRuntime().availableProcessors(), holder.availableProcessors());
        } finally {
            if (availableProcessorsSystemProperty != null) {
                System.setProperty("io.netty.availableProcessors", availableProcessorsSystemProperty);
            } else {
                System.clearProperty("io.netty.availableProcessors");
            }
        }
    }

    private static void await(final CyclicBarrier barrier) {
        try {
            barrier.await();
        } catch (ThreadInterruptedException | BrokenBarrierException e) {
            fail(e.toString());
        }
    }
}
