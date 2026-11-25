/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http;

// CLR: HttpHeaders.EMPTY_HEADERS is @Deprecated and not ported (its @deprecated text names EmptyHttpHeaders.INSTANCE),
// so there is no cyclic static initialization to break: the @Deprecated instance() (whose @deprecated text names
// INSTANCE) and InstanceInitializer, which only serve that cycle, are not ported (owner decision 2026-11-16), and
// INSTANCE is created directly. The empty lists and set are immutable (Array.Empty, ImmutableHashSet, as
// Collections.emptyList/emptySet are), and EMPTY_CHARS_ITERATOR is the private nested EmptyIterator, an IIterator with
// no elements (the codec-base IIterator for java.util.Iterator). iterator() is IteratorAsString() (see HttpHeaders).
// UnsupportedOperationException is NotSupportedException (docs/codec-base-porting.md).
public class EmptyHttpHeaders : HttpHeaders
{
    internal static readonly IIterator<IEntry<ICharSequence, ICharSequence>> EMPTY_CHARS_ITERATOR =
            new EmptyIterator<IEntry<ICharSequence, ICharSequence>>();

    public static readonly EmptyHttpHeaders INSTANCE = new EmptyHttpHeaders();

    protected EmptyHttpHeaders()
    {
    }

    public override string Get(string name)
    {
        return null;
    }

    public override int? GetInt(ICharSequence name)
    {
        return null;
    }

    public override int GetInt(ICharSequence name, int defaultValue)
    {
        return defaultValue;
    }

    public override short? GetShort(ICharSequence name)
    {
        return null;
    }

    public override short GetShort(ICharSequence name, short defaultValue)
    {
        return defaultValue;
    }

    public override long? GetTimeMillis(ICharSequence name)
    {
        return null;
    }

    public override long GetTimeMillis(ICharSequence name, long defaultValue)
    {
        return defaultValue;
    }

    public override IList<string> GetAll(string name)
    {
        return Array.Empty<string>();
    }

    public override IList<IEntry<string, string>> Entries()
    {
        return Array.Empty<IEntry<string, string>>();
    }

    public override bool Contains(string name)
    {
        return false;
    }

    public override bool IsEmpty()
    {
        return true;
    }

    public override int Size()
    {
        return 0;
    }

    public override ISet<string> Names()
    {
        return ImmutableHashSet<string>.Empty;
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

    public override IIterator<IEntry<string, string>> IteratorAsString()
    {
        return new EmptyIterator<IEntry<string, string>>();
    }

    public override IIterator<IEntry<ICharSequence, ICharSequence>> IteratorCharSequence()
    {
        return EMPTY_CHARS_ITERATOR;
    }

    // CLR: Collections.emptyList().iterator().
    private sealed class EmptyIterator<E> : IIterator<E>
    {
        public bool HasNext()
        {
            return false;
        }

        public E Next()
        {
            throw new InvalidOperationException();
        }

        public void Remove()
        {
            throw new InvalidOperationException();
        }

        public E Current => default;

        object IEnumerator.Current => default(E);

        public bool MoveNext()
        {
            return false;
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }
}
