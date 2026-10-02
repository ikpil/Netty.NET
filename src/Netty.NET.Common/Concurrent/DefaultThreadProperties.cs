using System;
using System.Threading;

namespace Netty.NET.Common.Concurrent;

public class DefaultThreadProperties : IThreadProperties
{
    private readonly Thread _t;
    private volatile ThreadPriority lastPriority;
    private volatile bool lastDaemon;

    public DefaultThreadProperties(Thread t)
    {
        _t = t;
        lastPriority = t.Priority;
        lastDaemon = t.IsBackground;
    }

    public ThreadState state()
    {
        return _t.ThreadState;
    }

    public ThreadPriority priority()
    {
        // CLR discards native priority after termination. Retain the last observed
        // value for postmortem queries; live queries still read the current value.
        try { lastPriority = _t.Priority; }
        catch (ThreadStateException) { }
        return lastPriority;
    }

    public bool isInterrupted()
    {
        throw new NotSupportedException("The CLR does not expose a non-destructive pending interrupt flag.");
    }

    public bool isDaemon()
    {
        try { lastDaemon = _t.IsBackground; }
        catch (ThreadStateException) { }
        return lastDaemon;
    }

    public string name()
    {
        return _t.Name;
    }

    public long id()
    {
        return _t.ManagedThreadId;
    }

    public System.Diagnostics.StackFrame[] stackTrace()
    {
        // CLR has no supported remote managed-thread stack API. Never substitute
        // the caller's stack for the requested thread; its owner can query itself.
        if (_t != Thread.CurrentThread)
            throw new NotSupportedException("Capturing another managed thread's stack is not supported by the CLR.");
        return new System.Diagnostics.StackTrace(1, true).GetFrames();
    }

    public bool isAlive()
    {
        return _t.IsAlive;
    }
}
