using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Collections;

public interface IQueue<T>
{
    int Count { get; }
    bool IsEmpty { get; }
    
    bool TryEnqueue(T item);
    bool TryDequeue(out T item);
    bool TryPeek(out T item);
    void Clear();
    int Drain(IConsumer<T> consumer, int limit);
}