using System.Collections.Generic;

namespace Netty.NET.Common.Collections;

public static class CollectionExtensions
{
    public static bool IsEmpty<T>(this ISet<T> a)
    {
        return 0 >= a.Count;
    }
}