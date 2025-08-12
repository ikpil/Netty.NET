namespace Netty.NET.Common.Functional;

public interface IConsumer<in T> 
{
    void accept(T var1);
}
