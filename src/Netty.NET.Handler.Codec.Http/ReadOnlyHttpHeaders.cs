/*
 * Copyright 2017 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.AsciiString;
using static Netty.NET.Handler.Codec.CharSequenceValueConverter;

namespace Netty.NET.Handler.Codec.Http;

/**
 * A variant of {@link HttpHeaders} which only supports read-only methods.
 * <p>
 * Any array passed to this class may be used directly in the underlying data structures of this class. If these
 * arrays may be modified it is the caller's responsibility to supply this class with a copy of the array.
 * <p>
 * This may be a good alternative to {@link DefaultHttpHeaders} if your have a fixed set of headers which will not
 * change.
 */
// CLR: @UnstableApi has no counterpart. The String overloads wrap their names and values in StringCharSequence
// because a C# string is not an ICharSequence. Map.Entry and Iterator are the codec-base IEntry and IIterator.
// NoSuchElementException and the read-only IllegalStateException of an iterator are InvalidOperationException,
// UnsupportedOperationException is NotSupportedException (the codec-base IIterator contract). The static import
// CharSequenceValueConverter.INSTANCE is the codec-base converter.
public sealed class ReadOnlyHttpHeaders : HttpHeaders
{
    private readonly ICharSequence[] nameValuePairs;

    /**
     * Create a new instance.
     * @param validateHeaders {@code true} to validate the contents of each header name.
     * @param nameValuePairs An array of the structure {@code [<name,value>,<name,value>,...]}.
     *                      A copy will <strong>NOT</strong> be made of this array. If the contents of this array
     *                      may be modified externally you are responsible for passing in a copy.
     */
    public ReadOnlyHttpHeaders(bool validateHeaders, params ICharSequence[] nameValuePairs)
    {
        if ((nameValuePairs.Length & 1) != 0)
        {
            throw NewInvalidArraySizeException();
        }

        if (validateHeaders)
        {
            ValidateHeaders(nameValuePairs);
        }

        this.nameValuePairs = nameValuePairs;
    }

    private static ArgumentException NewInvalidArraySizeException()
    {
        return new ArgumentException("nameValuePairs must be arrays of [name, value] pairs");
    }

    private static void ValidateHeaders(params ICharSequence[] keyValuePairs)
    {
        for (int i = 0; i < keyValuePairs.Length; i += 2)
        {
            DefaultHttpHeadersFactory.HeadersFactory().GetNameValidator().ValidateName(keyValuePairs[i]);
        }
    }

    private ICharSequence Get0(ICharSequence name)
    {
        int nameHash = AsciiString.HashCode(name);

        for (int i = 0; i < nameValuePairs.Length; i += 2)
        {
            ICharSequence roName = nameValuePairs[i];
            if (AsciiString.HashCode(roName) == nameHash && ContentEqualsIgnoreCase(roName, name))
            {
                // Suppress a warning out of bounds access since the constructor allows only pairs
                return nameValuePairs[i + 1];
            }
        }

        return null;
    }

    public override string Get(string name)
    {
        ICharSequence value = Get0(new StringCharSequence(name));
        return value == null ? null : value.ToString();
    }

    public override int? GetInt(ICharSequence name)
    {
        ICharSequence value = Get0(name);
        return value == null ? null : INSTANCE.ConvertToInt(value);
    }

    public override int GetInt(ICharSequence name, int defaultValue)
    {
        ICharSequence value = Get0(name);
        return value == null ? defaultValue : INSTANCE.ConvertToInt(value);
    }

    public override short? GetShort(ICharSequence name)
    {
        ICharSequence value = Get0(name);
        return value == null ? null : INSTANCE.ConvertToShort(value);
    }

    public override short GetShort(ICharSequence name, short defaultValue)
    {
        ICharSequence value = Get0(name);
        return value == null ? defaultValue : INSTANCE.ConvertToShort(value);
    }

