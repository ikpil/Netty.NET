using System;

namespace Netty.NET.Common;

public static class Arrays
{
    public static T[] copyOfRange<T>(T[] array, int start, int end) where T : struct
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));

        if (start < 0 || start > array.Length)
            throw new ArgumentOutOfRangeException(nameof(start), "start index out of range");

        if (end < start)
            throw new ArgumentException("end index must not be less than start index");

        if (end > array.Length)
            throw new ArgumentOutOfRangeException(nameof(end), "end index out of range");

        int length = end - start;
        T[] result = new T[length];
        Array.Copy(array, start, result, 0, length);
        return result;
    }

    public static void arraycopy<T>(T[] source, int sourceIndex, T[] destination, int destinationIndex, int length)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        if (sourceIndex < 0 || destinationIndex < 0 || length < 0)
            throw new ArgumentOutOfRangeException("Indices and length must be non-negative");
        if (sourceIndex + length > source.Length)
            throw new ArgumentException("Source array is not long enough.");
        if (destinationIndex + length > destination.Length)
            throw new ArgumentException("Destination array is not long enough.");

        Array.Copy(source, sourceIndex, destination, destinationIndex, length);
    }
}