/*
 * Copyright 2013 The Netty Project
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Concurrent;

public class DefaultPromise<V> : AbstractFuture<V>, IPromise<V>
{
    /**
     * System property with integer type value, that determine the max reentrancy/recursion level for when
     * listener notifications prompt other listeners to be notified.
     * <p>
     * When the reentrancy/recursion level becomes greater than this number, a new task will instead be scheduled
     * on the event loop, to finish notifying any subsequent listners.
     * <p>
     * The default value is {@code 8}.
     */
    public const string PROPERTY_MAX_LISTENER_STACK_DEPTH = "io.netty.defaultPromise.maxListenerStackDepth";
    private static readonly int MAX_LISTENER_STACK_DEPTH = PromiseListenerNotificationSettings.MaxStackDepth;
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(DefaultPromise<V>));
    private static readonly IInternalLogger rejectedExecutionLogger =
        InternalLoggerFactory.getInstance(typeof(DefaultPromise<V>).FullName + ".rejectedExecution");
    private static readonly object SUCCESS = new object();
    private static readonly object UNCANCELLABLE = new object();
    private static readonly CauseHolder CANCELLATION_CAUSE_HOLDER = new CauseHolder(new StacklessCancellationException());
    private object _result;
    private readonly IEventExecutor _executor;
    // CLR adaptation: only this class owns completion; the public Task is a read-only asynchronous bridge.
    private readonly TaskCompletionSource<V> _completion = new TaskCompletionSource<V>(TaskCreationOptions.RunContinuationsAsynchronously);
    public override Task<V> Task => _completion.Task;

    /**
     * One or more listeners. Can be a {@link GenericFutureListener} or a {@link DefaultFutureListeners}.
     * If {@code null}, it means either 1) no listeners were added yet or 2) all listeners were notified.
     * <p>
     * Threading - synchronized(this). We must support adding listeners when there is no EventExecutor.
     */
    private IGenericFutureListener<IFuture<V>> _listener;
    private DefaultFutureListeners<IFuture<V>> _listeners;
    /**
     * Threading - synchronized(this). We are required to hold the monitor to use Java's underlying wait()/notifyAll().
     */
    private short _waiters;
    /**
     * Threading - synchronized(this). We must prevent concurrent notification and FIFO listener notification if the
     * executor changes.
     */
    private bool _notifyingListeners;

    /**
     * Creates a new instance.
     * <p>
     * It is preferable to use {@link EventExecutor#newPromise()} to create a new promise
     *
     * @param executor
     *        the {@link EventExecutor} which is used to notify the promise once it is complete.
     *        It is assumed this executor will protect against {@link StackOverflowError} exceptions.
     *        The executor may be used to avoid {@link StackOverflowError} by executing a {@link Runnable} if the stack
     *        depth exceeds a threshold.
     *
     */
    public DefaultPromise(IEventExecutor executor)
    {
        _executor = ObjectUtil.checkNotNull(executor, "executor");
    }
    /**
     * See {@link #executor()} for expectations of the executor.
     */
    protected DefaultPromise()
    {
        // only for subclasses
        _executor = null;
    }

    public virtual IPromise<V> setSuccess(V result)
    {
        if (setSuccess0(result)) return this;
        throw new InvalidOperationException("complete already: " + this);
    }
    public virtual bool trySuccess(V result) => setSuccess0(result);
    public virtual IPromise<V> setFailure(Exception cause)
    {
        if (setFailure0(cause)) return this;
        throw new InvalidOperationException("complete already: " + this, cause);
    }
    public virtual bool tryFailure(Exception cause) => setFailure0(cause);
    public virtual bool setUncancellable()
    {
        if (Interlocked.CompareExchange(ref _result, UNCANCELLABLE, null) == null) return true;
        object result = Volatile.Read(ref _result);
        return !isDone0(result) || !isCancelled0(result);
    }
    public override bool isSuccess()
    {
        object result = Volatile.Read(ref _result);
        return result != null && result != UNCANCELLABLE && result is not CauseHolder;
    }
    public override bool isCancellable() => Volatile.Read(ref _result) == null;

    private sealed class LeanCancellationException : TaskCanceledException
    {
        // Suppress a warning since the method doesn't need synchronization
        // CLR exceptions acquire their stack when thrown. No Java fillInStackTrace() call is necessary.
        public override string StackTrace => "at DefaultPromise.cancel(...)";
    }
    public override Exception cause() => cause0(Volatile.Read(ref _result));
    private Exception cause0(object result)
    {
        if (result is not CauseHolder) return null;
        if (ReferenceEquals(result, CANCELLATION_CAUSE_HOLDER))
        {
            Exception error = new LeanCancellationException();
            if (ReferenceEquals(Interlocked.CompareExchange(ref _result, new CauseHolder(error), result), result)) return error;
            result = Volatile.Read(ref _result);
        }
        return ((CauseHolder)result).cause;
    }
    public override IPromise<V> addListener(IGenericFutureListener<IFuture<V>> listener)
    {
        ObjectUtil.checkNotNull(listener, "listener");
        using (UninterruptibleMonitor.enter(this)) addListener0(listener);
        if (isDone()) notifyListeners();
        return this;
    }
    public override IPromise<V> addListeners(params IGenericFutureListener<IFuture<V>>[] listeners)
    {
        ObjectUtil.checkNotNull(listeners, "listeners");
        using (UninterruptibleMonitor.enter(this))
            foreach (var listener in listeners)
            {
                if (listener == null) break;
                addListener0(listener);
            }
        if (isDone()) notifyListeners();
        return this;
    }
    public override IPromise<V> removeListener(IGenericFutureListener<IFuture<V>> listener)
    {
        ObjectUtil.checkNotNull(listener, "listener");
        using (UninterruptibleMonitor.enter(this)) removeListener0(listener);
        return this;
    }
    public override IPromise<V> removeListeners(params IGenericFutureListener<IFuture<V>>[] listeners)
    {
        ObjectUtil.checkNotNull(listeners, "listeners");
        using (UninterruptibleMonitor.enter(this))
            foreach (var listener in listeners)
            {
                if (listener == null) break;
                removeListener0(listener);
            }
        return this;
    }
    public override IPromise<V> await()
    {
        if (isDone()) return this;
        // CLR adaptation: Sleep(0) observes and clears an already pending interrupt without a flag API.
        Thread.Sleep(0);
        checkDeadLock();
        using (UninterruptibleMonitor.enter(this))
            while (!isDone())
            {
                incWaiters();
                try { Monitor.Wait(this); }
                finally { --_waiters; }
            }
        return this;
    }
    public override IPromise<V> awaitUninterruptibly()
    {
        if (isDone()) return this;
        checkDeadLock();
        bool interrupted = false;
        try
        {
            using (UninterruptibleMonitor.enter(this))
                while (!isDone())
                {
                    incWaiters();
                    try { Monitor.Wait(this); }
                    catch (ThreadInterruptedException)
                    {
                        // Interrupted while waiting.
                        interrupted = true;
                    }
                    finally { --_waiters; }
                }
        }
        finally { if (interrupted) Thread.CurrentThread.Interrupt(); }
        return this;
    }
    public override bool await(TimeSpan timeout) => await0(toNanos(timeout.Ticks, 100), true);
    public override bool await(long timeoutMillis) => await0(toNanos(timeoutMillis, 1000000), true);
    public override bool awaitUninterruptibly(TimeSpan timeout)
    {
        try { return await0(toNanos(timeout.Ticks, 100), false); }
        catch (ThreadInterruptedException error)
        {
            // Should not be raised at all.
            throw new InvalidOperationException("unexpected interruption", error);
        }
    }
    public override bool awaitUninterruptibly(long timeoutMillis)
    {
        try { return await0(toNanos(timeoutMillis, 1000000), false); }
        catch (ThreadInterruptedException error)
        {
            // Should not be raised at all.
            throw new InvalidOperationException("unexpected interruption", error);
        }
    }
    public override V getNow()
    {
        object result = Volatile.Read(ref _result);
        // CLR adaptation: default(V) represents Java null for value types.
        return result == null || result == SUCCESS || result == UNCANCELLABLE || result is CauseHolder ? default : (V)result;
    }
    /**
     * {@inheritDoc}
     *
     * @param mayInterruptIfRunning this value has no effect in this implementation.
     */
    public override bool cancel(bool mayInterruptIfRunning)
    {
        if (Interlocked.CompareExchange(ref _result, CANCELLATION_CAUSE_HOLDER, null) != null) return false;
        _completion.TrySetCanceled();
        if (checkNotifyWaiters()) notifyListeners();
        return true;
    }
    public override bool isCancelled() => isCancelled0(Volatile.Read(ref _result));
    public override bool isDone() => isDone0(Volatile.Read(ref _result));
    public override IPromise<V> sync() { this.await(); rethrowIfFailed(); return this; }
    public override IPromise<V> syncUninterruptibly() { awaitUninterruptibly(); rethrowIfFailed(); return this; }
    public override string ToString() => toStringBuilder().ToString();
    protected virtual StringBuilder toStringBuilder()
    {
        var buf = new StringBuilder(64).Append(StringUtil.simpleClassName(this)).Append('@')
            .Append(RuntimeHelpers.GetHashCode(this).ToString("x"));
        object result = Volatile.Read(ref _result);
        if (result == SUCCESS) buf.Append("(success)");
        else if (result == UNCANCELLABLE) buf.Append("(uncancellable)");
        else if (result is CauseHolder holder) buf.Append("(failure: ").Append(holder.cause).Append(')');
        else if (result != null) buf.Append("(success: ").Append(result).Append(')');
        else buf.Append("(incomplete)");
        return buf;
    }
    /**
     * Get the executor used to notify listeners when this promise is complete.
     * <p>
     * It is assumed this executor will protect against {@link StackOverflowError} exceptions.
     * The executor may be used to avoid {@link StackOverflowError} by executing a {@link Runnable} if the stack
     * depth exceeds a threshold.
     * @return The executor used to notify listeners when this promise is complete.
     */
    protected virtual IEventExecutor executor() => _executor;
    protected virtual void checkDeadLock()
    {
        IEventExecutor e = executor();
        if (e != null && e.inEventLoop()) throw new BlockingOperationException(ToString());
    }
    /**
     * Notify a listener that a future has completed.
     * <p>
     * This method has a fixed depth of {@link #MAX_LISTENER_STACK_DEPTH} that will limit recursion to prevent
     * {@link StackOverflowError} and will stop notifying listeners added after this threshold is exceeded.
     * @param eventExecutor the executor to use to notify the listener {@code listener}.
     * @param future the future that is complete.
     * @param listener the listener to notify.
     */
    protected internal static void notifyListener(IEventExecutor eventExecutor, IFuture<V> future, IGenericFutureListener<IFuture<V>> listener)
    {
        notifyListenerWithStackOverFlowProtection(ObjectUtil.checkNotNull(eventExecutor, "eventExecutor"),
            ObjectUtil.checkNotNull(future, "future"), ObjectUtil.checkNotNull(listener, "listener"));
    }
    private void notifyListeners()
    {
        IEventExecutor e = executor();
        if (e.inEventLoop())
        {
            InternalThreadLocalMap locals = InternalThreadLocalMap.get();
            int depth = locals.futureListenerStackDepth();
            if (depth < MAX_LISTENER_STACK_DEPTH)
            {
                locals.setFutureListenerStackDepth(depth + 1);
                try { notifyListenersNow(); }
                finally { locals.setFutureListenerStackDepth(depth); }
                return;
            }
        }
        safeExecute(e, Runnables.Create(notifyListenersNow));
    }
    /**
     * The logic in this method should be identical to {@link #notifyListeners()} but
     * cannot share code because the listener(s) cannot be cached for an instance of {@link DefaultPromise} since the
     * listener(s) may be changed and is protected by a synchronized operation.
     */
    private static void notifyListenerWithStackOverFlowProtection(IEventExecutor e, IFuture<V> future, IGenericFutureListener<IFuture<V>> listener)
    {
        if (e.inEventLoop())
        {
            InternalThreadLocalMap locals = InternalThreadLocalMap.get();
            int depth = locals.futureListenerStackDepth();
            if (depth < MAX_LISTENER_STACK_DEPTH)
            {
                locals.setFutureListenerStackDepth(depth + 1);
                try { notifyListener0(future, listener); }
                finally { locals.setFutureListenerStackDepth(depth); }
                return;
            }
        }
        safeExecute(e, Runnables.Create(() => notifyListener0(future, listener)));
    }
    private void notifyListenersNow()
    {
        IGenericFutureListener<IFuture<V>> listener;
        DefaultFutureListeners<IFuture<V>> listeners;
        using (UninterruptibleMonitor.enter(this))
        {
            listener = _listener;
            listeners = _listeners;
            // Only proceed if there are listeners to notify and we are not already notifying listeners.
            if (_notifyingListeners || (listener == null && listeners == null)) return;
            _notifyingListeners = true;
            if (listener != null) _listener = null;
            else _listeners = null;
        }
        for (;;)
        {
            if (listener != null) notifyListener0(this, listener);
            else
            {
                var array = listeners.listeners();
                int size = listeners.size();
                for (int i = 0; i < size; i++) notifyListener0(this, array[i]);
            }
            using (UninterruptibleMonitor.enter(this))
            {
                if (_listener == null && _listeners == null)
                {
                    // Nothing can throw from within this method, so setting notifyingListeners back to false does not
                    // need to be in a finally block.
                    _notifyingListeners = false;
                    return;
                }
                listener = _listener;
                listeners = _listeners;
                if (listener != null) _listener = null;
                else _listeners = null;
            }
        }
    }
    private static void notifyListener0(IFuture<V> future, IGenericFutureListener<IFuture<V>> listener)
    {
        try { listener.operationComplete(future); }
        catch (Exception error) { logger.warn("An exception was thrown by " + listener.GetType().FullName + ".operationComplete()", error); }
    }
    private void addListener0(IGenericFutureListener<IFuture<V>> listener)
    {
        if (_listener == null)
        {
            if (_listeners == null) _listener = listener;
            else _listeners.add(listener);
        }
        else { _listeners = new DefaultFutureListeners<IFuture<V>>(_listener, listener); _listener = null; }
    }
    private void removeListener0(IGenericFutureListener<IFuture<V>> listener)
    {
        if (ReferenceEquals(_listener, listener)) _listener = null;
        else if (_listeners != null)
        {
            _listeners.remove(listener);
            // Removal is rare, no need for compaction
            if (_listeners.size() == 0) _listeners = null;
        }
    }
    private bool setSuccess0(V result) => setValue0((object)result ?? SUCCESS);
    private bool setFailure0(Exception cause) => setValue0(new CauseHolder(ObjectUtil.checkNotNull(cause, "cause")));
    private bool setValue0(object result)
    {
        if (Interlocked.CompareExchange(ref _result, result, null) != null &&
            !ReferenceEquals(Interlocked.CompareExchange(ref _result, result, UNCANCELLABLE), UNCANCELLABLE)) return false;
        if (result is CauseHolder holder)
        {
            if (holder.cause is OperationCanceledException) _completion.TrySetCanceled();
            else _completion.TrySetException(holder.cause);
        }
        else _completion.TrySetResult(ReferenceEquals(result, SUCCESS) ? default : (V)result);
        if (checkNotifyWaiters()) notifyListeners();
        return true;
    }
    /**
     * Check if there are any waiters and if so notify these.
     * @return {@code true} if there are any listeners attached to the promise, {@code false} otherwise.
     */
    private bool checkNotifyWaiters()
    {
        using (UninterruptibleMonitor.enter(this))
        {
            if (_waiters > 0) Monitor.PulseAll(this);
            return _listener != null || _listeners != null;
        }
    }
    private void incWaiters()
    {
        if (_waiters == short.MaxValue) throw new InvalidOperationException("too many waiters: " + this);
        ++_waiters;
    }
    private void rethrowIfFailed()
    {
        Exception error = cause();
        if (error == null) return;
        // CLR adaptation: the diagnostic is associated without replacing the original failure object.
        if (error is not OperationCanceledException && ThrowableUtil.getSuppressed(error).Length == 0)
            ThrowableUtil.addSuppressed(error, ExceptionDispatchInfo.SetCurrentStackTrace(new Exception("Rethrowing promise failure cause")));
        ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static long toNanos(long value, int factor)
    {
        if (value > long.MaxValue / factor) return long.MaxValue;
        if (value < long.MinValue / factor) return long.MinValue;
        return value * factor;
    }
    private bool await0(long timeoutNanos, bool interruptable)
    {
        if (isDone()) return true;
        if (timeoutNanos <= 0) return isDone();
        if (interruptable) Thread.Sleep(0);
        checkDeadLock();
        // Start counting time from here instead of the first line of this method,
        // to avoid/postpone performance cost of System.nanoTime().
        long startTime = Stopwatch.GetTimestamp();
        using (UninterruptibleMonitor.enter(this))
        {
            bool interrupted = false;
            try
            {
                long waitTime = timeoutNanos;
                while (!isDone() && waitTime > 0)
                {
                    incWaiters();
                    try
                    {
                        // CLR Monitor.Wait has millisecond resolution; round up and use bounded chunks.
                        int millis = (int)Math.Min(int.MaxValue, 1 + (waitTime - 1) / 1000000);
                        Monitor.Wait(this, millis);
                    }
                    catch (ThreadInterruptedException)
                    {
                        if (interruptable) throw;
                        interrupted = true;
                    }
                    finally { --_waiters; }
                    // Check isDone() in advance, try to avoid calculating the elapsed time later.
                    if (isDone()) return true;
                    // Calculate the elapsed time here instead of in the while condition,
                    // try to avoid performance cost of System.nanoTime() in the first loop of while.
                    long elapsedTicks = Stopwatch.GetElapsedTime(startTime).Ticks;
                    waitTime = timeoutNanos - toNanos(elapsedTicks, 100);
                }
                return isDone();
            }
            finally { if (interrupted) Thread.CurrentThread.Interrupt(); }
        }
    }
    /**
     * Notify all progressive listeners.
     * <p>
     * No attempt is made to ensure notification order if multiple calls are made to this method before
     * the original invocation completes.
     * <p>
     * This will do an iteration over all listeners to get all of type {@link GenericProgressiveFutureListener}s.
     * @param progress the new progress.
     * @param total the total progress.
     */
    internal void notifyProgressiveListeners(long progress, long total)
    {
        object listeners = progressiveListeners();
        if (listeners == null) return;
        void notify()
        {
            if (listeners is IGenericProgressiveFutureListener<IFuture<V>> single) notifyProgressiveListener0(single, progress, total);
            else foreach (var listener in (IGenericProgressiveFutureListener<IFuture<V>>[])listeners) notifyProgressiveListener0(listener, progress, total);
        }
        IEventExecutor e = executor();
        if (e.inEventLoop()) notify();
        else safeExecute(e, Runnables.Create(notify));
    }
    /**
     * Returns a {@link GenericProgressiveFutureListener}, an array of {@link GenericProgressiveFutureListener}, or
     * {@code null}.
     */
    private object progressiveListeners()
    {
        using (UninterruptibleMonitor.enter(this))
        {
            if (_listener == null && _listeners == null)
            {
                // No listeners added
                return null;
            }
            if (_listeners != null)
            {
                // Copy DefaultFutureListeners into an array of listeners.
                int count = _listeners.progressiveSize();
                if (count == 0) return null;
                var copy = new IGenericProgressiveFutureListener<IFuture<V>>[count];
                int j = 0;
                foreach (var listener in _listeners.listeners())
                    if (listener is IGenericProgressiveFutureListener<IFuture<V>> progressive) copy[j++] = progressive;
                return count == 1 ? copy[0] : copy;
            }
            if (_listener is IGenericProgressiveFutureListener<IFuture<V>> single) return single;
            // Only one listener was added and it's not a progressive listener.
            return null;
        }
    }
    private void notifyProgressiveListener0(IGenericProgressiveFutureListener<IFuture<V>> listener, long progress, long total)
    {
        try { listener.operationProgressed(this, progress, total); }
        catch (Exception error) { logger.warn("An exception was thrown by " + listener.GetType().FullName + ".operationProgressed()", error); }
    }
    private static bool isCancelled0(object result) => result is CauseHolder holder && holder.cause is OperationCanceledException;
    private static bool isDone0(object result) => result != null && result != UNCANCELLABLE;
    private sealed class CauseHolder
    {
        internal readonly Exception cause;
        internal CauseHolder(Exception cause) { this.cause = cause; }
    }
    private static void safeExecute(IEventExecutor executor, IRunnable task)
    {
        try { executor.execute(task); }
        catch (Exception error) { rejectedExecutionLogger.error("Failed to submit a listener notification task. Event loop shut down?", error); }
    }
    private sealed class StacklessCancellationException : TaskCanceledException
    {
        // Override fillInStackTrace() so we not populate the backtrace via a native call and so leak the
        // Classloader.
        // CLR adaptation: a synthetic frame replaces Java ThrowableUtil.unknownStackTrace().
        public override string StackTrace => "at DefaultPromise.cancel(...)";
    }

    // CLR adapters retain covariant fluent returns for typed listener registrations.
    public override IPromise<V> addListener<F>(IGenericFutureListener<F> listener) { base.addListener(listener); return this; }
    public override IPromise<V> addListeners<F>(params IGenericFutureListener<F>[] listeners) { base.addListeners(listeners); return this; }
    public override IPromise<V> removeListener<F>(IGenericFutureListener<F> listener) { base.removeListener(listener); return this; }
    public override IPromise<V> removeListeners<F>(params IGenericFutureListener<F>[] listeners) { base.removeListeners(listeners); return this; }
}

// CLR generic static fields are per closed type. Read this process-wide setting once, as Java does.
internal static class PromiseListenerNotificationSettings
{
    internal static readonly int MaxStackDepth = Math.Min(8,
        SystemPropertyUtil.getInt("io.netty.defaultPromise.maxListenerStackDepth", 8));
}
