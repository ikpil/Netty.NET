namespace Netty.NET.Common.Functional;

public interface ICallable<out T>
{
    T call();
}