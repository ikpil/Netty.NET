using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Collections;

public abstract class AbstractQueue<T> : IQueue<T>
{
    public abstract int Count { get; }
    public abstract bool IsEmpty { get; }
    public abstract bool TryEnqueue(T item);
    public abstract bool TryDequeue(out T item);
    public abstract bool TryPeek(out T item);
    public abstract void Clear();
    public abstract int Drain(IConsumer<T> consumer, int limit);
}