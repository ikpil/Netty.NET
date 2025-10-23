using System;
using System.Collections.Generic;

namespace Netty.NET.Common.Collections;

public static class Collectives
{
    public static IReadOnlyList<T> emptyList<T>()
    {
        return Array.Empty<T>();
    }
    public static IReadOnlyList<T> singletonList<T>(T item)
    {
        var l = new List<T>(1);
        l.Add(item);
        return l;
    }
    public static List<T> asList<T>(params T[] items)
    {
        return new List<T>(items);
    }
}