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

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Test;

/**
 * Junit 5 extension for leak detection using {@link LeakPresenceDetector}.
 * <p>
 * Leak presence is checked at the class level. Any resource must be closed at the end of the test class, by the time
 * {@link org.junit.jupiter.api.AfterAll} has been called. Method-level detection is not possible because some tests
 * retain resources between methods on the same class, notably parameterized tests that allocate different buffers
 * before running the test methods with those buffers.
 * <p>
 * This extension supports parallel test execution, but has to make some assumptions about the thread lifecycle. The
 * resource scope for the class is created in {@link org.junit.jupiter.api.BeforeAll}, and then saved in a thread local
 * on each {@link org.junit.jupiter.api.BeforeEach}. This appears to work well with junit's default parallelism,
 * despite the use of a fork-join pool that can transfer tasks between threads, but it may lead to problems if tests
 * make use of fork-join machinery themselves.
 * <p>
 * The ThreadLocal holding the scope is {@link InheritableThreadLocal inheritable}, so that e.g. event loops created
 * in a test are assigned to the test resource scope.
 */
// CLR: xUnit v3 has no extension callbacks. A test assembly registers this extension with
// [assembly: TestFramework(typeof(LeakPresenceTestFramework))], the counterpart of the JUnit service file
// (META-INF/services/org.junit.jupiter.api.extension.Extension) and junit.jupiter.extensions.autodetection.enabled
// of the parent pom (argLine.leak). LeakPresenceTestFramework (CLR-only, LeakPresenceTestFramework.cs) runs
// BeforeAll before xUnit runs a test class (class fixtures, constructors, tests and fixture disposal) and AfterAll
// after it, as JUnit runs the callbacks around @BeforeAll and @AfterAll.
//
// The scope holder is an AsyncLocal in place of the InheritableThreadLocal. BeforeAll sets it in the flow that runs the
// class, and the ExecutionContext carries it into every test of the class, into Tasks and thread-pool work items they
// queue and into threads they start (Thread.Start captures the ExecutionContext), so an event loop created in a test
// is assigned to the test resource scope as in Java. Because each test already runs in the flow of its class, the
// beforeEach and afterEach callbacks, which copy the class scope into the test thread's thread local and restore the
// previous one, have no counterpart; parallel classes do not share the holder, so no thread transfer can mix scopes.
public static class LeakPresenceExtension
{
    internal const string LEAK_PRESENCE_DETECTION_DISABLED_PROPERTY =
        "io.netty.test.leakPresenceDetection.disabled";

    // CLR: SCOPE_KEY and PREVIOUS_SCOPE_KEY are keys of JUnit's ExtensionContext.Store; the class scope is the
    // AsyncLocal WithTransferableScope.SCOPE of the flow that runs the class, and nothing is stored.

    static LeakPresenceExtension()
    {
        if (!GetBoolean(LEAK_PRESENCE_DETECTION_DISABLED_PROPERTY))
        {
            // CLR: environment variables are the system properties (SystemPropertyUtil). The value is the
            // assembly-qualified name of the generic definition, which DefaultResourceLeakDetectorFactory closes over
            // each resource type. The factory reads it once, so the framework initializes this class before any
            // resource leak detector is created (LeakPresenceTestFramework constructor).
            Environment.SetEnvironmentVariable("io.netty.customResourceLeakDetector",
                typeof(WithTransferableScope<>).AssemblyQualifiedName);
        }
    }

    // CLR: runs the static constructor, as loading the extension class runs its static initializer in Java.
    internal static void Initialize()
    {
    }

    // CLR: Boolean.getBoolean is true only for the value "true", ignoring case; SystemPropertyUtil.GetBoolean also
    // accepts "yes" and "1".
    private static bool GetBoolean(string name)
    {
        return "true".Equals(SystemPropertyUtil.Get(name), StringComparison.OrdinalIgnoreCase);
    }

    // CLR: ExecutionCondition.evaluateExecutionCondition. The framework applies the extension only when this returns
    // true. With the property set, the test classes run without leak presence detection, as they do in the Java build's
    // leak profile, where the extension is not autodetected; JUnit would also skip a class that registers the extension
    // with @ExtendWith, which no ported test does (the two that do, OpenSslEngineTest and OpenSslCredentialBuilderTest,
    // are library-not-ported with the OpenSSL units).
    internal static bool EvaluateExecutionCondition(out string reason)
    {
        if (GetBoolean(LEAK_PRESENCE_DETECTION_DISABLED_PROPERTY))
        {
            reason = "Leak presence detection disabled by " + LEAK_PRESENCE_DETECTION_DISABLED_PROPERTY;
            return false;
        }

        reason = "Leak presence detection enabled";
        return true;
    }

