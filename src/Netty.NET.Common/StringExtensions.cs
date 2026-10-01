using System;
using StringComparison = System.StringComparison;

namespace Netty.NET.Common;

public static class StringExtensions
{
    public static bool EqualsIgnoreCase(this string a, string b)
    {
        return a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    public static char charAt(this string s, int index)
    {
        return s[index];
    }

    public static string substring(this string s, int start)
    {
        return s.Substring(start);
    }

    public static string substring(this string s, int start, int end)
    {
        return s.Substring(start, end - start);
    }

    public static int length(this string s)
    {
        return s.Length;
    }

    public static int indexOf(this string s, char c)
    {
        return s.IndexOf(c);
    }

    public static int indexOf(this string s, char c, int startIndex)
    {
        // Java String.indexOf clamps negative offsets and returns -1 past the end.
        return startIndex >= s.Length ? -1 : s.IndexOf(c, Math.Max(0, startIndex));
    }

    public static int lastIndexOf(this string s, char c)
    {
        return s.LastIndexOf(c);
    }

    public static bool regionMatches(this string str, int toffset, string other, int ooffset, int len)
    {
        return regionMatches(str, false, toffset, other, ooffset, len);
    }

    public static bool regionMatches(this string str, bool ignoreCase, int toffset, string other, int ooffset, int len)
    {
        if (str == null || other == null)
            throw new ArgumentNullException(str == null ? nameof(str) : nameof(other));

        if (toffset < 0 || ooffset < 0 ||
            len > str.Length - toffset || len > other.Length - ooffset)
            return false;

        if (len <= 0)
            return true;

        string strRegion = str.Substring(toffset, len);
        string otherRegion = other.Substring(ooffset, len);
        return string.Equals(strRegion, otherRegion,
            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
