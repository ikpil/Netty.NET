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

namespace Netty.Common.Tests.Concurrent
{
    public class DefaultThreadFactoryTest {

        @Test
            @Timeout(value = 2000, unit = TimeUnit.MILLISECONDS)
        public void testDescendantThreadGroups() throws ThreadInterruptedException {
            final SecurityManager current = System.getSecurityManager();

            boolean securityManagerSet = false;
            try {
            try {
            // install security manager that only allows parent thread groups to mess with descendant thread groups
            System.setSecurityManager(new SecurityManager() {
            @Override
            public void checkAccess(ThreadGroup g) {
            final ThreadGroup source = Thread.CurrentThread.getThreadGroup();

            if (source != null) {
                if (!source.parentOf(g)) {
                    throw new SecurityException("source group is not an ancestor of the target group");
                }
                super.checkAccess(g);
            }
        }

        // so we can restore the security manager at the end of the test
        @Override
        public void checkPermission(Permission perm) {
        }
    });
}

} catch (NotSupportedException e) {
                Assumptions.assumeFalse(true, "Setting SecurityManager not supported");
            }
            securityManagerSet = true;

            // holder for the thread factory, plays the role of a global singleton
            final AtomicReference<DefaultThreadFactory> factory = new AtomicReference<DefaultThreadFactory>();
            final AtomicInteger counter = new AtomicInteger();
            final IRunnable task = new IRunnable() {
                @Override
                public void run() {
                    counter.incrementAndGet();
                }
            };

            final AtomicReference<Throwable> interrupted = new AtomicReference<Throwable>();

            // create the thread factory, since we are running the thread group brother, the thread
            // factory will now forever be tied to that group
            // we then create a thread from the factory to run a "task" for us
            final Thread first = new Thread(new ThreadGroup("brother"), new IRunnable() {
                @Override
                public void run() {
                    factory.set(new DefaultThreadFactory("test", false, ThreadPriority.Normal, null));
                    final Thread t = factory.get().newThread(task);
                    t.start();
                    try {
                        t.join();
                    } catch (ThreadInterruptedException e) {
                        interrupted.set(e);
                        Thread.CurrentThread.interrupt();
                    }
                }
            });
            first.start();
            first.join();

            assertNull(interrupted.get());

            // now we will use factory again, this time from a sibling thread group sister
            // if DefaultThreadFactory is "sticky" about thread groups, a security manager
            // that forbids sibling thread groups from messing with each other will strike this down
            final Thread second = new Thread(new ThreadGroup("sister"), new IRunnable() {
                @Override
                public void run() {
                    final Thread t = factory.get().newThread(task);
                    t.start();
                    try {
                        t.join();
                    } catch (ThreadInterruptedException e) {
                        interrupted.set(e);
                        Thread.CurrentThread.interrupt();
                    }
                }
            });
            second.start();
            second.join();

            assertNull(interrupted.get());

            assertEquals(2, counter.get());
        } finally {
            if (securityManagerSet) {
                System.setSecurityManager(current);
            }
        }
    }

    // test that when DefaultThreadFactory is constructed with a sticky thread group, threads
    // created by it have the sticky thread group
    @Test
    @Timeout(value = 2000, unit = TimeUnit.MILLISECONDS)
    public void testDefaultThreadFactoryStickyThreadGroupConstructor() throws ThreadInterruptedException {
        final ThreadGroup sticky = new ThreadGroup("sticky");
        runStickyThreadGroupTest(
                new Callable<DefaultThreadFactory>() {
                    @Override
                    public DefaultThreadFactory call() throws Exception {
                        return new DefaultThreadFactory("test", false, ThreadPriority.Normal, sticky);
                    }
                },
                sticky);
    }

    // test that when a security manager is installed that provides a ThreadGroup, DefaultThreadFactory inherits from
    // the security manager
    @Test
    @Timeout(value = 2000, unit = TimeUnit.MILLISECONDS)
    public void testDefaultThreadFactoryInheritsThreadGroupFromSecurityManager() throws ThreadInterruptedException {
        final SecurityManager current = System.getSecurityManager();

        boolean securityManagerSet = false;
        try {
            final ThreadGroup sticky = new ThreadGroup("sticky");
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
            } catch (NotSupportedException e) {
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

    private static void runStickyThreadGroupTest(
            final Callable<DefaultThreadFactory> callable,
            final ThreadGroup expected) throws ThreadInterruptedException {
        final AtomicReference<ThreadGroup> captured = new AtomicReference<ThreadGroup>();
        final AtomicReference<Throwable> exception = new AtomicReference<Throwable>();

        final Thread first = new Thread(new ThreadGroup("wrong"), new IRunnable() {
            @Override
            public void run() {
                final DefaultThreadFactory factory;
                try {
                    factory = callable.call();
                } catch (Exception e) {
                    exception.set(e);
                    throw new Exception(e);
                }
                final Thread t = factory.newThread(new IRunnable() {
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
    @Test
    @Timeout(value = 2000, unit = TimeUnit.MILLISECONDS)
    public void testDefaultThreadFactoryNonStickyThreadGroupConstructor() throws ThreadInterruptedException {

        final AtomicReference<DefaultThreadFactory> factory = new AtomicReference<DefaultThreadFactory>();
        final AtomicReference<ThreadGroup> firstCaptured = new AtomicReference<ThreadGroup>();

        final ThreadGroup firstGroup = new ThreadGroup("first");
        final Thread first = new Thread(firstGroup, new IRunnable() {
            @Override
            public void run() {
                factory.set(new DefaultThreadFactory("sticky", false, ThreadPriority.Normal, null));
                final Thread t = factory.get().newThread(new IRunnable() {
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

        final AtomicReference<ThreadGroup> secondCaptured = new AtomicReference<ThreadGroup>();

        final ThreadGroup secondGroup = new ThreadGroup("second");
        final Thread second = new Thread(secondGroup, new IRunnable() {
            @Override
            public void run() {
                final Thread t = factory.get().newThread(new IRunnable() {
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
    @Test
    @Timeout(value = 2000, unit = TimeUnit.MILLISECONDS)
    public void testCurrentThreadGroupIsUsed() throws ThreadInterruptedException {
        final AtomicReference<DefaultThreadFactory> factory = new AtomicReference<DefaultThreadFactory>();
        final AtomicReference<ThreadGroup> firstCaptured = new AtomicReference<ThreadGroup>();

        final ThreadGroup group = new ThreadGroup("first");
        final Thread first = new Thread(group, new IRunnable() {
            @Override
            public void run() {
                final Thread current = Thread.CurrentThread;
                firstCaptured.set(current.getThreadGroup());
                factory.set(new DefaultThreadFactory("sticky", false));
            }
        });
        first.start();
        first.join();
        assertEquals(group, firstCaptured.get());

        ThreadGroup currentThreadGroup = Thread.CurrentThread.getThreadGroup();
        Thread second = factory.get().newThread(new IRunnable() {
            @Override
            public void run() {
                // NOOP.
            }
        });
        second.join();
        assertEquals(currentThreadGroup, currentThreadGroup);
    }
}
