using System;

namespace Netty.NET.Common.Collections;

public interface IBlockingQueue<T>
{
    bool TryDequeue(out T item);
    bool TryDequeue(out T item, TimeSpan timeout);
}