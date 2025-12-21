using System.Collections.Concurrent;
using System.Collections.Generic;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Collections;

public static class CollectionExtensions
{
    public static bool isEmpty(this string str)
    {
        return string.IsNullOrEmpty(str);
    }

    public static bool IsEmpty<T>(this IReadOnlyList<T> c)
    {
        return 0 >= c.Count;
    }

    public static bool IsEmpty<TKey, TValue>(this Dictionary<TKey, TValue> c)
    {
        return 0 >= c.Count;
    }

    public static bool IsEmpty<T>(this ISet<T> c)
    {
        return 0 >= c.Count;
    }

    public static bool IsEmpty<T>(this ConcurrentHashSet<T> c)
    {
        return 0 >= c.Count;
    }

    public static bool IsEmpty<T>(this BlockingCollection<T> c)
    {
        return 0 >= c.Count;
    }
}