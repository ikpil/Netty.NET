namespace Netty.NET.Common.Concurrent;

public static class FastThreadLocalThread
{
    public static bool currentThreadHasFastThreadLocal()
    {
        return true;
    }
}