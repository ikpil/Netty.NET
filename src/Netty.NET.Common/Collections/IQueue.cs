using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Collections;

public interface IQueue<T>
{
    int Count { get; }
    bool isEmpty();
    bool tryRemove(T item);
    bool tryEnqueue(T item);
    bool tryDequeue(out T item);
    bool tryPeek(out T item);
    void clear();
    int drain(IConsumer<T> consumer, int limit);
}