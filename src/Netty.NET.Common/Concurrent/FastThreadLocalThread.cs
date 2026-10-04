/*
* Copyright 2014 The Netty Project
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
using System.Runtime.CompilerServices;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Concurrent;

/**
 * A special {@link Thread} that provides fast access to {@link FastThreadLocal} variables.
 */
// CLR adaptation: System.Threading.Thread is sealed. This class owns a native
// thread and marks its execution with thread-static state; factories return Thread.
public class FastThreadLocalThread
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(FastThreadLocalThread));
    private static readonly ConditionalWeakTable<Thread, FastThreadLocalThread> OwnedThreads = new();
    [ThreadStatic] private static FastThreadLocalThread _current;
    /**
     * Set of thread IDs that are treated like {@link FastThreadLocalThread}.
     */
    /**
     * Immutable, thread-safe helper class that wraps {@link LongLongHashMap}
     */
    // CLR adaptation: the upstream fallback set is replaced by per-thread scope
    // membership. Queries only inspect the caller, so no shared ID map is needed.
    [ThreadStatic] private static bool _fallbackScope;

    // This will be set to true if we have a chance to wrap the Runnable.
    private readonly bool cleanupFastThreadLocals;
    private InternalThreadLocalMap _threadLocalMap;
    private Action _target;
    public Thread Thread { get; }
    internal static FastThreadLocalThread CurrentFastThreadLocalThread() => _current;

    public FastThreadLocalThread() : this(null, null, 0, false) { }
    public FastThreadLocalThread(string name) : this(null, name, 0, false) { }
    public FastThreadLocalThread(Action target) : this(target, null, 0, true) { }
    public FastThreadLocalThread(Action target, string name) : this(target, name, 0, true) { }
    public FastThreadLocalThread(ThreadGroup group, Action target) : this(target, null, 0, true, group) { }
    public FastThreadLocalThread(ThreadGroup group, string name) : this(null, name, 0, false, group) { }
    public FastThreadLocalThread(ThreadGroup group, Action target, string name) : this(target, name, 0, true, group) { }
    public FastThreadLocalThread(ThreadGroup group, Action target, string name, long stackSize)
        : this(target, name, checked((int)stackSize), true, group) { }

    // CLR ThreadGroup identity uses weak metadata and inherits the creator group.
    // Stack size is a CLR hint and must fit its Int32 parameter.
    private FastThreadLocalThread(Action target, string name, int stackSize, bool cleanup, ThreadGroup group = null)
    {
        cleanupFastThreadLocals = cleanup;
        _target = cleanup ? FastThreadLocalRunnable.Wrap(target) : target;
        Thread = stackSize == 0 ? new Thread(Entry) : new Thread(Entry, stackSize);
        ThreadGroup.Assign(Thread, group);
        if (name != null) Thread.Name = name;
        OwnedThreads.Add(Thread, this);
    }

    private void Entry()
    {
        _current = this;
        try { Run(); }
        finally
        {
            // Native Thread ownership metadata may outlive execution. Release captured work on termination.
            _target = null;
            _current = null;
        }
    }

    public virtual void Run() => _target?.Invoke();

    /**
     * Returns the internal data structure that keeps the thread-local variables bound to this thread.
     * Note that this method is for internal use only, and thus is subject to change at any time.
     */
    public InternalThreadLocalMap ThreadLocalMap()
    {
        if (_current != this && logger.IsWarnEnabled())
            logger.Warn(new InvalidOperationException("It's not thread-safe to get 'threadLocalMap' which doesn't belong to the caller thread"));
        return _threadLocalMap;
    }

    /**
     * Sets the internal data structure that keeps the thread-local variables bound to this thread.
     * Note that this method is for internal use only, and thus is subject to change at any time.
     */
    public void SetThreadLocalMap(InternalThreadLocalMap threadLocalMap)
    {
        if (_current != this && logger.IsWarnEnabled())
            logger.Warn(new InvalidOperationException("It's not thread-safe to set 'threadLocalMap' which doesn't belong to the caller thread"));
        _threadLocalMap = threadLocalMap;
    }

    /**
     * Returns {@code true} if {@link FastThreadLocal#removeAll()} will be called once {@link #run()} completes.
     *
     * @deprecated Use {@link FastThreadLocalThread#currentThreadWillCleanupFastThreadLocals()} instead
     */
    public bool WillCleanupFastThreadLocals() => cleanupFastThreadLocals;

    /**
     * Returns {@code true} if {@link FastThreadLocal#removeAll()} will be called once {@link Thread#run()} completes.
     *
     * @deprecated Use {@link FastThreadLocalThread#currentThreadWillCleanupFastThreadLocals()} instead
     */
    public static bool WillCleanupFastThreadLocals(Thread thread)
    {
        return thread != null && OwnedThreads.TryGetValue(thread, out var owner) && owner.cleanupFastThreadLocals;
    }

    /**
     * Returns {@code true} if {@link FastThreadLocal#removeAll()} will be called once {@link Thread#run()} completes.
     */
    public static bool CurrentThreadWillCleanupFastThreadLocals()
    {
        // intentionally doesn't accept a thread parameter to work with ScopedValue in the future
        return _current?.cleanupFastThreadLocals == true || _fallbackScope;
    }

    /**
     * Returns {@code true} if this thread supports {@link FastThreadLocal}.
     */
    public static bool CurrentThreadHasFastThreadLocal()
    {
        // intentionally doesn't accept a thread parameter to work with ScopedValue in the future
        return _current != null || _fallbackScope;
    }

    /**
     * Run the given task with {@link FastThreadLocal} support. This call should wrap the runnable for any thread that
     * is long-running enough to make treating it as a {@link FastThreadLocalThread} reasonable, but that can't
     * actually extend this class (e.g. because it's a virtual thread). Netty will use optimizations for recyclers and
     * allocators as if this was a {@link FastThreadLocalThread}.
     * <p>This method will clean up any {@link FastThreadLocal}s at the end, and
     * {@link #currentThreadWillCleanupFastThreadLocals()} will return {@code true}.
     * <p>At the moment, {@link FastThreadLocal} uses normal {@link ThreadLocal} as the backing storage here, but in
     * the future this may be replaced with scoped values, if semantics can be preserved and performance is good.
     *
     * @param runnable The task to run
     */
    public static void RunWithFastThreadLocal(Action runnable)
    {
        ArgumentNullException.ThrowIfNull(runnable);
        if (_current != null) throw new InvalidOperationException("Caller is a real FastThreadLocalThread");
        if (_fallbackScope) throw new InvalidOperationException("Reentrant call to run()");
        _fallbackScope = true;
        try { runnable(); }
        finally
        {
            _fallbackScope = false;
            FastThreadLocal.RemoveAll();
        }
    }

    /**
     * Query whether this thread is allowed to perform blocking calls or not.
     * {@link FastThreadLocalThread}s are often used in event-loops, where blocking calls are forbidden in order to
     * prevent event-loop stalls, so this method returns {@code false} by default.
     * <p>
     * Subclasses of {@link FastThreadLocalThread} can override this method if they are not meant to be used for
     * running event-loops.
     *
     * @return {@code false}, unless overridden by a subclass.
     */
    public virtual bool PermitBlockingCalls() => false;
}
