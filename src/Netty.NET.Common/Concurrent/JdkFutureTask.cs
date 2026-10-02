using System;
using System.Threading;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

// CLR adapter for the JDK FutureTask used by inherited bulk invocation. Unlike
// Netty PromiseTask, it stays cancellable while user code runs and allows blocking
// on an executor thread. The cancellation phase prevents an interrupt from reaching
// a later task after the runner returns to its pool.
internal class JdkFutureTask<T> : PromiseTask<T>
{
    private readonly object runnerGate = new();
    private Thread runner;
    private int cancellationPhases;

    internal JdkFutureTask(IEventExecutor executor, IRunnable runnable, T result)
        : base(executor, runnable, result) { }
    internal JdkFutureTask(IEventExecutor executor, ICallable<T> callable)
        : base(executor, callable) { }

    protected override void checkDeadLock() { }

    public override void run()
    {
        if (!acquireRunner()) return;
        try
        {
            if (!isDone())
            {
                try { trySuccessInternal(runTask()); }
                catch (Exception cause) { tryFailureInternal(cause); }
            }
        }
        finally { releaseRunner(); }
    }

    protected bool runAndReset()
    {
        if (!acquireRunner()) return false;
        bool ran = false;
        try
        {
            if (!isDone())
            {
                try { runTask(); ran = true; }
                catch (Exception cause) { tryFailureInternal(cause); }
            }
        }
        finally { releaseRunner(); }
        return ran && !isDone();
    }

    private bool acquireRunner()
    {
        using (UninterruptibleMonitor.enter(runnerGate))
        {
            if (isDone() || runner != null) return false;
            runner = Thread.CurrentThread;
            return true;
        }
    }

    private void releaseRunner()
    {
        bool interrupted = false;
        using (UninterruptibleMonitor.enter(runnerGate))
        {
            while (cancellationPhases != 0)
            {
                try { Monitor.Wait(runnerGate); }
                catch (ThreadInterruptedException) { interrupted = true; }
            }
            runner = null;
        }
        if (interrupted) Thread.CurrentThread.Interrupt();
    }

    public override bool cancel(bool mayInterruptIfRunning)
    {
        if (!mayInterruptIfRunning) return base.cancel(false);
        using (UninterruptibleMonitor.enter(runnerGate))
        {
            if (isDone()) return false;
            ++cancellationPhases;
        }
        try
        {
            bool cancelled = base.cancel(true);
            if (cancelled)
            {
                using (UninterruptibleMonitor.enter(runnerGate)) runner?.Interrupt();
            }
            return cancelled;
        }
        finally
        {
            using (UninterruptibleMonitor.enter(runnerGate))
            {
                --cancellationPhases;
                Monitor.PulseAll(runnerGate);
            }
        }
    }
}
