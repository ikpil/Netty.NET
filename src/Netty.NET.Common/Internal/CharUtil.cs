using System;
using System.Collections.Generic;

namespace Netty.NET.Common.Internal;

public static class CharUtil
{
    internal static bool ContentEquals(ICharSequence left, ICharSequence right) =>
        ContentEquals(left, right, false);

    internal static bool ContentEqualsIgnoreCase(ICharSequence left, ICharSequence right) =>
        ContentEquals(left, right, true);

    private static bool ContentEquals(ICharSequence left, ICharSequence right, bool ignoreCase)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left != null && right != null && left.Count == right.Count &&
               CompareRegions(left, 0, right, 0, left.Count, ignoreCase);
    }

    public static bool RegionMatches(IReadOnlyList<char> value, int thisStart, ICharSequence other, int start, int length) =>
        RegionMatches(value, thisStart, other, start, length, false);

    public static bool RegionMatchesIgnoreCase(IReadOnlyList<char> value, int thisStart, ICharSequence other, int start, int length) =>
        RegionMatches(value, thisStart, other, start, length, true);

    private static bool RegionMatches(IReadOnlyList<char> value, int thisStart, ICharSequence other, int start, int length,
        bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(other);
        if (thisStart < 0 || start < 0 || length > value.Count - thisStart || length > other.Count - start)
        {
            return false;
        }

        // Preserve the bridge's empty/negative region rule without adding offsets.
        return length <= 0 || CompareRegions(value, thisStart, other, start, length, ignoreCase);
    }

    private static bool TryGetSpan(IReadOnlyList<char> value, out ReadOnlySpan<char> span)
    {
        switch (value)
        {
            case StringCharSequence text:
                span = text.AsSpan();
                return true;
            case AppendableCharSequence builder:
                span = builder.AsSpan();
                return true;
            case char[] chars:
                span = chars;
                return true;
            default:
                span = default;
                return false;
        }
    }

    private static bool CompareRegions(IReadOnlyList<char> left, int leftStart, IReadOnlyList<char> right, int rightStart,
        int length, bool ignoreCase)
    {
        if (TryGetSpan(left, out ReadOnlySpan<char> leftSpan) && TryGetSpan(right, out ReadOnlySpan<char> rightSpan))
        {
            return leftSpan.Slice(leftStart, length).Equals(rightSpan.Slice(rightStart, length),
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        if (!ignoreCase)
        {
            for (int i = 0; i < length; i++)
            {
                if (left[leftStart + i] != right[rightStart + i])
                {
                    return false;
                }
            }

            return true;
        }

        // Compare complete surrogate pairs with the BCL; never truncate UTF-16 to bytes.
        Span<char> leftUnit = stackalloc char[2];
        Span<char> rightUnit = stackalloc char[2];
        for (int i = 0; i < length;)
        {
            leftUnit[0] = left[leftStart + i];
            rightUnit[0] = right[rightStart + i];
            int width = 1;
            if (i < length - 1 && char.IsHighSurrogate(leftUnit[0]) && char.IsLowSurrogate(left[leftStart + i + 1]))
            {
                width = 2;
                leftUnit[1] = left[leftStart + i + 1];
                rightUnit[1] = right[rightStart + i + 1];
            }

            if (!((ReadOnlySpan<char>)leftUnit[..width]).Equals(rightUnit[..width], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            i += width;
        }

        return true;
    }
}
