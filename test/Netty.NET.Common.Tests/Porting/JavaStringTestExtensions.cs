using System;
using System.Text.RegularExpressions;

namespace Netty.NET.Common.Tests;

// The upstream StringUtil suite also checks Java String.split behavior. This
// fixture adapter preserves regex delimiters and trailing-empty-field semantics
// for those tests; it is not an implementation of a Netty public API.
internal static class JavaStringTestExtensions
{
    internal static string[] split(this string value, string pattern, int limit = 0)
    {
        var regex = new Regex(pattern);
        string[] parts = limit > 0 ? regex.Split(value, limit) : regex.Split(value);
        if (limit != 0 || value.Length == 0) return parts;
        int count = parts.Length;
        while (count > 0 && parts[count - 1].Length == 0) count--;
        Array.Resize(ref parts, count);
        return parts;
    }
}
