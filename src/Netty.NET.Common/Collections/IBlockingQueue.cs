using System;

namespace Netty.NET.Common.Collections;

public interface IBlockingQueue<T>
{
    T Take(); // blocks until an item is available
    bool TryTake(out T item); // immediately returns false if queue is empty
    bool TryTake(out T item, TimeSpan timeout); // waits for timeout if queue is empty
}