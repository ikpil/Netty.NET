namespace Netty.NET.Common.Internal;

internal static class Pow2
{
    public const int MAX_POW2 = 1 << 30; // 2^30 
    
    public static long align(long value, int alignment)
    {
        return (value + alignment - 1) & -alignment;
    }
}