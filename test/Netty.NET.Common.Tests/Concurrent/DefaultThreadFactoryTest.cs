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
using Xunit;

namespace Netty.NET.Common.Tests.Concurrent;

public class DefaultThreadFactoryTest
{
    [Fact]
    public void TestDescendantThreadGroups()
    {
        // install security manager that only allows parent thread groups to mess with descendant thread groups
        // so we can restore the security manager at the end of the test
        // CLR: SecurityManager is unavailable. Verify the inherited identities and
        // original task count directly; this does not test a JVM permission policy.

        // holder for the thread factory, plays the role of a global singleton
        DefaultThreadFactory factory = null;
        int counter = 0;
        Action task = () => Interlocked.Increment(ref counter);
        Exception interrupted = null;

        // create the thread factory, since we are running the thread group brother, the thread
        // factory will now forever be tied to that group
        // we then create a thread from the factory to run a "task" for us
        var brother = new ThreadGroup("brother");
        ThreadGroup firstCaptured = null;
        var first = brother.NewThread(() =>
        {
            try
            {
                factory = new DefaultThreadFactory("test", false, ThreadPriority.Normal, null);
                Thread t = factory.NewThread(task);
                firstCaptured = ThreadGroup.GetThreadGroup(t);
                t.Start();
                if (!t.Join(TimeSpan.FromSeconds(2))) throw new TimeoutException();
            }
            catch (Exception error) { interrupted = error; }
        });
        first.Start();
        Assert.True(first.Join(TimeSpan.FromSeconds(2)));
        Assert.Null(interrupted);
        Assert.Same(brother, firstCaptured);

        // now we will use factory again, this time from a sibling thread group sister
        // if DefaultThreadFactory is "sticky" about thread groups, a security manager
        // that forbids sibling thread groups from messing with each other will strike this down
        var sister = new ThreadGroup("sister");
        ThreadGroup secondCaptured = null;
        var second = sister.NewThread(() =>
        {
            try
            {
                Thread t = factory.NewThread(task);
                secondCaptured = ThreadGroup.GetThreadGroup(t);
                t.Start();
                if (!t.Join(TimeSpan.FromSeconds(2))) throw new TimeoutException();
            }
            catch (Exception error) { interrupted = error; }
        });
        second.Start();
        Assert.True(second.Join(TimeSpan.FromSeconds(2)));
        Assert.Null(interrupted);
        Assert.Same(sister, secondCaptured);
        Assert.Equal(2, Volatile.Read(ref counter));
    }

    // test that when DefaultThreadFactory is constructed with a sticky thread group, threads
    // created by it have the sticky thread group
    [Fact]
    public void TestDefaultThreadFactoryStickyThreadGroupConstructor()
    {
        var sticky = new ThreadGroup("sticky");
        RunStickyThreadGroupTest(() => new DefaultThreadFactory("test", false, ThreadPriority.Normal, sticky), sticky);
    }

    // test that when a security manager is installed that provides a ThreadGroup, DefaultThreadFactory inherits from
    // the security manager
    [Fact(Skip = "CLR has no JVM SecurityManager; upstream also skips when installing it is unsupported.")]
    public void TestDefaultThreadFactoryInheritsThreadGroupFromSecurityManager()
    {
        // so we can restore the security manager at the end of the test
        // CLR: no process-wide security manager or implicit security-manager group
        // exists. The explicit-group constructor is verified independently above.
        throw new NotSupportedException("JVM SecurityManager is not available on the CLR.");
    }

    private static void RunStickyThreadGroupTest(Func<DefaultThreadFactory> callable, ThreadGroup expected)
    {
        ThreadGroup captured = null;
        Exception exception = null;
        var first = new ThreadGroup("wrong").NewThread(() =>
        {
            try
            {
                DefaultThreadFactory factory = callable();
                Thread t = factory.NewThread(() => { });
                captured = ThreadGroup.GetThreadGroup(t);
            }
            catch (Exception error) { exception = error; }
        });
        first.Start();
        Assert.True(first.Join(TimeSpan.FromSeconds(2)));
        Assert.Null(exception);
        Assert.Same(expected, captured);
    }

    // test that when DefaultThreadFactory is constructed without a sticky thread group, threads
    // created by it inherit the correct thread group
    [Fact]
    public void TestDefaultThreadFactoryNonStickyThreadGroupConstructor()
    {
        DefaultThreadFactory factory = null;
        ThreadGroup firstCaptured = null;
        var firstGroup = new ThreadGroup("first");
        var first = firstGroup.NewThread(() =>
        {
            factory = new DefaultThreadFactory("sticky", false, ThreadPriority.Normal, null);
            Thread t = factory.NewThread(() => { });
            firstCaptured = ThreadGroup.GetThreadGroup(t);
        });
        first.Start();
        Assert.True(first.Join(TimeSpan.FromSeconds(2)));
        Assert.Same(firstGroup, firstCaptured);

        ThreadGroup secondCaptured = null;
        var secondGroup = new ThreadGroup("second");
        var second = secondGroup.NewThread(() =>
        {
            Thread t = factory.NewThread(() => { });
            secondCaptured = ThreadGroup.GetThreadGroup(t);
        });
        second.Start();
        Assert.True(second.Join(TimeSpan.FromSeconds(2)));
        Assert.Same(secondGroup, secondCaptured);
    }

    // test that when DefaultThreadFactory is constructed without a sticky thread group, threads
    // created by it inherit the correct thread group
    [Fact]
    public void TestCurrentThreadGroupIsUsed()
    {
        DefaultThreadFactory factory = null;
        ThreadGroup firstCaptured = null;
        var group = new ThreadGroup("first");
        var first = group.NewThread(() =>
        {
            firstCaptured = ThreadGroup.GetThreadGroup(Thread.CurrentThread);
            factory = new DefaultThreadFactory("sticky", false);
        });
        first.Start();
        Assert.True(first.Join(TimeSpan.FromSeconds(2)));
        Assert.Same(group, firstCaptured);

        ThreadGroup currentThreadGroup = ThreadGroup.CurrentThreadGroup();
        Thread second = factory.NewThread(() =>
        {
            // NOOP.
        });
        // CLR cannot join an unstarted Thread. Inspect its assigned identity instead,
        // strengthening the upstream's final self-equality assertion.
        Assert.Same(currentThreadGroup, ThreadGroup.GetThreadGroup(second));
    }
}
