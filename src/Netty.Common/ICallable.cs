namespace Netty.NET.Common;

public interface ICallable<out T>
{
    T call();
}