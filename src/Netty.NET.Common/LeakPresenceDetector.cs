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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

// Java static state is shared across erased T. Put it in a nongeneric CLR type.
public static class LeakPresenceDetector
{
    internal const string TRACK_CREATION_STACK_PROPERTY = "io.netty.util.LeakPresenceDetector.trackCreationStack";
    internal static readonly bool TRACK_CREATION_STACK = SystemPropertyUtil.GetBoolean(TRACK_CREATION_STACK_PROPERTY, false);
    internal static readonly ResourceScope GLOBAL = new("global");
    private static int staticInitializerCount;

    internal static bool InStaticInitializerSlow(StackTrace trace)
    {
        foreach (StackFrame frame in trace.GetFrames())
        {
            var method = frame.GetMethod();
            // CLR static constructors are .cctor, corresponding to Java <clinit>.
            // MethodBase.IsConstructor excludes static constructors on the CLR.
            if (method?.IsStatic == true && method.Name == ".cctor") return true;
        }
        return false;
    }
    internal static bool InStaticInitializerFast()
    {
        // This plain field access is safe. The worst that can happen is that we see non-zero where we shouldn't.
        return Volatile.Read(ref staticInitializerCount) != 0 && InStaticInitializerSlow(new StackTrace());
    }
    /**
     * Wrap a static initializer so that any resources created inside the block will not be tracked. Example:
     * <pre>{@code
     * private static final ByteBuf CRLF_BUF = LeakPresenceDetector.staticInitializer(() -> unreleasableBuffer(
     *             directBuffer(2).writeByte(CR).writeByte(LF)).asReadOnly());
     * }</pre>
     * <p>
     * Note that technically, this method does not actually care what happens inside the block. Instead, it turns on
     * stack trace introspection at the start of the block, and turns it back off at the end. Any allocation in that
     * interval will be checked to see whether it is part of a static initializer, and if it is, it will not be
     * tracked.
     *
     * @param supplier A code block to run
     * @return The value returned by the {@code supplier}
     * @param <R> The supplier return type
     */
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static R StaticInitializer<R>(Func<R> supplier)
    {
        if (!InStaticInitializerSlow(new StackTrace())) throw new InvalidOperationException("Not in static initializer.");
        Interlocked.Increment(ref staticInitializerCount);
        try { return supplier(); }
        finally { Interlocked.Decrement(ref staticInitializerCount); }
    }
    /**
     * Check the current leak presence detector scope for open resources. If any resources remain unclosed, an
     * exception is thrown.
     *
     * @throws IllegalStateException If there is a leak, or if the leak detector is not a {@link LeakPresenceDetector}.
     */
    public static void Check()
    {
        // for LeakPresenceDetector, this is cheap.
        ResourceLeakDetector<object> detector = ResourceLeakDetectorFactory.Instance().NewResourceLeakDetector<object>(typeof(object));
        if (detector is not LeakPresenceDetector<object> presence)
            throw new InvalidOperationException("LeakPresenceDetector not in use. Please register it using " +
                "environment property io.netty.customResourceLeakDetector=" + typeof(LeakPresenceDetector<>).AssemblyQualifiedName);
        //noinspection resource
        presence.ScopeForCheck().Check();
    }
    /**
     * A resource scope keeps track of the resources for a particular set of threads. Different scopes can be checked
     * for leaks separately, to enable parallel test execution.
     */
    public sealed class ResourceScope : IDisposable
    {
        internal readonly string name;
        private long openResourceCounter;
        private readonly ConcurrentDictionary<object, Exception> creationStacks = TRACK_CREATION_STACK ? new() : null;
        private int open = 1;
        /**
         * Create a new scope.
         *
         * @param name The scope name, used for error reporting
         */
        public ResourceScope(string name) => this.name = name;
        internal void CheckOpen()
        {
            if (Volatile.Read(ref open) == 0)
                throw new AllocationProhibitedException("Resource scope '" + (name ?? "null") + "' already closed");
        }
        internal void Track(object tracker)
        {
            CheckOpen();
            Interlocked.Increment(ref openResourceCounter);
            if (TRACK_CREATION_STACK) creationStacks.TryAdd(tracker, new LeakCreation());
        }
        internal void Release(object tracker)
        {
            Interlocked.Decrement(ref openResourceCounter);
            if (TRACK_CREATION_STACK) creationStacks.TryRemove(tracker, out _);
            CheckOpen();
        }
        internal void Check()
        {
            // Interlocked replaces LongAdder; checks still require quiescent producers.
            long count = Interlocked.Exchange(ref openResourceCounter, 0);
            if (count == 0) return;
            string message = "Possible memory leak detected for resource scope '" + (name ?? "null") + "'. ";
            if (count < 0)
                throw new InvalidOperationException(message + "Resource count was negative: A resource previously reported as a leak was released " +
                    "after all. Please ensure that that resource is released before its test finishes.");
            if (TRACK_CREATION_STACK)
            {
                var failure = new InvalidOperationException(message + "Creation stack traces:");
                int index = 0;
                foreach (Exception creation in creationStacks.Values)
                {
                    ThrowableUtil.AddSuppressed(failure, creation);
                    if (index++ > 5) break;
                }
                creationStacks.Clear();
                throw failure;
            }
            throw new InvalidOperationException(message + "Please use paranoid leak detection to get more information, or set " +
                "environment property " + TRACK_CREATION_STACK_PROPERTY + "=true");
        }
        /**
         * Check whether there are any open resources left, and {@link #close()} would throw.
         *
         * @return {@code true} if there are open resources
         */
        public bool HasOpenResources() => Volatile.Read(ref openResourceCounter) > 0;
        /**
         * Close this scope. Closing a scope will prevent new resources from being allocated (or released) in this
         * scope. The call also throws an exception if there are any resources left open.
         */
        public void Close()
        {
            // CLR IDisposable repeats must leave the scope closed. The pinned
            // Java decrement can become negative and accidentally allow reuse.
            if (Interlocked.Exchange(ref open, 0) != 0) Check();
        }
        public void Dispose() => Close();
    }
    private sealed class LeakCreation : Exception
    {
        private readonly Thread thread = Thread.CurrentThread;
        private readonly StackTrace trace = new(0, true);
        private string message;
        public override string StackTrace => trace.ToString();
        public override string Message
        {
            get
            {
                using var held = UninterruptibleMonitor.Enter(this);
                if (message == null)
                {
                    if (InStaticInitializerSlow(trace))
                        message = "Resource created in static initializer. Please wrap the static initializer in " +
                            "LeakPresenceDetector.staticInitializer so that this resource is excluded.";
                    else
                        message = "Resource created outside static initializer on thread '" +
                            (thread.Name ?? thread.ManagedThreadId.ToString()) + "' (" + thread.ThreadState + "), likely leak.";
                }
                return message;
            }
        }
    }
    /**
     * Special exception type to show that an allocation is prohibited at the moment, for example because the
     * {@link ResourceScope} is closed, or because the current thread cannot be associated with a particular scope.
     * <p>
     * Some code in Netty will treat this exception specially to avoid allocation loops.
     */
    public sealed class AllocationProhibitedException : InvalidOperationException
    {
        public AllocationProhibitedException(string message) : base(message) { }
    }
}

