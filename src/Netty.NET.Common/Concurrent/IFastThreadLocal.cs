using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

public interface IFastThreadLocal
{
    void remove(InternalThreadLocalMap threadLocalMap);
}