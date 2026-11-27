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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static Netty.NET.Common.Test.LeakPresenceExtension;

// CLR: testsuite-common's own tests run with the extension, which its service file registers for them as for every
// module that depends on it.
[assembly: TestFramework(typeof(Netty.NET.Common.Test.LeakPresenceTestFramework))]

namespace Netty.NET.Common.Test;

// CLR: the source tests run the nested classes in-process with org.junit.platform.testkit (EngineTestKit) and assert
// their container and test events. xUnit v3 has no in-process engine test kit, so each test drives the lifecycle that
// JUnit runs for the selected class through the extension members, in a flow of its own as LeakPresenceTestFramework
// runs a class (BeforeAll, the tests, AfterAll), and asserts what the events show: no failure, the tests run in the
// class scope, the scope closed by its owner. The nested classes remain test classes, which LeakPresenceTestFramework
// runs in this assembly with their own scopes (xUnit has no @Nested lifecycle).
public class LeakPresenceExtensionTest
{
    private static readonly ResourceLeakDetector<object> DETECTOR =
        ResourceLeakDetectorFactory.Instance().NewResourceLeakDetector<object>(typeof(object));

    [Fact]
    public Task NestedClassLifecycleIsSupported() => RunAsClass(async () =>
    {
        // CLR: JUnit runs the @Nested InnerTest inside the lifecycle of OuterNestedTest: beforeAll of the outer class,
        // outerTest, beforeAll of the nested class (which reuses the outer scope), innerTest, afterAll of the nested
        // class (which does not close the scope it does not own), afterAll of the outer class.
        BeforeAll(typeof(OuterNestedTest), nameof(OuterNestedTest));
        ScopeWrapper outer = WithTransferableScope.SCOPE.Value;
        Assert.NotNull(outer);
        Assert.Same(typeof(OuterNestedTest), outer.owner);
        TrackAndClose();

        BeforeAll(typeof(OuterNestedTest.InnerTest), nameof(OuterNestedTest.InnerTest));
        Assert.Same(outer, WithTransferableScope.SCOPE.Value);
        TrackAndClose();
        await AfterAll(outer, typeof(OuterNestedTest.InnerTest));
        TrackAndClose();

        await AfterAll(outer, typeof(OuterNestedTest));
        Assert.Throws<LeakPresenceDetector.AllocationProhibitedException>(() => DETECTOR.Track(new object()));
    });

    [Fact]
    public void ExplicitExtensionTestIsSkippedWhenLeakPresenceDetectionIsDisabled()
    {
        string previous = Environment.GetEnvironmentVariable(LEAK_PRESENCE_DETECTION_DISABLED_PROPERTY);
        Environment.SetEnvironmentVariable(LEAK_PRESENCE_DETECTION_DISABLED_PROPERTY, "true");
        try
        {
            // CLR: the condition that skips the explicitly extended class (results.containerEvents().skipped());
            // LeakPresenceTestFramework applies the extension only when it is enabled.
            Assert.False(EvaluateExecutionCondition(out string reason));
            Assert.Equal("Leak presence detection disabled by " + LEAK_PRESENCE_DETECTION_DISABLED_PROPERTY, reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LEAK_PRESENCE_DETECTION_DISABLED_PROPERTY, previous);
        }
        Assert.True(EvaluateExecutionCondition(out string enabled));
        Assert.Equal("Leak presence detection enabled", enabled);
    }

    [Fact]
    public Task InheritedTestClassExecutesParentAndChildTests() => RunAsClass(async () =>
    {
        // CLR: JUnit runs parentTest and childTest in the lifecycle of InheritedChildTest; the scope is owned by the
        // class that runs, not by the class that declares the test.
        BeforeAll(typeof(InheritedChildTest), nameof(InheritedChildTest));
        ScopeWrapper scope = WithTransferableScope.SCOPE.Value;
        Assert.Same(typeof(InheritedChildTest), scope.owner);
        TrackAndClose();
        TrackAndClose();

        await AfterAll(scope, typeof(InheritedParentTest));
        TrackAndClose();
        await AfterAll(scope, typeof(InheritedChildTest));
        Assert.Throws<LeakPresenceDetector.AllocationProhibitedException>(() => DETECTOR.Track(new object()));
    });

