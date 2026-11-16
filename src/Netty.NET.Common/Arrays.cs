using System;
using System.Collections.Generic;
using System.Globalization;

namespace Netty.NET.Common;

public static class Arrays
{
    public static void Fill<T>(T[] src, T val)
    {
        Array.Fill(src, val);
    }

    public static void Fill<T>(T[] src, int fromIndex, int toIndex, T val)
    {
        Array.Fill(src, val, fromIndex, toIndex - fromIndex);
    }

    public static T[] CopyOf<T>(T[] src, int size)
    {
        var dest = new T[size];
        Array.Copy(src, dest, Math.Min(src.Length, size));
        return dest;
    }

    // java.util.Arrays.copyOfRange: from must lie in [0, original.length] (ArrayIndexOutOfBoundsException, here
    // ArgumentOutOfRangeException from Arraycopy), from > to is an IllegalArgumentException, and a to beyond
    // original.length pads the copy with default values.
    public static T[] CopyOfRange<T>(T[] original, int from, int to) where T : struct
    {
        ArgumentNullException.ThrowIfNull(original);
        int newLength = to - from;
        if (newLength < 0)
        {
            throw new ArgumentException(from.ToString(CultureInfo.InvariantCulture) + " > " + to.ToString(CultureInfo.InvariantCulture));
        }
        T[] copy = new T[newLength];
        Arraycopy(original, from, copy, 0, Math.Min(original.Length - from, newLength));
        return copy;
    }

    public static void Arraycopy<T>(T[] source, int sourceIndex, T[] destination, int destinationIndex, int length)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        // System.arraycopy reports every range violation as ArrayIndexOutOfBoundsException.
        if (sourceIndex < 0 || destinationIndex < 0 || length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Indices and length must be non-negative");
        if (length > source.Length - sourceIndex)
            throw new ArgumentOutOfRangeException(nameof(sourceIndex), "Source array is not long enough.");
        if (length > destination.Length - destinationIndex)
            throw new ArgumentOutOfRangeException(nameof(destinationIndex), "Destination array is not long enough.");

        Array.Copy(source, sourceIndex, destination, destinationIndex, length);
    }

}
