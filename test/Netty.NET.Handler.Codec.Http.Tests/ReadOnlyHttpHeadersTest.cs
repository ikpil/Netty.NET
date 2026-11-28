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
using System.Collections.Generic;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpHeaderValues;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: the Strings that the headers return are compared through StringCharSequence, because a C# string is not an
// ICharSequence. HttpHeaders.iterator() is @Deprecated and names iteratorAsString() as its replacement, so it is
// not ported and stringIterator() uses IteratorAsString() (rule 7). Map.Entry is the codec-base IEntry;
// assert3ParisEquals takes the IEnumerator of the entries (the IIterator of IteratorAsString() and the enumerator
// of entries(), whose List.iterator() is the IList enumerator), and hasNext()/next() are MoveNext()/Current.
// IllegalArgumentException is ArgumentException.
public class ReadOnlyHttpHeadersTest
{
    [Fact]
    public void GetValue()
    {
        ReadOnlyHttpHeaders headers = new ReadOnlyHttpHeaders(true,
                ACCEPT, APPLICATION_JSON);
        Assert.False(headers.IsEmpty());
        Assert.Equal(1, headers.Size());
        Assert.True(APPLICATION_JSON.ContentEquals(new StringCharSequence(headers.Get(ACCEPT))));
        Assert.True(headers.Contains(ACCEPT));
        Assert.Null(headers.Get(CONTENT_LENGTH));
        Assert.False(headers.Contains(CONTENT_LENGTH));
    }

    [Fact]
    public void CharSequenceIterator()
    {
        ReadOnlyHttpHeaders headers = new ReadOnlyHttpHeaders(true,
                ACCEPT, APPLICATION_JSON, CONTENT_LENGTH, ZERO, CONNECTION, CLOSE);
        Assert.False(headers.IsEmpty());
        Assert.Equal(3, headers.Size());
        IIterator<IEntry<ICharSequence, ICharSequence>> itr = headers.IteratorCharSequence();
        Assert.True(itr.HasNext());
        IEntry<ICharSequence, ICharSequence> next = itr.Next();
        Assert.True(ACCEPT.ContentEqualsIgnoreCase(next.GetKey()));
        Assert.True(APPLICATION_JSON.ContentEqualsIgnoreCase(next.GetValue()));
        Assert.True(itr.HasNext());
        next = itr.Next();
        Assert.True(CONTENT_LENGTH.ContentEqualsIgnoreCase(next.GetKey()));
        Assert.True(ZERO.ContentEqualsIgnoreCase(next.GetValue()));
        Assert.True(itr.HasNext());
        next = itr.Next();
        Assert.True(CONNECTION.ContentEqualsIgnoreCase(next.GetKey()));
        Assert.True(CLOSE.ContentEqualsIgnoreCase(next.GetValue()));
        Assert.False(itr.HasNext());
    }

    [Fact]
    public void StringIterator()
    {
        ReadOnlyHttpHeaders headers = new ReadOnlyHttpHeaders(true,
                ACCEPT, APPLICATION_JSON, CONTENT_LENGTH, ZERO, CONNECTION, CLOSE);
        Assert.False(headers.IsEmpty());
        Assert.Equal(3, headers.Size());
        Assert3ParisEquals(headers.IteratorAsString());
    }

    [Fact]
    public void Entries()
    {
        ReadOnlyHttpHeaders headers = new ReadOnlyHttpHeaders(true,
                ACCEPT, APPLICATION_JSON, CONTENT_LENGTH, ZERO, CONNECTION, CLOSE);
        Assert.False(headers.IsEmpty());
        Assert.Equal(3, headers.Size());
        Assert3ParisEquals(headers.Entries().GetEnumerator());
    }

    [Fact]
    public void Names()
    {
        ReadOnlyHttpHeaders headers = new ReadOnlyHttpHeaders(true,
                ACCEPT, APPLICATION_JSON, CONTENT_LENGTH, ZERO, CONNECTION, CLOSE);
        Assert.False(headers.IsEmpty());
        Assert.Equal(3, headers.Size());
        ISet<string> names = headers.Names();
        Assert.Equal(3, names.Count);
        Assert.True(names.Contains(ACCEPT.ToString()));
        Assert.True(names.Contains(CONTENT_LENGTH.ToString()));
        Assert.True(names.Contains(CONNECTION.ToString()));
    }

    [Fact]
    public void GetAll()
    {
        ReadOnlyHttpHeaders headers = new ReadOnlyHttpHeaders(false,
                ACCEPT, APPLICATION_JSON, CONTENT_LENGTH, ZERO, ACCEPT, APPLICATION_OCTET_STREAM);
        Assert.False(headers.IsEmpty());
        Assert.Equal(3, headers.Size());
        IList<string> names = headers.GetAll(ACCEPT);
        Assert.Equal(2, names.Count);
        Assert.True(APPLICATION_JSON.ContentEqualsIgnoreCase(new StringCharSequence(names[0])));
        Assert.True(APPLICATION_OCTET_STREAM.ContentEqualsIgnoreCase(new StringCharSequence(names[1])));
    }

    [Fact]
    public void ValidateNamesFail()
    {
        Assert.Throws<ArgumentException>(() => new ReadOnlyHttpHeaders(true,
                ACCEPT, APPLICATION_JSON, AsciiString.Cached(" ")));
    }

    [Fact]
    public void EmptyHeaderName()
    {
        Assert.Throws<ArgumentException>(() => new ReadOnlyHttpHeaders(true,
                ACCEPT, APPLICATION_JSON, AsciiString.Cached(" "), ZERO));
    }

    [Fact]
    public void HeaderWithoutValue()
    {
        Assert.Throws<ArgumentException>(() => new ReadOnlyHttpHeaders(false,
                ACCEPT, APPLICATION_JSON, CONTENT_LENGTH));
    }

    private static void Assert3ParisEquals(IEnumerator<IEntry<string, string>> itr)
    {
        Assert.True(itr.MoveNext());
        IEntry<string, string> next = itr.Current;
        Assert.True(ACCEPT.ContentEqualsIgnoreCase(new StringCharSequence(next.GetKey())));
        Assert.True(APPLICATION_JSON.ContentEqualsIgnoreCase(new StringCharSequence(next.GetValue())));
        Assert.True(itr.MoveNext());
        next = itr.Current;
        Assert.True(CONTENT_LENGTH.ContentEqualsIgnoreCase(new StringCharSequence(next.GetKey())));
        Assert.True(ZERO.ContentEqualsIgnoreCase(new StringCharSequence(next.GetValue())));
        Assert.True(itr.MoveNext());
        next = itr.Current;
        Assert.True(CONNECTION.ContentEqualsIgnoreCase(new StringCharSequence(next.GetKey())));
        Assert.True(CLOSE.ContentEqualsIgnoreCase(new StringCharSequence(next.GetValue())));
        Assert.False(itr.MoveNext());
    }
}
