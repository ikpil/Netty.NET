using System;

namespace Netty.NET.Common.Collections;

public interface IBlockingQueue<T>
{
    T take(); // blocks until an item is available
    bool tryTake(out T item); // immediately returns false if queue is empty
    bool tryTake(out T item, TimeSpan timeout); // waits for timeout if queue is empty
}