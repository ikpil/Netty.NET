using Netty.NET.Common.Functional;

namespace Netty.NET.Common;

public interface IMessagePassingQueue<T>
{
    void clear();
    int drain(IConsumer<T> c, int limit);

    bool relaxedOffer(T var1);
    T relaxedPoll();
    T relaxedPeek();
    int size();
}