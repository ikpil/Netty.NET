using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.ResourceLeakDetector;

namespace Netty.NET.Common;

internal sealed class DefaultResourceLeak<T> : IResourceLeakTracker<T>, IResourceLeak where T : class
{
    // generics and updaters do not mix.
    // generics and updaters do not mix.
    // CLR fields use native atomic operations; Java updater casts are unnecessary.
    private TraceRecord head;
    private int droppedRecords;
    private readonly ISet<DefaultResourceLeak<T>> allLeaks;
    private readonly int trackedHash;
    private readonly CollectedObjectWatch.Registration registration;

    internal DefaultResourceLeak(T referent, ConcurrentQueue<DefaultResourceLeak<T>> refQueue, ISet<DefaultResourceLeak<T>> allLeaks, object initialHint)
    {
        ArgumentNullException.ThrowIfNull(referent);
        this.allLeaks = allLeaks;
        // Store the hash of the tracked object to later assert it in the close(...) method.
        // It's important that we not store a reference to the referent as this would disallow it from
        // be collected via the WeakReference.
        trackedHash = RuntimeHelpers.GetHashCode(referent);
        allLeaks.Add(this);
        registration = CollectedObjectWatch.register(referent, () => refQueue.Enqueue(this));
        // Create a new Record so we always have the creation stacktrace included.
        try { head = initialHint == null ? new TraceRecord(TraceRecord.BOTTOM) : new TraceRecord(TraceRecord.BOTTOM, initialHint); }
        finally { GC.KeepAlive(referent); }
    }
    public void record() => record0(null);
    public void record(object hint) => record0(hint);
    /**
         * This method works by exponentially backing off as more records are present in the stack. Each record has a
         * 1 / 2^n chance of dropping the top most record and replacing it with itself. This has a number of convenient
         * properties:
         *
         * <ol>
         * <li>  The current record is always recorded. This is due to the compare and swap dropping the top most
         *       record, rather than the to-be-pushed record.
         * <li>  The very last access will always be recorded. This comes as a property of 1.
         * <li>  It is possible to retain more records than the target, based upon the probability distribution.
         * <li>  It is easy to keep a precise record of the number of elements in the stack, since each element has to
         *     know how tall the stack is.
         * </ol>
         *
         * In this particular implementation, there are also some advantages. A thread local random is used to decide
         * if something should be recorded. This means that if there is a deterministic access pattern, it is now
         * possible to see what other accesses occur, rather than always dropping them. Second, after
         * {@link #TARGET_RECORDS} accesses, backoff occurs. This matches typical access patterns,
         * where there are either a high number of accesses (i.e. a cached buffer), or low (an ephemeral buffer), but
         * not many in between.
         * <p>
         * The use of atomics avoids serializing a high number of accesses, when most of the records will be thrown
         * away. High contention only happens when there are very few existing records, which is only likely when the
         * object isn't shared! If this is a problem, the loop can be aborted and the record dropped, because another
         * thread won the race.
         */
    private void record0(object hint)
    {
        // Check TARGET_RECORDS > 0 here to avoid similar check before remove from and add to lastRecords
        if (TARGET_RECORDS <= 0) return;
        TraceRecord previous, next;
        bool dropped;
        do
        {
            previous = Volatile.Read(ref head);
            if (previous == null || previous.pos() == TraceRecord.CLOSE_MARK_POS)
            {
                // already closed.
                return;
            }
            TraceRecord prior = previous;
            int count = previous.pos() + 1;
            dropped = count >= TARGET_RECORDS && ThreadLocalRandom.current().Next(1 << Math.Min(count - TARGET_RECORDS, 30)) != 0;
            if (dropped) prior = previous.next();
            next = hint == null ? new TraceRecord(prior) : new TraceRecord(prior, hint);
        } while (!ReferenceEquals(Interlocked.CompareExchange(ref head, next, previous), previous));
        if (dropped) Interlocked.Increment(ref droppedRecords);
    }
    internal bool dispose() { registration.cancel(); return allLeaks.Remove(this); }
    public bool close()
    {
        if (!allLeaks.Remove(this)) return false;
        // Call clear so the reference is not even enqueued.
        registration.cancel();
        Volatile.Write(ref head, TRACK_CLOSE ? new TraceRecord(true) : null);
        return true;
    }
    public bool close(T trackedObject)
    {
        // Ensure that the object that was tracked is the same as the one that was passed to close(...).
        Debug.Assert(trackedHash == RuntimeHelpers.GetHashCode(trackedObject));
        try { return close(); }
        finally
        {
            // This method will do `synchronized(trackedObject)` and we should be sure this will not cause deadlock.
            // It should not, because somewhere up the callstack should be a (successful) `trackedObject.release`,
            // therefore it is unreasonable that anyone else, anywhere, is holding a lock on the trackedObject.
            // (Unreasonable but possible, unfortunately.)
            // CLR GC.KeepAlive supplies the reachability fence without a resource monitor.
            reachabilityFence0(trackedObject);
        }
    }
    /**
         * Ensures that the object referenced by the given reference remains
         * <a href="package-summary.html#reachability"><em>strongly reachable</em></a>,
         * regardless of any prior actions of the program that might otherwise cause
         * the object to become unreachable; thus, the referenced object is not
         * reclaimable by garbage collection at least until after the invocation of
         * this method.
         *
         * <p> Recent versions of the JDK have a nasty habit of prematurely deciding objects are unreachable.
         * see: https://stackoverflow.com/questions/26642153/finalize-called-on-strongly-reachable-object-in-java-8
         * The Java 9 method Reference.reachabilityFence offers a solution to this problem.
         *
         * <p> This method is always implemented as a synchronization on {@code ref}, not as
         * {@code Reference.reachabilityFence} for consistency across platforms and to allow building on JDK 6-8.
         * <b>It is the caller's responsibility to ensure that this synchronization will not cause deadlock.</b>
         *
         * @param ref the reference. If {@code null}, this method has no effect.
         * @see java.lang.ref.Reference#reachabilityFence
         */
    private static void reachabilityFence0(object referent)
    {
        // Empty synchronized is ok: https://stackoverflow.com/a/31933260/1151521
        // The original synchronization is replaced by the CLR's explicit lifetime fence.
        GC.KeepAlive(referent);
    }
    public Exception getCloseStackTraceIfAny()
    {
        TraceRecord current = Volatile.Read(ref head);
        return current?.pos() == TraceRecord.CLOSE_MARK_POS ? current : null;
    }
    public override string ToString() => generateReport(Volatile.Read(ref head));
    internal string getReportAndClearRecords() => generateReport(Interlocked.Exchange(ref head, null));
    private string generateReport(TraceRecord current)
    {
        if (current == null)
        {
            // Already closed
            return StringUtil.EMPTY_STRING;
        }
        int dropped = Volatile.Read(ref droppedRecords), duped = 0;
        int present = current.pos() + 1;
        // Guess about 2 kilobytes per stack trace
        var buffer = new StringBuilder(present * 2048).Append(StringUtil.NEWLINE);
        buffer.Append("Recent access records: ").Append(StringUtil.NEWLINE);
        int index = 1;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (; current != TraceRecord.BOTTOM; current = current.next())
        {
            string record = current.ToString();
            if (!seen.Add(record)) { duped++; continue; }
            if (current.next() == TraceRecord.BOTTOM) buffer.Append("Created at:").Append(StringUtil.NEWLINE).Append(record);
            else buffer.Append('#').Append(index++).Append(':').Append(StringUtil.NEWLINE).Append(record);
        }
        if (duped > 0) buffer.Append(": ").Append(duped).Append(" leak records were discarded because they were duplicates").Append(StringUtil.NEWLINE);
        if (dropped > 0) buffer.Append(": ").Append(dropped).Append(" leak records were discarded because the leak record count is targeted to ")
            .Append(TARGET_RECORDS).Append(". Use system property ").Append(PROP_TARGET_RECORDS).Append(" to increase the limit.").Append(StringUtil.NEWLINE);
        buffer.Length -= StringUtil.NEWLINE.Length;
        return buffer.ToString();
    }
}
