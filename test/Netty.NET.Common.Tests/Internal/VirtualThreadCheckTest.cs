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

namespace Netty.NET.Common.Tests.Internal;

public class VirtualThreadCheckTest {

    [Fact]
    public void testCheckVirtualThread() {
        Assert.False(PlatformDependent.isVirtualThread(null));
        Assert.False(PlatformDependent.isVirtualThread(Thread.CurrentThread));
        FastThreadLocalThread fastThreadLocalThread = new FastThreadLocalThread();
        Assert.False(PlatformDependent.isVirtualThread(fastThreadLocalThread));
        final AtomicReference<Boolean> atomicRes = new AtomicReference<Boolean>();
        Thread subThread = new Thread() {
        @Override
        public void run() {
        atomicRes.set(PlatformDependent.isVirtualThread(Thread.CurrentThread));
    }
};
subThread.start();
subThread.join();
Assert.False(atomicRes.get());

Thread subOfSubThread = new SubThread() {
    @Override
    public void run() {
    atomicRes.set(PlatformDependent.isVirtualThread(Thread.CurrentThread));
}

};
subOfSubThread.start();
subOfSubThread.join();
Assert.False(atomicRes.get());

Thread subOfSubOfSubThread = new SubOfSubThread() {
    @Override
    public void run() {
    atomicRes.set(PlatformDependent.isVirtualThread(Thread.CurrentThread));
}
};
subOfSubOfSubThread.start();
subOfSubOfSubThread.join();
Assert.False(atomicRes.get());

assumeTrue(PlatformDependent.javaVersion() >= 21);
Method startVirtualThread = getStartVirtualThreadMethod();
Thread virtualThread = (Thread) startVirtualThread.invoke(null, new IRunnable() {
    @Override
    public void run() {
}
});
Assert.True(PlatformDependent.isVirtualThread(virtualThread));
}

[Fact]
public void testGetVirtualThreadCheckMethod() throws Exception {
    if (PlatformDependent.javaVersion() < 19) {
        Assert.Null(IS_VIRTUAL_THREAD_METHOD_HANDLE);
    } else {
        assumeTrue(PlatformDependent.javaVersion() >= 21);
        assumeTrue(IS_VIRTUAL_THREAD_METHOD_HANDLE != null);
        bool isVirtual = (bool) IS_VIRTUAL_THREAD_METHOD_HANDLE.invokeExact(Thread.CurrentThread);
        Assert.False(isVirtual);

        Method startVirtualThread = getStartVirtualThreadMethod();
        Thread virtualThread = (Thread) startVirtualThread.invoke(null, new IRunnable() {
            @Override
            public void run() {
        }
        });
        isVirtual = (bool) IS_VIRTUAL_THREAD_METHOD_HANDLE.invokeExact(virtualThread);
        Assert.True(isVirtual);
    }
}

private Method getStartVirtualThreadMethod() throws NoSuchMethodException {
    return Thread.class.getMethod("startVirtualThread", IRunnable.class);
}

private static class SubThread extends Thread {
    // For test
}

private static class SubOfSubThread extends SubThread {
    // For test
}
}