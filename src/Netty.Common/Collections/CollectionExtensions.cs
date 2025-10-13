using System.Collections.Generic;

namespace Netty.NET.Common.Collections;

public static class CollectionExtensions
{
    public static bool isEmpty(this string str)
    {
        return string.IsNullOrEmpty(str);
    }
    
    public static bool IsEmpty<T>(this List<T> c)
    {
        return 0 >= c.Count;
    }
    
    public static bool IsEmpty<TKey, TValue>(this Dictionary<TKey, TValue> c)
    {
        return 0 >= c.Count;
    }
    
    public static bool IsEmpty<T>(this HashSet<T> c)
    {
        return 0 >= c.Count;
    }

    public static bool IsEmpty<T>(this ConcurrentHashSet<T> c)
    {
        return 0 >= c.Count;
    }
}