using System;
using System.Threading;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Internal;

public class AutomaticCleanerReference : WeakReference<object>
{
    private readonly IRunnable _cleanupTask;

    public AutomaticCleanerReference(object referent, IRunnable cleanupTask)
        : base(referent, REFERENCE_QUEUE)
    {
        _cleanupTask = cleanupTask;
    }

    public void cleanup()
    {
        _cleanupTask.run();
    }

    public Thread get()
    {
        return null;
    }

    public void clear()
    {
        LIVE_SET.remove(this);
        base.clear();
    }
}