/**
 * Alternative leak detector implementation for reliable and performant detection in tests.
 *
 * <h3>Background</h3>
 * <p>
 * The standard {@link ResourceLeakDetector} produces no "false positives", but this comes with tradeoffs. You either
 * get many false negatives because only a small sample of buffers is instrumented, or you turn on paranoid detection
 * which carries a somewhat heavy performance cost with each allocation. Additionally, paranoid detection enables
 * detailed recording of buffer access operations with heavy performance impact. Avoiding false negatives is necessary
 * for (unit, fuzz...) testing if bugs should lead to reliable test failures, but the performance impact can be
 * prohibitive for some tests.
 *
 * <h3>The presence detector</h3>
 * <p>
 * The <i>leak presence detector</i> takes a different approach. It foregoes detailed tracking of allocation and
 * modification stack traces. In return every resource is counted, so there are no false negatives where a leak would
 * not be detected.
 * <p>
 * The presence detector also does not wait for an unclosed resource to be garbage collected before it's reported as
 * leaked. This ensures that leaks are detected promptly and can be directly associated with a particular test, but it
 * can lead to false positives. Tests that use the presence detector must shut down completely <i>before</i> checking
 * for resource leaks. There are also complications with static fields, described below.
 *
 * <h3>Resource Scopes</h3>
 * <p>
 * A resource scope manages all resources of a set of threads over time. On allocation, a resource is assigned to a
 * scope through the {@link #currentScope()} method. When {@link #check()} is called, or the scope is
 * {@link ResourceScope#close() closed}, all resources in that scope must have been released.
 * <p>
 * By default, there is only a single "global" scope, and when {@link #check()} is called, all resources in the entire
 * JVM must have been released. To enable parallel test execution, it may be necessary to use separate scopes for
 * separate tests instead, so that one test can check for its own leaks while another test is still in progress. You
 * can override {@link #currentScope()} to implement this for your test framework.
 *
 * <h3>Static Fields</h3>
 * <p>
 * While the presence detector requires that <i>all</i> resources be closed after a test, some resources kept in static
 * fields cannot be released, or there would be false positives. To avoid this, resources created inside static
 * initializers, specifically when the allocation stack trace contains a {@code <clinit>} method, <i>are not
 * tracked</i>.
 * <p>
 * Because the presence detector does not normally capture or introspect allocation stack traces, additional
 * cooperation is required. Any static initializer must be wrapped in a {@link #staticInitializer(Supplier)} call,
 * which will temporarily enable stack trace introspection. For example:
 * <pre>{@code
 * private static final ByteBuf CRLF_BUF = LeakPresenceDetector.staticInitializer(() -> unreleasableBuffer(
 *             directBuffer(2).writeByte(CR).writeByte(LF)).asReadOnly());
 * }</pre>
 * <p>
 * Since stack traces are not captured by default, it can be difficult to tell apart a real leak from a missed static
 * initializer. You can temporarily turn on allocation stack trace capture using the
 * {@code -Dio.netty.util.LeakPresenceDetector.trackCreationStack=true} system property.
 *
 * @param <T> The resource type to detect
 */
