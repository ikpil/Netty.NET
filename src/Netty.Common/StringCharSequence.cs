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
        Contract.Requires(value != null);

        _value = value;
        _offset = 0;
        _count = _value.Length;
    }

    public StringCharSequence(string value, int offset, int count)
    {
        Contract.Requires(value != null);
        Contract.Requires(offset >= 0 && count >= 0);
        Contract.Requires(offset <= value.Length - count);

        _value = value;
        _offset = offset;
        _count = count;
    }

    public int Count => _count;

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
        return _value[index];
    }

    public int length()
    {
        return _count;
    }

    public ICharSequence subSequence(int start, int end)
    {
        Contract.Requires(start >= 0 && end >= start);
        Contract.Requires(end <= _count);

        return end == start
            ? Empty
            : new StringCharSequence(_value, _offset + start, end - start);
    }

    public char this[int index]
    {
        get
        {
            Contract.Requires(index >= 0 && index < _count);
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
        Contract.Requires(start >= 0 && start < _count);

        int index = _value.IndexOf(ch, _offset + start);
        return index < 0 ? index : index - _offset;
    }

    public int indexOf(string target, int start = 0) => _value.IndexOf(target, StringComparison.Ordinal);

    public string ToString(int start)
    {
        Contract.Requires(start >= 0 && start < _count);

        return _value.Substring(_offset + start, _count);
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