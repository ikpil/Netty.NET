using System;
using System.Collections.Generic;

namespace Netty.NET.Common.Collections;

public static class Collectives
{
    public static IReadOnlyList<T> EmptyList<T>()
    {
        return Array.Empty<T>();
    }
    public static IReadOnlyList<T> SingletonList<T>(T item)
    {
        var l = new List<T>(1);
        l.Add(item);
        return l;
    }
    public static List<T> AsList<T>(params T[] items)
    {
        return new List<T>(items);
    }
}