public class LeakPresenceDetector<T> : ResourceLeakDetector<T> where T : class
{
    /**
     * Create a new detector for the given resource type.
     *
     * @param resourceType The resource type
     */
    public LeakPresenceDetector(Type resourceType) : base(resourceType, 0) { }
    /**
     * This constructor should not be used directly, it is called reflectively by {@link ResourceLeakDetectorFactory}.
     *
     * @param resourceType The resource type
     * @param samplingInterval Ignored
     */
    [Obsolete]
    public LeakPresenceDetector(Type resourceType, int samplingInterval) : this(resourceType) { }
    /**
     * This constructor should not be used directly, it is called reflectively by {@link ResourceLeakDetectorFactory}.
     *
     * @param resourceType The resource type
     * @param samplingInterval Ignored
     * @param maxActive Ignored
     */
    public LeakPresenceDetector(Type resourceType, int samplingInterval, long maxActive) : this(resourceType) { }
    /**
     * Get the resource scope for the current thread. This is used to assign resources to scopes, and it is used by
     * {@link #check()} to tell which scope to check for open resources. By default, the global scope is returned.
     *
     * @return The resource scope to use
     */
    protected virtual LeakPresenceDetector.ResourceScope CurrentScope() => LeakPresenceDetector.GLOBAL;
    internal LeakPresenceDetector.ResourceScope ScopeForCheck() => CurrentScope();
    public sealed override IResourceLeakTracker<T> Track(T obj)
        => LeakPresenceDetector.InStaticInitializerFast() ? null : TrackForcibly(obj);
    public sealed override IResourceLeakTracker<T> TrackForcibly(T obj) => new PresenceTracker(CurrentScope());
    public sealed override bool IsRecordEnabled() => false;
    private sealed class PresenceTracker : IResourceLeakTracker<T>
    {
        private readonly LeakPresenceDetector.ResourceScope scope;
        private int closed;
        internal PresenceTracker(LeakPresenceDetector.ResourceScope scope)
        {
            this.scope = scope;
            scope.Track(this);
        }
        public void Record() { }
        public void Record(object hint) { }
        public bool Close(T trackedObject)
        {
            if (Interlocked.CompareExchange(ref closed, 1, 0) != 0) return false;
            scope.Release(this);
            return true;
        }
    }
}
