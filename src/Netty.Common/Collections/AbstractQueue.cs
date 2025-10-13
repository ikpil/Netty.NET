using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Collections;

public abstract class AbstractQueue<T> : IQueue<T>
{
    public abstract int Count { get; }

    public abstract bool isEmpty();
    public abstract bool tryEnqueue(T item);
    public abstract bool tryDequeue(out T item);
    public abstract bool tryPeek(out T item);
    public abstract bool tryRemove(T item);
    public abstract void clear();
    public abstract int drain(IConsumer<T> consumer, int limit);
}