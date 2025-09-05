namespace Netty.NET.Common.Collections;

using System;
using System.Collections.Generic;
using System.Threading;

public class LinkedBlockingQueue<T>
{
    private readonly LinkedList<T> _list = new LinkedList<T>();
    private readonly int _capacity;
    private readonly object _lock = new object();

    public LinkedBlockingQueue(int capacity = int.MaxValue)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _list.Count;
            }
        }
    }

    // 블로킹 추가
    public void Enqueue(T item)
    {
        lock (_lock)
        {
            while (_list.Count >= _capacity)
            {
                Monitor.Wait(_lock);
            }

            _list.AddLast(item);
            Monitor.PulseAll(_lock); // Dequeue 대기 스레드 깨움
        }
    }

    // 블로킹 제거
    public T Dequeue()
    {
        lock (_lock)
        {
            while (_list.Count == 0)
            {
                Monitor.Wait(_lock);
            }

            var value = _list.First.Value;
            _list.RemoveFirst();
            Monitor.PulseAll(_lock); // Enqueue 대기 스레드 깨움
            return value;
        }
    }

    // 타임아웃 있는 추가
    public bool TryEnqueue(T item, TimeSpan timeout)
    {
        lock (_lock)
        {
            if (_list.Count >= _capacity)
            {
                if (!Monitor.Wait(_lock, timeout))
                    return false;
            }

            if (_list.Count >= _capacity)
                return false;

            _list.AddLast(item);
            Monitor.PulseAll(_lock);
            return true;
        }
    }

    // 타임아웃 있는 제거
    public bool TryDequeue(out T item, TimeSpan timeout)
    {
        lock (_lock)
        {
            if (_list.Count == 0)
            {
                if (!Monitor.Wait(_lock, timeout))
                {
                    item = default!;
                    return false;
                }
            }

            if (_list.Count == 0)
            {
                item = default!;
                return false;
            }

            item = _list.First.Value;
            _list.RemoveFirst();
            Monitor.PulseAll(_lock);
            return true;
        }
    }
}