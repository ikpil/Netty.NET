using System.Collections.Generic;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Collections;

/**
 * This is an implementation of {@link MessagePassingQueue}, similar to what might be returned from
 * {@link PlatformDependent#newMpscQueue(int)}, but intended to be used for debugging purpose.
 * The implementation relies on synchronised monitor locks for thread-safety.
 * The {@code fill} bulk operation is not supported by this implementation.
 */
public class BlockingQueue<T> : IQueue<T>
{
    private readonly object _lock;
    private readonly Queue<T> _deque;
    private readonly int _maxCapacity;

    public BlockingQueue(int maxCapacity)
    {
        _lock = new object();
        _maxCapacity = maxCapacity;

        // This message passing queue is backed by an ArrayDeque instance,
        // made thread-safe by synchronising on `this` BlockingQueue instance.
        // Why ArrayDeque?
        // We use ArrayDeque instead of LinkedList or LinkedBlockingQueue because it's more space efficient.
        // We use ArrayDeque instead of List because we need the queue APIs.
        // We use ArrayDeque instead of ConcurrentLinkedQueue because CLQ is unbounded and has O(n) size().
        // We use ArrayDeque instead of ArrayBlockingQueue because ABQ allocates its max capacity up-front,
        // and these queues will usually have large capacities, in potentially great numbers (one per thread),
        // but often only have comparatively few items in them.
        _deque = new Queue<T>(maxCapacity);
    }


    public int Count => CountInternal();
    public bool IsEmpty => IsEmptyInternal();

    public bool TryEnqueue(T item)
    {
        lock (_lock)
        {
            if (_deque.Count == _maxCapacity)
            {
                return false;
            }

            _deque.Enqueue(item);
            return true;
        }
    }


    public bool TryDequeue(out T item)
    {
        lock (_lock)
        {
            item = _deque.Dequeue();
            return true;
        }
    }

    public bool TryPeek(out T item)
    {
        lock (_lock)
        {
            return _deque.TryPeek(out item);
        }
    }

    public int Drain(IConsumer<T> c, int limit)
    {
        T item;
        int i = 0;
        for (; i < limit && TryDequeue(out item); i++)
        {
            c.accept(item);
        }

        return i;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _deque.Clear();
        }
    }


    private int CountInternal()
    {
        lock (_lock)
        {
            return _deque.Count;
        }
    }


    private bool IsEmptyInternal()
    {
        lock (_lock)
        {
            return 0 >= _deque.Count;
        }
    }
}