    public override long? GetTimeMillis(ICharSequence name)
    {
        ICharSequence value = Get0(name);
        return value == null ? null : INSTANCE.ConvertToTimeMillis(value);
    }

    public override long GetTimeMillis(ICharSequence name, long defaultValue)
    {
        ICharSequence value = Get0(name);
        return value == null ? defaultValue : INSTANCE.ConvertToTimeMillis(value);
    }

    public override IList<string> GetAll(string name)
    {
        if (IsEmpty())
        {
            return Array.Empty<string>();
        }

        ICharSequence csName = new StringCharSequence(name);
        int nameHash = AsciiString.HashCode(csName);
        IList<string> values = new List<string>(4);

        for (int i = 0; i < nameValuePairs.Length; i += 2)
        {
            ICharSequence roName = nameValuePairs[i];
            if (AsciiString.HashCode(roName) == nameHash && ContentEqualsIgnoreCase(roName, csName))
            {
                values.Add(nameValuePairs[i + 1].ToString());
            }
        }

        return values;
    }

    public override IList<IEntry<string, string>> Entries()
    {
        if (IsEmpty())
        {
            return Array.Empty<IEntry<string, string>>();
        }

        IList<IEntry<string, string>> entries = new List<IEntry<string, string>>(Size());

        for (int i = 0; i < nameValuePairs.Length; i += 2)
        {
            entries.Add(new ImmutableEntry(nameValuePairs[i].ToString(),
                    nameValuePairs[i + 1].ToString())); // [java/index-out-of-bounds]
        }

        return entries;
    }

    public override bool Contains(string name)
    {
        return Get0(new StringCharSequence(name)) != null;
    }

    public override bool Contains(string name, string value, bool ignoreCase)
    {
        return ContainsValue(new StringCharSequence(name), new StringCharSequence(value), ignoreCase);
    }

