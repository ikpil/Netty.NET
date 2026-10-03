namespace Netty.NET.Common.Functional;

public interface IConsumer<in T> 
{
    void Accept(T var1);
}
