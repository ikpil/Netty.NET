namespace Netty.NET.Common.Concurrent;

public interface IDefaultAttribute
{
    IAttributeKey Key();
    bool IsRemoved();
}