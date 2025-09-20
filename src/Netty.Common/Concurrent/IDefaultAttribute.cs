namespace Netty.NET.Common.Concurrent;

public interface IDefaultAttribute
{
    IAttributeKey key();
    bool isRemoved();
}