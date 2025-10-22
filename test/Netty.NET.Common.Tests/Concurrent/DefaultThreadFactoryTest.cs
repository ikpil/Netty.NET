/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests.Concurrent;

public class DefaultThreadFactoryTest {

    [Fact(Timeout = 2000)]
    public void testDescendantThreadGroups() {
        // SecurityManager current = System.getSecurityManager();
        //
        // bool securityManagerSet = false;
        // try {
        //     try {
        //         // install security manager that only allows parent thread groups to mess with descendant thread groups
        //         System.setSecurityManager(new SecurityManager() {
        //             @Override
        //             public void checkAccess(ThreadGroup g) {
        //                 ThreadGroup source = Thread.currentThread().getThreadGroup();
        //
        //                 if (source != null) {
        //                     if (!source.parentOf(g)) {
        //                         throw new SecurityException("source group is not an ancestor of the target group");
        //                     }
        //                     super.checkAccess(g);
        //                 }
        //             }
        //
        //             // so we can restore the security manager at the end of the test
        //             @Override
        //             public void checkPermission(Permission perm) {
        //             }
        //         });
        //     } catch (UnsupportedOperationException e) {
        //         Assumptions.assumeFalse(true, "Setting SecurityManager not supported");
        //     }
        //     securityManagerSet = true;
        //
        //     // holder for the thread factory, plays the role of a global singleton
        //     AtomicReference<DefaultThreadFactory> factory = new AtomicReference<DefaultThreadFactory>();
        //     AtomicInteger counter = new AtomicInteger();
        //     Runnable task = new Runnable() {
        //         @Override
        //         public void run() {
        //             counter.incrementAndGet();
        //         }
        //     };
        //
        //     AtomicReference<Exception> interrupted = new AtomicReference<Exception>();
        //
        //     // create the thread factory, since we are running the thread group brother, the thread
        //     // factory will now forever be tied to that group
        //     // we then create a thread from the factory to run a "task" for us
        //     Thread first = new Thread(new ThreadGroup("brother"), new Runnable() {
        //         @Override
        //         public void run() {
        //             factory.set(new DefaultThreadFactory("test", false, Thread.NORM_PRIORITY, null));
        //             Thread t = factory.get().newThread(task);
        //             t.start();
        //             try {
        //                 t.join();
        //             } catch (InterruptedException e) {
        //                 interrupted.set(e);
        //                 Thread.currentThread().interrupt();
        //             }
        //         }
        //     });
        //     first.start();
        //     first.join();
        //
        //     assertNull(interrupted.get());
        //
        //     // now we will use factory again, this time from a sibling thread group sister
        //     // if DefaultThreadFactory is "sticky" about thread groups, a security manager
        //     // that forbids sibling thread groups from messing with each other will strike this down
        //     Thread second = new Thread(new ThreadGroup("sister"), new Runnable() {
        //         @Override
        //         public void run() {
        //             Thread t = factory.get().newThread(task);
        //             t.start();
        //             try {
        //                 t.join();
        //             } catch (InterruptedException e) {
        //                 interrupted.set(e);
        //                 Thread.currentThread().interrupt();
        //             }
        //         }
        //     });
        //     second.start();
        //     second.join();
        //
        //     assertNull(interrupted.get());
        //
        //     assertEquals(2, counter.get());
        // } finally {
        //     if (securityManagerSet) {
        //         System.setSecurityManager(current);
        //     }
        // }
    }

    // test that when DefaultThreadFactory is constructed with a sticky thread group, threads
    // created by it have the sticky thread group
    [Fact(Timeout = 2000)]
    public void testDefaultThreadFactoryStickyThreadGroupConstructor() {
        ThreadGroup sticky = new ThreadGroup("sticky");
        runStickyThreadGroupTest(
            new AnonymousCallable<DefaultThreadFactory>(() => new DefaultThreadFactory("test", false, ThreadPriority.Normal, sticky)),
            sticky
        );
    }