    public override bool ContainsValue(ICharSequence name, ICharSequence value, bool ignoreCase)
    {
        if (ignoreCase)
        {
            for (int i = 0; i < nameValuePairs.Length; i += 2)
            {
                if (ContentEqualsIgnoreCase(nameValuePairs[i], name) &&
                        ContentEqualsIgnoreCase(nameValuePairs[i + 1], value))
                {
                    return true;
                }
            }
        }
        else
        {
            for (int i = 0; i < nameValuePairs.Length; i += 2)
            {
                if (ContentEqualsIgnoreCase(nameValuePairs[i], name) &&
                        ContentEquals(nameValuePairs[i + 1], value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public override IIterator<string> ValueStringIterator(ICharSequence name)
    {
        return new ReadOnlyStringValueIterator(this, name);
    }

    public override IIterator<ICharSequence> ValueCharSequenceIterator(ICharSequence name)
    {
        return new ReadOnlyValueIterator(this, name);
    }

    public override IIterator<IEntry<string, string>> IteratorAsString()
    {
        return new ReadOnlyStringIterator(this);
    }

    public override IIterator<IEntry<ICharSequence, ICharSequence>> IteratorCharSequence()
    {
        return new ReadOnlyIterator(this);
    }

    public override bool IsEmpty()
    {
        return nameValuePairs.Length == 0;
    }

    public override int Size()
    {
        return nameValuePairs.Length >>> 1;
    }

    public override ISet<string> Names()
    {
        if (IsEmpty())
        {
            return ImmutableHashSet<string>.Empty;
        }

        ISet<string> names = new LinkedHashSet<string>(Size());

        for (int i = 0; i < nameValuePairs.Length; i += 2)
        {
            names.Add(nameValuePairs[i].ToString());
        }

        return names;
    }

    public override HttpHeaders Add(string name, object value)
    {
        throw new NotSupportedException("read only");
    }

    public override HttpHeaders Add(string name, IEnumerable<object> values)
    {
        throw new NotSupportedException("read only");
    }

    public override HttpHeaders AddInt(ICharSequence name, int value)
    {
        throw new NotSupportedException("read only");
    }

    public override HttpHeaders AddShort(ICharSequence name, short value)
    {
        throw new NotSupportedException("read only");
    }

    public override HttpHeaders Set(string name, object value)
    {
        throw new NotSupportedException("read only");
    }

    public override HttpHeaders Set(string name, IEnumerable<object> values)
    {
        throw new NotSupportedException("read only");
    }

    public override HttpHeaders SetInt(ICharSequence name, int value)
    {
        throw new NotSupportedException("read only");
    }

    public override HttpHeaders SetShort(ICharSequence name, short value)
    {
        throw new NotSupportedException("read only");
    }

    public override HttpHeaders Remove(string name)
    {
        throw new NotSupportedException("read only");
    }

    public override HttpHeaders Clear()
    {
        throw new NotSupportedException("read only");
    }

    // CLR: java.util.AbstractMap.SimpleImmutableEntry<String, String> (equals/hashCode/toString of Map.Entry).
    private sealed class ImmutableEntry : IEntry<string, string>
    {
        private readonly string key;
        private readonly string value;

        internal ImmutableEntry(string key, string value)
        {
            this.key = key;
            this.value = value;
        }

        public string GetKey()
        {
            return key;
        }

        public string GetValue()
        {
            return value;
        }

        public string SetValue(string value)
        {
            throw new NotSupportedException();
        }

        public override bool Equals(object o)
        {
            return o is IEntry<string, string> e && Equals(key, e.GetKey()) && Equals(value, e.GetValue());
        }

        public override int GetHashCode()
        {
            return (key == null ? 0 : key.GetHashCode()) ^ (value == null ? 0 : value.GetHashCode());
        }

        public override string ToString()
        {
            return key + '=' + value;
        }
    }

    // CLR: the inner classes read ReadOnlyHttpHeaders.this.nameValuePairs, which they receive instead. MoveNext()/Current
    // are the IEnumerator<T> form of hasNext()/next() (the codec-base IIterator).
    private sealed class ReadOnlyIterator : IEntry<ICharSequence, ICharSequence>,
            IIterator<IEntry<ICharSequence, ICharSequence>>
    {
        private readonly ICharSequence[] nameValuePairs;
        private ICharSequence key;
        private ICharSequence value;
        private int nextNameIndex;
        private bool started;

        internal ReadOnlyIterator(ReadOnlyHttpHeaders headers)
        {
            nameValuePairs = headers.nameValuePairs;
        }

        public bool HasNext()
        {
            return nextNameIndex != nameValuePairs.Length;
        }

        public IEntry<ICharSequence, ICharSequence> Next()
        {
            if (!HasNext())
            {
                throw new InvalidOperationException();
            }

            key = nameValuePairs[nextNameIndex];
            value = nameValuePairs[nextNameIndex + 1];
            nextNameIndex += 2;
            started = true;
            return this;
        }

        public void Remove()
        {
            throw new NotSupportedException("read only");
        }

        public ICharSequence GetKey()
        {
            return key;
        }

        public ICharSequence GetValue()
        {
            return value;
        }

        public ICharSequence SetValue(ICharSequence value)
        {
            throw new NotSupportedException("read only");
        }

        public override string ToString()
        {
            return key.ToString() + '=' + value.ToString();
        }

        public IEntry<ICharSequence, ICharSequence> Current => started ? this : null;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (!HasNext())
            {
                return false;
            }

            Next();
            return true;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }

    private sealed class ReadOnlyStringIterator : IEntry<string, string>,
            IIterator<IEntry<string, string>>
    {
        private readonly ICharSequence[] nameValuePairs;
        private string key;
        private string value;
        private int nextNameIndex;
        private bool started;

        internal ReadOnlyStringIterator(ReadOnlyHttpHeaders headers)
        {
            nameValuePairs = headers.nameValuePairs;
        }

        public bool HasNext()
        {
            return nextNameIndex != nameValuePairs.Length;
        }

        public IEntry<string, string> Next()
        {
            if (!HasNext())
            {
                throw new InvalidOperationException();
            }

            key = nameValuePairs[nextNameIndex].ToString();
            value = nameValuePairs[nextNameIndex + 1].ToString();
            nextNameIndex += 2;
            started = true;
            return this;
        }

        public void Remove()
        {
            throw new NotSupportedException("read only");
        }

        public string GetKey()
        {
            return key;
        }

        public string GetValue()
        {
            return value;
        }

        public string SetValue(string value)
        {
            throw new NotSupportedException("read only");
        }

        public override string ToString()
        {
            return key + '=' + value;
        }

        public IEntry<string, string> Current => started ? this : null;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (!HasNext())
            {
                return false;
            }

            Next();
            return true;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }

    private sealed class ReadOnlyStringValueIterator : IIterator<string>
    {
        private readonly ICharSequence[] nameValuePairs;
        private readonly ICharSequence name;
        private readonly int nameHash;
        private int nextNameIndex;
        private string current;

        internal ReadOnlyStringValueIterator(ReadOnlyHttpHeaders headers, ICharSequence name)
        {
            nameValuePairs = headers.nameValuePairs;
            this.name = name;
            nameHash = AsciiString.HashCode(name);
            nextNameIndex = FindNextValue();
        }

        public bool HasNext()
        {
            return nextNameIndex != -1;
        }

        public string Next()
        {
            if (!HasNext())
            {
                throw new InvalidOperationException();
            }

            string value = nameValuePairs[nextNameIndex + 1].ToString();
            nextNameIndex = FindNextValue();
            return value;
        }

        public void Remove()
        {
            throw new NotSupportedException("read only");
        }

        private int FindNextValue()
        {
            for (int i = nextNameIndex; i < nameValuePairs.Length; i += 2)
            {
                ICharSequence roName = nameValuePairs[i];
                if (nameHash == AsciiString.HashCode(roName) && ContentEqualsIgnoreCase(name, roName))
                {
                    return i;
                }
            }

            return -1;
        }

        public string Current => current;

        object IEnumerator.Current => current;

        public bool MoveNext()
        {
            if (!HasNext())
            {
                return false;
            }

            current = Next();
            return true;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }

    private sealed class ReadOnlyValueIterator : IIterator<ICharSequence>
    {
        private readonly ICharSequence[] nameValuePairs;
        private readonly ICharSequence name;
        private readonly int nameHash;
        private int nextNameIndex;
        private ICharSequence current;

        internal ReadOnlyValueIterator(ReadOnlyHttpHeaders headers, ICharSequence name)
        {
            nameValuePairs = headers.nameValuePairs;
            this.name = name;
            nameHash = AsciiString.HashCode(name);
            nextNameIndex = FindNextValue();
        }

        public bool HasNext()
        {
            return nextNameIndex != -1;
        }

        public ICharSequence Next()
        {
            if (!HasNext())
            {
                throw new InvalidOperationException();
            }

            ICharSequence value = nameValuePairs[nextNameIndex + 1];
            nextNameIndex = FindNextValue();
            return value;
        }

        public void Remove()
        {
            throw new NotSupportedException("read only");
        }

        private int FindNextValue()
        {
            for (int i = nextNameIndex; i < nameValuePairs.Length; i += 2)
            {
                ICharSequence roName = nameValuePairs[i];
                if (nameHash == AsciiString.HashCode(roName) && ContentEqualsIgnoreCase(name, roName))
                {
                    return i;
                }
            }

            return -1;
        }

        public ICharSequence Current => current;

        object IEnumerator.Current => current;

        public bool MoveNext()
        {
            if (!HasNext())
            {
                return false;
            }

            current = Next();
            return true;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }
}
