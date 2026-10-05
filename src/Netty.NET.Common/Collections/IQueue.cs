namespace Netty.NET.Common.Collections;

public interface IQueue<T>
{
    int Count { get; }
    bool IsEmpty();
    bool TryRemove(T item);
    bool TryEnqueue(T item);
    bool TryDequeue(out T item);
    bool TryPeek(out T item);
    void Clear();
}
