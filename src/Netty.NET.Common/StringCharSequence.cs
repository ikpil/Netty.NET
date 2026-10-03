using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

public sealed class StringCharSequence : ICharSequence, IEquatable<StringCharSequence>
{
    public static readonly StringCharSequence Empty = new StringCharSequence(string.Empty);

    private readonly string _value;
    private readonly int _offset;
    private readonly int _count;

    public StringCharSequence(string value)
    {
        _value = ObjectUtil.checkNotNull(value, nameof(value));
        _offset = 0;
        _count = _value.Length;
    }

    public StringCharSequence(string value, int offset, int count)
    {
        ObjectUtil.checkNotNull(value, nameof(value));
        if (MathUtil.isOutOfBounds(offset, count, value.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        _value = value;
        _offset = offset;
        _count = count;
    }

    public int Count => _count;

    /// <summary>Returns the logical UTF-16 view without copying or allocating a substring.</summary>
    public ReadOnlySpan<char> AsSpan() => _value.AsSpan(_offset, _count);

    /// <summary>Returns a logical view that retains the immutable backing string.</summary>
    public ReadOnlyMemory<char> AsMemory() => _value.AsMemory(_offset, _count);

    public static explicit operator string(StringCharSequence charSequence)
    {
        Contract.Requires(charSequence != null);
        return charSequence.ToString();
    }

    public static explicit operator StringCharSequence(string value)
    {
        Contract.Requires(value != null);

        return value.Length > 0 ? new StringCharSequence(value) : Empty;
    }

    public ICharSequence subSequence(int start) => subSequence(start, _count);

    public char charAt(int index)
    {
        return this[index];
    }

    public int length()
    {
        return _count;
    }

    public ICharSequence subSequence(int start, int end)
    {
        if (start < 0 || end < start || end > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        return end == start
            ? Empty
            : new StringCharSequence(_value, _offset + start, end - start);
    }

    public char this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return _value[_offset + index];
        }
    }

    public bool regionMatches(bool ignoreCase, int thisStart, ICharSequence seq, int start, int length) => ignoreCase
        ? regionMatchesIgnoreCase(thisStart, seq, start, length)
        : regionMatches(thisStart, seq, start, length);

    public bool regionMatches(int thisStart, ICharSequence seq, int start, int length) =>
        CharUtil.RegionMatches(this, thisStart, seq, start, length);

    public bool regionMatchesIgnoreCase(int thisStart, ICharSequence seq, int start, int length) =>
        CharUtil.RegionMatchesIgnoreCase(this, thisStart, seq, start, length);

    public int indexOf(char ch, int start = 0)
    {
        start = Math.Max(0, start);
        if (start >= _count) return -1;
        int index = AsSpan().Slice(start).IndexOf(ch);
        return index < 0 ? -1 : start + index;
    }

    public int indexOf(string target, int start = 0)
    {
        ObjectUtil.checkNotNull(target, nameof(target));
        start = Math.Max(0, start);
        if (start > _count) return target.Length == 0 ? _count : -1;
        int index = _value.IndexOf(target, _offset + start, _count - start, StringComparison.Ordinal);
        return index < 0 ? -1 : index - _offset;
    }

    public string ToString(int start)
    {
        if (start < 0 || start > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
        return _value.Substring(_offset + start, _count - start);
    }

    public override string ToString() => _count == 0 ? string.Empty : ToString(0);

    public bool Equals(StringCharSequence other)
    {
        if (other == null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_count != other._count)
        {
            return false;
        }

        return string.Compare(_value, _offset, other._value, other._offset, _count,
            StringComparison.Ordinal) == 0;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(obj, null))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is StringCharSequence other)
        {
            return Equals(other);
        }

        if (obj is ICharSequence seq)
        {
            return contentEquals(seq);
        }

        return false;
    }

    public int hashCode(bool ignoreCase) => ignoreCase
        ? StringComparer.OrdinalIgnoreCase.GetHashCode(ToString())
        : StringComparer.Ordinal.GetHashCode(ToString());

    public override int GetHashCode() => hashCode(false);

    public bool contentEquals(ICharSequence other) => CharUtil.ContentEquals(this, other);

    public bool contentEqualsIgnoreCase(ICharSequence other) => CharUtil.ContentEqualsIgnoreCase(this, other);

    public IEnumerator<char> GetEnumerator() => new CharSequenceEnumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
