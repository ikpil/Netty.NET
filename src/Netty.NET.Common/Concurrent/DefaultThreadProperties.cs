using System;
using System.Threading;

namespace Netty.NET.Common.Concurrent;

public class DefaultThreadProperties : IThreadProperties
{
    private readonly Thread _t;

    public DefaultThreadProperties(Thread t)
    {
        _t = t;
    }

    public ThreadState state()
    {
        return _t.ThreadState;
    }

    public ThreadPriority priority()
    {
        return _t.Priority;
    }

    public bool isInterrupted()
    {
        throw new NotSupportedException();
        //return _t.IsInterrupted();
    }

    public bool isDaemon()
    {
        return _t.IsBackground;
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
        throw new NotSupportedException();
    }

    public bool isAlive()
    {
        return _t.IsAlive;
    }
}