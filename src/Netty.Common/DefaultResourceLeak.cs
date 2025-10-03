using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.ResourceLeakDetector;

namespace Netty.NET.Common;

//@SuppressWarnings("deprecation")
internal class DefaultResourceLeak<T> : IResourceLeakTracker<T>
{
    //@SuppressWarnings("unused")
    private readonly AtomicReference<TraceRecord> head = new AtomicReference<TraceRecord>();

    //@SuppressWarnings("unused")
    private readonly AtomicInteger droppedRecords = new AtomicInteger();

    private readonly ISet<IResourceLeakTracker<T>> allLeaks;
    private readonly int trackedHash;

    public DefaultResourceLeak(
            object referent,
            ReferenceQueue<object> refQueue,
            ISet<IResourceLeakTracker<T>> allLeaks,
            object initialHint) {
        super(referent, refQueue);

        Debug.Assert(referent != null);

        this.allLeaks = allLeaks;

        // Store the hash of the tracked object to later assert it in the close(...) method.
        // It's important that we not store a reference to the referent as this would disallow it from
        // be collected via the WeakReference.
        trackedHash = RuntimeHelpers.GetHashCode(referent);
        allLeaks.Add(this);
        // Create a new Record so we always have the creation stacktrace included.
        head.set(initialHint == null 
            ? new TraceRecord(TraceRecord.BOTTOM) 
            : new TraceRecord(TraceRecord.BOTTOM, initialHint)
        );
    }

    public void record() {
        record0(null);
    }

    public void record(object hint) {
        record0(hint);
    }

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
     *
     * The use of atomics avoids serializing a high number of accesses, when most of the records will be thrown
     * away. High contention only happens when there are very few existing records, which is only likely when the
     * object isn't shared! If this is a problem, the loop can be aborted and the record dropped, because another
     * thread won the race.
     */
    private void record0(object hint) {
        // Check TARGET_RECORDS > 0 here to avoid similar check before remove from and add to lastRecords
        if (TARGET_RECORDS > 0) {
            TraceRecord oldHead;
            TraceRecord prevHead;
            TraceRecord newHead;
            bool dropped;
            do {
                if ((prevHead = oldHead = head.get()) == null) {
                    // already closed.
                    return;
                }
                int numElements = oldHead.pos() + 1;
                if (numElements >= TARGET_RECORDS) {
                    int backOffFactor = Math.Min(numElements - TARGET_RECORDS, 30);
                    dropped = ThreadLocalRandom.current().Next(1 << backOffFactor) != 0;
                    if (dropped)
                    {
                        prevHead = oldHead.next();
                    }
                } else {
                    dropped = false;
                }
                newHead = hint != null ? new TraceRecord(prevHead, hint) : new TraceRecord(prevHead);
            } while (!head.compareAndSet(oldHead, newHead));
            if (dropped) {
                droppedRecords.incrementAndGet();
            }
        }
    }

    public bool dispose() {
        clear();
        return allLeaks.remove(this);
    }

    @Override
    public bool close() {
        if (allLeaks.remove(this)) {
            // Call clear so the reference is not even enqueued.
            clear();
            head.set(null);
            return true;
        }
        return false;
    }

    @Override
    public bool close(T trackedObject) {
        // Ensure that the object that was tracked is the same as the one that was passed to close(...).
        Debug.Assert(trackedHash == RuntimeHelpers.GetHashCode(trackedObject));

        try {
            return close();
        } finally {
            // This method will do `synchronized(trackedObject)` and we should be sure this will not cause deadlock.
            // It should not, because somewhere up the callstack should be a (successful) `trackedObject.release`,
            // therefore it is unreasonable that anyone else, anywhere, is holding a lock on the trackedObject.
            // (Unreasonable but possible, unfortunately.)
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
    private static void reachabilityFence0(object @ref) {
        if (@ref != null) {
            lock (@ref) {
                // Empty synchronized is ok: https://stackoverflow.com/a/31933260/1151521
            }
        }
    }

    public override string ToString() {
        TraceRecord oldHead = head.get();
        return generateReport(oldHead);
    }

    string getReportAndClearRecords() {
        TraceRecord oldHead = head.getAndSet(null);
        return generateReport(oldHead);
    }

    private string generateReport(TraceRecord oldHead) {
        if (oldHead == null) {
            // Already closed
            return StringUtil.EMPTY_STRING;
        }

        int dropped = droppedRecords.get();
        int duped = 0;

        int present = oldHead.pos() + 1;
        // Guess about 2 kilobytes per stack trace
        StringBuilder buf = new StringBuilder(present * 2048).Append(StringUtil.NEWLINE);
        buf.Append("Recent access records: ").Append(StringUtil.NEWLINE);

        int i = 1;
        ISet<string> seen = new HashSet<string>(present);
        for (; oldHead != TraceRecord.BOTTOM; oldHead = oldHead.next()) {
            string s = oldHead.ToString();
            if (seen.Add(s)) {
                if (oldHead.next() == TraceRecord.BOTTOM) {
                    buf.Append("Created at:").Append(StringUtil.NEWLINE).Append(s);
                } else {
                    buf.Append('#').Append(i++).Append(':').Append(StringUtil.NEWLINE).Append(s);
                }
            } else {
                duped++;
            }
        }

        if (duped > 0) {
            buf.Append(": ")
                    .Append(duped)
                    .Append(" leak records were discarded because they were duplicates")
                    .Append(StringUtil.NEWLINE);
        }

        if (dropped > 0) {
            buf.Append(": ")
               .Append(dropped)
               .Append(" leak records were discarded because the leak record count is targeted to ")
               .Append(TARGET_RECORDS)
               .Append(". Use system property ")
               .Append(PROP_TARGET_RECORDS)
               .Append(" to increase the limit.")
               .Append(StringUtil.NEWLINE);
        }

        buf.Length -= StringUtil.NEWLINE.length();
        return buf.ToString();
    }
}
