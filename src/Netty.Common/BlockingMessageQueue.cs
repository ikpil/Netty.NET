using System;
using System.Collections.Generic;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common;

/**
 * This is an implementation of {@link MessagePassingQueue}, similar to what might be returned from
 * {@link PlatformDependent#newMpscQueue(int)}, but intended to be used for debugging purpose.
 * The implementation relies on synchronised monitor locks for thread-safety.
 * The {@code fill} bulk operation is not supported by this implementation.
 */
public class BlockingMessageQueue<T> : IMessagePassingQueue<T>
{
    private readonly object _lock;
    private readonly Queue<T> _deque;
    private readonly int _maxCapacity;

    public BlockingMessageQueue(int maxCapacity)
    {
        _lock = new object();
        _maxCapacity = maxCapacity;
        
        // This message passing queue is backed by an ArrayDeque instance,
        // made thread-safe by synchronising on `this` BlockingMessageQueue instance.
        // Why ArrayDeque?
        // We use ArrayDeque instead of LinkedList or LinkedBlockingQueue because it's more space efficient.
        // We use ArrayDeque instead of List because we need the queue APIs.
        // We use ArrayDeque instead of ConcurrentLinkedQueue because CLQ is unbounded and has O(n) size().
        // We use ArrayDeque instead of ArrayBlockingQueue because ABQ allocates its max capacity up-front,
        // and these queues will usually have large capacities, in potentially great numbers (one per thread),
        // but often only have comparatively few items in them.
        _deque = new Queue<T>(maxCapacity);
    }

    public bool offer(T e)
    {
        lock (_lock)
        {
            if (_deque.Count == _maxCapacity)
            {
                return false;
            }

            _deque.Enqueue(e);
            return true;
        }
    }

    public T poll()
    {
        lock (_lock)
        {
            return _deque.Dequeue();
        }
    }

    public T peek()
    {
        lock (_lock)
        {
            _deque.TryPeek(out var result);
            return result;
        }
    }

    public int size()
    {
        lock (_lock)
        {
            return _deque.Count;
        }
    }

    public void clear()
    {
        lock (_lock)
        {
            _deque.Clear();
        }
    }

    public bool isEmpty()
    {
        lock (_lock)
        {
            return 0 == _deque.Count;
        }
    }

    public int capacity()
    {
        return _maxCapacity;
    }

    public int drain(IConsumer<T> c, int limit)
    {
        T obj;
        int i = 0;
        for (; i < limit && (obj = poll()) != null; i++)
        {
            c.accept(obj);
        }

        return i;
    }
    
    public bool relaxedOffer(T var1)
    {
        return offer(var1);
    }
    
    public T relaxedPoll()
    {
        return poll();
    }
    
    public T relaxedPeek()
    {
        return peek();
    }


    public int fill(Func<T> s, int limit)
    {
        throw new NotSupportedException();
    }

    public int drain(Action<T> c)
    {
        throw new NotSupportedException();
    }

    public int fill(Func<T> s)
    {
        throw new NotSupportedException();
    }

    public void drain(Action<T> c, IWaitStrategy wait, IExitCondition exit)
    {
        throw new NotSupportedException();
    }

    public void fill(Func<T> s, IWaitStrategy wait, IExitCondition exit)
    {
        throw new NotSupportedException();
    }
}