    // CLR supplement: the framework installed the detector before any detector was created, and the class scope
    // reaches the test, a thread the test starts and a Task it queues (the ExecutionContext in place of the
    // InheritableThreadLocal).
    [Fact]
    public async Task ClassScopeFlowsIntoTestsThreadsAndTasks()
    {
        Assert.IsType<WithTransferableScope<object>>(DETECTOR);
        ScopeWrapper scope = WithTransferableScope.SCOPE.Value;
        Assert.NotNull(scope);
        Assert.Same(typeof(LeakPresenceExtensionTest), scope.owner);
        Assert.False(scope.scope.HasOpenResources());

        IResourceLeakTracker<object> tracker = DETECTOR.Track(new object());
        Assert.True(scope.scope.HasOpenResources());
        Assert.True(tracker.Close(null));
        Assert.False(scope.scope.HasOpenResources());

        IResourceLeakTracker<object> threadTracker = null;
        Thread thread = new Thread(() => threadTracker = DETECTOR.Track(new object()));
        thread.Start();
        thread.Join();
        Assert.True(scope.scope.HasOpenResources());
        Assert.True(threadTracker.Close(null));

        IResourceLeakTracker<object> taskTracker = await Task.Run(() => DETECTOR.Track(new object()));
        Assert.True(scope.scope.HasOpenResources());
        Assert.True(taskTracker.Close(null));
        Assert.False(scope.scope.HasOpenResources());
    }

    // CLR supplement: AfterAll waits for resources that close after the class finished (loop.shutdownGracefully
    // without waiting), and fails with the detector's message when one stays open.
    [Fact]
    public Task AfterAllWaitsForResourcesToClose() => RunAsClass(async () =>
    {
        BeforeAll(typeof(InheritedParentTest), "Closing");
        ScopeWrapper scope = WithTransferableScope.SCOPE.Value;
        IResourceLeakTracker<object> tracker = DETECTOR.Track(new object());
        Task close = Task.Run(async () =>
        {
            await Task.Delay(300);
            tracker.Close(null);
        });
        long start = Stopwatch.GetTimestamp();
        await AfterAll(scope, typeof(InheritedParentTest));
        Assert.True(Stopwatch.GetElapsedTime(start) >= TimeSpan.FromMilliseconds(250));
        await close;
    });

    [Fact]
    public Task AfterAllReportsALeak() => RunAsClass(async () =>
    {
        BeforeAll(typeof(InheritedParentTest), "Leaky");
        ScopeWrapper scope = WithTransferableScope.SCOPE.Value;
        DETECTOR.Track(new object());
        InvalidOperationException leak = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await AfterAll(scope, typeof(InheritedParentTest)));
        Assert.StartsWith("Possible memory leak detected for resource scope 'Leaky'. ", leak.Message);
    });

    [Fact]
    public Task ResourceOutsideTestIsRejected() => RunAsClass(() =>
    {
        NullReferenceException failure = Assert.Throws<NullReferenceException>(() => DETECTOR.Track(new object()));
        Assert.Equal("Resource created outside test?", failure.Message);
        return Task.CompletedTask;
    });

    // CLR supplement: the presence detector records nothing, so DisabledForSlowLeakDetection does not skip.
    [Fact]
    public void SlowLeakDetectionIsOffWithThePresenceDetector()
    {
        Assert.False(DisabledForSlowLeakDetection.SlowLeakDetection);
    }

    // CLR: runs body in a flow without a class scope, as LeakPresenceTestFramework starts each class; the scope
    // holder changes made in the flow do not reach this test's own class scope.
    private static Task RunAsClass(Func<Task> body) => Task.Run(() =>
    {
        WithTransferableScope.SCOPE.Value = null;
        return body();
    });

    private static void TrackAndClose()
    {
        Assert.True(DETECTOR.Track(new object()).Close(null));
    }

    public class OuterNestedTest
    {
        [Fact]
        public void OuterTest()
        {
        }

        // CLR: a class of its own in xUnit, run with its own scope.
        public class InnerTest
        {
            // CLR: innerTest; a member cannot have the name of its enclosing type (CS0542).
            [Fact]
            public void InnerTestMethod()
            {
            }
        }
    }

    public class InheritedParentTest
    {
        [Fact]
        public void ParentTest()
        {
        }
    }

    public class InheritedChildTest : InheritedParentTest
    {
        [Fact]
        public void ChildTest()
        {
        }
    }

    // CLR: @ExtendWith(LeakPresenceExtension.class) has no counterpart; every class of the assembly runs with the
    // extension (TestFramework attribute above).
    public class ExplicitExtensionTest
    {
        [Fact]
        public void Test()
        {
        }
    }
}
