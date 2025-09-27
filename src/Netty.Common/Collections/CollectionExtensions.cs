using System.Collections.Generic;

namespace Netty.NET.Common.Collections;

public static class CollectionExtensions
{
    public static bool IsEmpty<T>(this IReadOnlyCollection<T> a)
    {
        return 0 >= a.Count;
    }

    public static bool IsEmpty<T>(this ICollection<T> a)
    {
        return 0 >= a.Count;
    }
}