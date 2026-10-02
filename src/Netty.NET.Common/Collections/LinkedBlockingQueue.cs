using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Collections;

// CLR adapter for the JDK queue used by Netty executors. A shared monitor keeps
// offer/poll/peek/remove atomic without draining and rebuilding a live queue.
public class LinkedBlockingQueue<T> : IQueue<T>, IBlockingQueue<T>
{
    private readonly object gate = new();
    private readonly LinkedList<T> queue = new();
    private readonly int capacity;
    private int count;

    public LinkedBlockingQueue(int boundedCapacity)
    {
        if (boundedCapacity <= 0) throw new ArgumentException("capacity must be positive", nameof(boundedCapacity));
        capacity = boundedCapacity;
    }

    // CLR compatibility overload: import the supplied collection's initial FIFO
    // snapshot. The adapter owns subsequent queue mutations and synchronization.
    public LinkedBlockingQueue(IProducerConsumerCollection<T> collection, int boundedCapacity)
        : this(boundedCapacity)
    {
        ArgumentNullException.ThrowIfNull(collection);
        foreach (T item in collection.ToArray()) add(item);
    }

    public int Count => Volatile.Read(ref count);
    public bool isEmpty() => Count == 0;

    public bool tryEnqueue(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        using (UninterruptibleMonitor.enter(gate))
        {
            if (queue.Count >= capacity) return false;
            queue.AddLast(item);
            Volatile.Write(ref count, queue.Count);
            Monitor.PulseAll(gate);
            return true;
        }
    }

    public void add(T item)
    {
        if (!tryEnqueue(item)) throw new InvalidOperationException("Queue full");
    }

    public bool tryDequeue(out T item) => tryTake(out item);

    public bool tryTake(out T item)
    {
        using (UninterruptibleMonitor.enter(gate)) return poll(out item);
    }

    private bool poll(out T item)
    {
        var head = queue.First;
        if (head == null) { item = default; return false; }
        item = head.Value;
        queue.RemoveFirst();
        Volatile.Write(ref count, queue.Count);
        return true;
    }

    public bool tryPeek(out T item)
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            var head = queue.First;
            if (head == null) { item = default; return false; }
            item = head.Value;
            return true;
        }
    }

    public virtual bool tryRemove(T item)
    {
        if (item is null) return false;
        using (UninterruptibleMonitor.enter(gate))
        {
            var node = queue.Find(item);
            if (node == null) return false;
            queue.Remove(node);
            Volatile.Write(ref count, queue.Count);
            return true;
        }
    }

    public T take()
    {
        // JDK take/lockInterruptibly observes a pending interrupt even if the
        // queue is populated. CLR only exposes it at an interruptible wait.
        Thread.Sleep(0);
        lock (gate)
        {
            while (queue.Count == 0) Monitor.Wait(gate);
            poll(out T item);
            return item;
        }
    }

    public bool tryTake(out T item, TimeSpan timeout)
    {
        Thread.Sleep(0);
        long started = Stopwatch.GetTimestamp();
        lock (gate)
        {
            while (queue.Count == 0)
            {
                long elapsed = Stopwatch.GetElapsedTime(started).Ticks;
                if (timeout.Ticks <= elapsed) { item = default; return false; }
                long remaining = timeout.Ticks - elapsed;
                int millis = (int)Math.Min(int.MaxValue, 1 + (remaining - 1) / TimeSpan.TicksPerMillisecond);
                Monitor.Wait(gate, millis);
            }
            return poll(out item);
        }
    }

    public void clear()
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            queue.Clear();
            Volatile.Write(ref count, 0);
        }
    }

    public int drain(IConsumer<T> consumer, int limit)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        int count = 0;
        while (count < limit && tryTake(out T item))
        {
            consumer.accept(item);
            ++count;
        }
        return count;
    }
}
