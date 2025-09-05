using System.Collections.Concurrent;
using System.Collections.Generic;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Collections;

public interface IQueue<T> : IProducerConsumerCollection<T>, IReadOnlyCollection<T>
{
    int Count { get; }
    bool IsEmpty();

    bool TryRemove(T item);
    bool TryEnqueue(T item);
    bool TryDequeue(out T item);
    bool TryPeek(out T item);
    void Clear();
    int Drain(IConsumer<T> consumer, int limit);
}