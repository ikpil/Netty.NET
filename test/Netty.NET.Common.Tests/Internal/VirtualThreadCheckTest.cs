/*
 * Copyright 2025 The Netty Project
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
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

public class VirtualThreadCheckTest
{
    private static class SubThread : Thread
    {
        // For test
    }

    private static class SubOfSubThread : SubThread
    {
        // For test
    }

    [Fact]
    public void testCheckVirtualThread()
    {
        Assert.False(PlatformDependent.isVirtualThread(null));
        Assert.False(PlatformDependent.isVirtualThread(Thread.CurrentThread));
        FastThreadLocalThread fastThreadLocalThread = new FastThreadLocalThread(Runnables.Empty);
        Assert.False(PlatformDependent.isVirtualThread(fastThreadLocalThread));
        AtomicBoolean atomicRes = new AtomicBoolean();
        Thread subThread = new Thread(() =>
        {
            atomicRes.set(PlatformDependent.isVirtualThread(Thread.CurrentThread));
        });

        subThread.Start();
        subThread.Join();
        Assert.False(atomicRes.get());

        Thread subOfSubThread = new SubThread(() =>
        {
            atomicRes.set(PlatformDependent.isVirtualThread(Thread.CurrentThread));
        });
        subOfSubThread.Start();
        subOfSubThread.Join();
        Assert.False(atomicRes.get());

        Thread subOfSubOfSubThread = new SubOfSubThread(() =>
        {
            atomicRes.set(PlatformDependent.isVirtualThread(Thread.CurrentThread));
        });
        subOfSubOfSubThread.Start();
        subOfSubOfSubThread.Join();
        Assert.False(atomicRes.get());

        assumeTrue(PlatformDependent.javaVersion() >= 21);
        Method startVirtualThread = getStartVirtualThreadMethod();
        Thread virtualThread = (Thread)startVirtualThread.invoke(null, Runnables.Empty);
        Assert.True(PlatformDependent.isVirtualThread(virtualThread));
    }

    [Fact]
    public void testGetVirtualThreadCheckMethod()
    {
        if (PlatformDependent.javaVersion() < 19)
        {
            Assert.Null(IS_VIRTUAL_THREAD_METHOD_HANDLE);
        }
        else
        {
            assumeTrue(PlatformDependent.javaVersion() >= 21);
            assumeTrue(IS_VIRTUAL_THREAD_METHOD_HANDLE != null);
            bool isVirtual = (bool)IS_VIRTUAL_THREAD_METHOD_HANDLE.invokeExact(Thread.CurrentThread);
            Assert.False(isVirtual);

            Method startVirtualThread = getStartVirtualThreadMethod();
            Thread virtualThread = (Thread)startVirtualThread.invoke(null, Runnables.Empty);
            isVirtual = (bool)IS_VIRTUAL_THREAD_METHOD_HANDLE.invokeExact(virtualThread);
            Assert.True(isVirtual);
        }
    }

    private Method getStartVirtualThreadMethod()
    {
        return Thread.class.getMethod("startVirtualThread", IRunnable.class);
    }
}