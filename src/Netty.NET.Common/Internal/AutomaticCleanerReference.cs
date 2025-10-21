using System;
using System.Threading;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Internal;

public class AutomaticCleanerReference : WeakReferenceQueueEntry<object>
{
    private readonly ConcurrentHashSet<AutomaticCleanerReference> _liveSet;
    private readonly IRunnable _cleanupTask;

    public AutomaticCleanerReference(ConcurrentHashSet<AutomaticCleanerReference> a, object referent, WeakReferenceQueue<object> referenceQueue, IRunnable cleanupTask)
        : base(referent, referenceQueue)
    {
        _liveSet = a;
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

    public override void clear()
    {
        _liveSet.Remove(this);
        base.clear();
    }
}