    // test that when a security manager is installed that provides a ThreadGroup, DefaultThreadFactory inherits from
    // the security manager
    [Fact(Timeout = 2000)]
    public void testDefaultThreadFactoryInheritsThreadGroupFromSecurityManager() {
        SecurityManager current = System.getSecurityManager();

        bool securityManagerSet = false;
        try {
            ThreadGroup sticky = new ThreadGroup("sticky");
            try {
                System.setSecurityManager(new SecurityManager() {
                    @Override
                    public ThreadGroup getThreadGroup() {
                        return sticky;
                    }

                    // so we can restore the security manager at the end of the test
                    @Override
                    public void checkPermission(Permission perm) {
                    }
                });
            } catch (UnsupportedOperationException e) {
                Assumptions.assumeFalse(true, "Setting SecurityManager not supported");
            }
            securityManagerSet = true;

            runStickyThreadGroupTest(
                    new Callable<DefaultThreadFactory>() {
                        @Override
                        public DefaultThreadFactory call() throws Exception {
                            return new DefaultThreadFactory("test");
                        }
                    },
                    sticky);
        } finally {
            if (securityManagerSet) {
                System.setSecurityManager(current);
            }
        }
    }

    private static void runStickyThreadGroupTest(ICallable<DefaultThreadFactory> callable, ThreadGroup expected) {
        AtomicReference<ThreadGroup> captured = new AtomicReference<ThreadGroup>();
        AtomicReference<Exception> exception = new AtomicReference<Exception>();

        Thread first = new Thread(new ThreadGroup("wrong"), new Runnable() {
            @Override
            public void run() {
                DefaultThreadFactory factory;
                try {
                    factory = callable.call();
                } catch (Exception e) {
                    exception.set(e);
                    throw new RuntimeException(e);
                }
                Thread t = factory.newThread(new Runnable() {
                    @Override
                    public void run() {
                    }
                });
                captured.set(t.getThreadGroup());
            }
        });
        first.start();
        first.join();

        assertNull(exception.get());

        assertEquals(expected, captured.get());
    }

    // test that when DefaultThreadFactory is constructed without a sticky thread group, threads
    // created by it inherit the correct thread group
    [Fact(Timeout = 2000)]
    public void testDefaultThreadFactoryNonStickyThreadGroupConstructor() {

        AtomicReference<DefaultThreadFactory> factory = new AtomicReference<DefaultThreadFactory>();
        AtomicReference<ThreadGroup> firstCaptured = new AtomicReference<ThreadGroup>();

        ThreadGroup firstGroup = new ThreadGroup("first");
        Thread first = new Thread(firstGroup, new Runnable() {
            @Override
            public void run() {
                factory.set(new DefaultThreadFactory("sticky", false, Thread.NORM_PRIORITY, null));
                Thread t = factory.get().newThread(new Runnable() {
                    @Override
                    public void run() {
                    }
                });
                firstCaptured.set(t.getThreadGroup());
            }
        });
        first.start();
        first.join();

        assertEquals(firstGroup, firstCaptured.get());

        AtomicReference<ThreadGroup> secondCaptured = new AtomicReference<ThreadGroup>();

        ThreadGroup secondGroup = new ThreadGroup("second");
        Thread second = new Thread(secondGroup, new Runnable() {
            @Override
            public void run() {
                Thread t = factory.get().newThread(new Runnable() {
                    @Override
                    public void run() {
                    }
                });
                secondCaptured.set(t.getThreadGroup());
            }
        });
        second.start();
        second.join();

        assertEquals(secondGroup, secondCaptured.get());
    }

    // test that when DefaultThreadFactory is constructed without a sticky thread group, threads
    // created by it inherit the correct thread group
    [Fact(Timeout = 2000)]
    public void testCurrentThreadGroupIsUsed() {
        AtomicReference<DefaultThreadFactory> factory = new AtomicReference<DefaultThreadFactory>();
        AtomicReference<ThreadGroup> firstCaptured = new AtomicReference<ThreadGroup>();

        ThreadGroup group = new ThreadGroup("first");
        Thread first = new Thread(group, new Runnable() {
            @Override
            public void run() {
                Thread current = Thread.currentThread();
                firstCaptured.set(current.getThreadGroup());
                factory.set(new DefaultThreadFactory("sticky", false));
            }
        });
        first.start();
        first.join();
        assertEquals(group, firstCaptured.get());

        ThreadGroup currentThreadGroup = Thread.currentThread().getThreadGroup();
        Thread second = factory.get().newThread(new Runnable() {
            @Override
            public void run() {
                // NOOP.
            }
        });
        second.join();
        assertEquals(currentThreadGroup, currentThreadGroup);
    }
}