    // CLR: beforeAll(ExtensionContext). testClass is context.getRequiredTestClass() and displayName
    // context.getDisplayName(), the simple class name. It sets the holder of the caller's flow, so the caller is the
    // async method that then runs the class.
    internal static void BeforeAll(Type testClass, string displayName)
    {
        ScopeWrapper existingScope = WithTransferableScope.SCOPE.Value;
        if (existingScope == null)
        {
            ScopeWrapper scope = new ScopeWrapper(
                new LeakPresenceDetector.ResourceScope(displayName), testClass);
            WithTransferableScope.SCOPE.Value = scope;
            return;
        }

        // JUnit creates a distinct ExtensionContext for each @Nested class. Those nested classes must reuse the
        // shared outer scope, but only when they are enclosed by the class that originally created it.
        // CLR: xUnit has no @Nested lifecycle: a nested C# class is a test class of its own, run by its own flow, so
        // the holder is empty here and the nested class gets its own scope. The check remains for a class run inside
        // another class's flow.
        if (!IsOwnedBy(testClass, existingScope.owner))
        {
            throw new InvalidOperationException("Weird context lifecycle");
        }

        WithTransferableScope.SCOPE.Value = existingScope;
    }

    // CLR: afterAll(ExtensionContext). The caller passes the scope that BeforeAll set in its flow. Asynchronous: the
    // waits are Task.Delay in place of TimeUnit.MILLISECONDS.sleep, so no thread is blocked.
    internal static async ValueTask AfterAll(ScopeWrapper scope, Type testClass)
    {
        if (scope == null)
        {
            return;
        }
        if (scope.owner != testClass)
        {
            return;
        }

        // Wait some time for resources to close. Many tests do loop.shutdownGracefully without waiting, and that's ok.
        long start = Stopwatch.GetTimestamp();

        while (scope.scope.HasOpenResources() && Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(100);
        }

        scope.scope.Close();
    }

    /**
     * Accept the class that created the shared scope and any of its @Nested classes.
     *
     * JUnit models nested test classes as separate Class objects and separate ExtensionContexts, not as subclasses of
     * the outer test class. That means a simple assignability check would reject legitimate nested usage and cause the
     * nested class to fail in beforeAll even though it should reuse the outer scope.
     */
    private static bool IsOwnedBy(Type testClass, Type owner)
    {
        Type current = testClass;

        while (current != null)
        {
            if (current == owner)
            {
                return true;
            }

            // CLR: Class.getEnclosingClass() is Type.DeclaringType.
            current = current.DeclaringType;
        }

        return false;
    }

    // CLR: the static SCOPE of the generic WithTransferableScope<T> is one field for every T in Java (erasure); the
    // static fields of a CLR generic class exist once per constructed type, so the field is in this non-generic class.
    public static class WithTransferableScope
    {
        internal static readonly AsyncLocal<ScopeWrapper> SCOPE = new AsyncLocal<ScopeWrapper>();
    }

    public sealed class WithTransferableScope<T> : LeakPresenceDetector<T> where T : class
    {
        public WithTransferableScope(Type resourceType, int samplingInterval)
            : base(resourceType)
        {
        }

        public WithTransferableScope(Type resourceType, int samplingInterval, long maxActive)
            : base(resourceType)
        {
        }

        protected override LeakPresenceDetector.ResourceScope CurrentScope()
        {
            // CLR: Objects.requireNonNull(obj, message) throws a NullPointerException with the message.
            ScopeWrapper scope = WithTransferableScope.SCOPE.Value;
            if (scope == null)
            {
                throw new NullReferenceException("Resource created outside test?");
            }

            return scope.scope;
        }
    }

    /**
     * Prevent junit from closing the ResourceScope automatically.
     */
    // CLR: JUnit closes the ExtensionContext.Store values that are CloseableResource (AutoCloseable since JUnit 5.13)
    // when their context ends; nothing in xUnit closes the scope, which AfterAll closes.
    internal sealed class ScopeWrapper
    {
        internal readonly LeakPresenceDetector.ResourceScope scope;
        /**
         * The test class that originally created the shared scope.
         *
         * Nested classes reuse the same scope but must not close it in afterAll; only this owner may do the final
         * close and remove the scope from the store.
         */
        internal readonly Type owner;

        internal ScopeWrapper(LeakPresenceDetector.ResourceScope scope, Type owner)
        {
            this.scope = scope;
            this.owner = owner;
        }
    }
}
