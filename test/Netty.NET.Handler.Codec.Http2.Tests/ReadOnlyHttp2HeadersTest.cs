/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.Tests.DefaultHttp2HeadersTest;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: a Java String passed as a CharSequence is a StringCharSequence built by the private Cs(string) (a C# string is
// not an ICharSequence; docs/codec-http-porting.md, Shared CLR adaptations). assertThrows(T.class) is
// Assert.ThrowsAny<T> with the CLR exception: IllegalArgumentException is ArgumentException,
// UnsupportedOperationException is NotSupportedException and NoSuchElementException is InvalidOperationException (the
// codec-base IIterator contract, docs/codec-base-porting.md, Headers). Iterator and Map.Entry are the codec-base
// IIterator and IEntry; iterator()/valueIterator are Iterator()/ValueIterator.
public class ReadOnlyHttp2HeadersTest
{
    [Fact]
    public void NotKeyValuePairThrows()
    {
        Assert.ThrowsAny<ArgumentException>(() => ReadOnlyHttp2Headers.Trailers(false, new AsciiString[] { null }));
    }

    // CLR: Java's NullPointerException comes from otherHeaders.length on the null array (a dereference, not a
    // checkNotNull), whose CLR counterpart is NullReferenceException (docs/codec-http-porting.md, Core test notes).
    [Fact]
    public void NullTrailersNotAllowed()
    {
        Assert.ThrowsAny<NullReferenceException>(() => ReadOnlyHttp2Headers.Trailers(false, (AsciiString[]) null));
    }

    [Fact]
    public void NullHeaderNameNotChecked()
    {
        ReadOnlyHttp2Headers.Trailers(false, null, null);
    }

    [Fact]
    public void NullHeaderNameValidated()
    {
        Assert.ThrowsAny<Http2Exception>(() => ReadOnlyHttp2Headers.Trailers(true, null, new AsciiString("foo")));
    }

    [Fact]
    public void PseudoHeaderNotAllowedAfterNonPseudoHeaders()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
                ReadOnlyHttp2Headers.Trailers(true, new AsciiString(":scheme"), new AsciiString("foo"),
                        new AsciiString("othername"), new AsciiString("goo"),
                        new AsciiString(":path"), new AsciiString("val")));
    }

    [Fact]
    public void NullValuesAreNotAllowed()
    {
        Assert.ThrowsAny<ArgumentException>(() => ReadOnlyHttp2Headers.Trailers(true, new AsciiString("foo"), null));
    }

    [Fact]
    public void IllegalValuesAreNotAllowed()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => ReadOnlyHttp2Headers.Trailers(true, new AsciiString("foo"), new AsciiString("ba\nr")));
    }

    [Fact]
    public void EmptyHeaderNameAllowed()
    {
        ReadOnlyHttp2Headers.Trailers(false, AsciiString.EMPTY_STRING, new AsciiString("foo"));
    }

    [Fact]
    public void TestPseudoHeadersMustComeFirstWhenIteratingServer()
    {
        IHttp2Headers headers = NewServerHeaders();
        VerifyPseudoHeadersFirst(headers);
    }

    [Fact]
    public void TestPseudoHeadersMustComeFirstWhenIteratingClient()
    {
        IHttp2Headers headers = NewClientHeaders();
        VerifyPseudoHeadersFirst(headers);
    }

    [Fact]
    public void TestIteratorReadOnlyClient()
    {
        Assert.ThrowsAny<NotSupportedException>(() => TestIteratorReadOnly(NewClientHeaders()));
    }

    [Fact]
    public void TestIteratorReadOnlyServer()
    {
        Assert.ThrowsAny<NotSupportedException>(() => TestIteratorReadOnly(NewServerHeaders()));
    }

    [Fact]
    public void TestIteratorReadOnlyTrailers()
    {
        Assert.ThrowsAny<NotSupportedException>(() => TestIteratorReadOnly(NewTrailers()));
    }

    [Fact]
    public void TestIteratorEntryReadOnlyClient()
    {
        Assert.ThrowsAny<NotSupportedException>(() => TestIteratorEntryReadOnly(NewClientHeaders()));
    }

    [Fact]
    public void TestIteratorEntryReadOnlyServer()
    {
        Assert.ThrowsAny<NotSupportedException>(() => TestIteratorEntryReadOnly(NewServerHeaders()));
    }

    [Fact]
    public void TestIteratorEntryReadOnlyTrailers()
    {
        Assert.ThrowsAny<NotSupportedException>(() => TestIteratorEntryReadOnly(NewTrailers()));
    }

    [Fact]
    public void TestSize()
    {
        IHttp2Headers headers = NewTrailers();
        Assert.Equal(OtherHeaders().Length / 2, headers.Size());
    }

    [Fact]
    public void TestIsNotEmpty()
    {
        IHttp2Headers headers = NewTrailers();
        Assert.False(headers.IsEmpty());
    }

    [Fact]
    public void TestIsEmpty()
    {
        IHttp2Headers headers = ReadOnlyHttp2Headers.Trailers(false);
        Assert.True(headers.IsEmpty());
    }

    [Fact]
    public void TestContainsName()
    {
        IHttp2Headers headers = NewClientHeaders();
        Assert.True(headers.Contains(Cs("Name1")));
        Assert.True(headers.Contains(IHttp2Headers.PseudoHeaderName.PATH.Value()));
        Assert.False(headers.Contains(IHttp2Headers.PseudoHeaderName.STATUS.Value()));
        Assert.False(headers.Contains(Cs("a missing header")));
    }

    [Fact]
    public void TestContainsNameAndValue()
    {
        IHttp2Headers headers = NewClientHeaders();
        Assert.True(headers.Contains(Cs("Name1"), Cs("value1")));
        Assert.False(headers.Contains(Cs("Name1"), Cs("Value1")));
        Assert.True(headers.Contains(Cs("name2"), Cs("Value2"), true));
        Assert.False(headers.Contains(Cs("name2"), Cs("Value2"), false));
        Assert.True(headers.Contains(IHttp2Headers.PseudoHeaderName.PATH.Value(), Cs("/foo")));
        Assert.False(headers.Contains(IHttp2Headers.PseudoHeaderName.STATUS.Value(), Cs("200")));
        Assert.False(headers.Contains(Cs("a missing header"), Cs("a missing value")));
    }

    [Fact]
    public void TestGet()
    {
        IHttp2Headers headers = NewClientHeaders();
        Assert.True(AsciiString.ContentEqualsIgnoreCase(Cs("value1"), headers.Get(Cs("Name1"))));
        Assert.True(AsciiString.ContentEqualsIgnoreCase(Cs("/foo"),
                   headers.Get(IHttp2Headers.PseudoHeaderName.PATH.Value())));
        Assert.Null(headers.Get(IHttp2Headers.PseudoHeaderName.STATUS.Value()));
        Assert.Null(headers.Get(Cs("a missing header")));
    }

    [Fact]
    public void TestClientOtherValueIterator()
    {
        TestValueIteratorSingleValue(NewClientHeaders(), Cs("name2"), Cs("value2"));
    }

    [Fact]
    public void TestClientPsuedoValueIterator()
    {
        TestValueIteratorSingleValue(NewClientHeaders(), Cs(":path"), Cs("/foo"));
    }

    [Fact]
    public void TestServerPsuedoValueIterator()
    {
        TestValueIteratorSingleValue(NewServerHeaders(), Cs(":status"), Cs("200"));
    }

    [Fact]
    public void TestEmptyValueIterator()
    {
        IHttp2Headers headers = NewServerHeaders();
        IIterator<ICharSequence> itr = headers.ValueIterator(Cs("foo"));
        Assert.False(itr.HasNext());
        Assert.ThrowsAny<InvalidOperationException>(() => itr.Next());
    }

    [Fact]
    public void TestIteratorMultipleValues()
    {
        IHttp2Headers headers = ReadOnlyHttp2Headers.ServerHeaders(false, new AsciiString("200"),
                new AsciiString("name2"), new AsciiString("value1"),
                new AsciiString("name1"), new AsciiString("value2"),
                new AsciiString("name2"), new AsciiString("value3"));
        IIterator<ICharSequence> itr = headers.ValueIterator(Cs("name2"));
        Assert.True(itr.HasNext());
        Assert.True(AsciiString.ContentEqualsIgnoreCase(Cs("value1"), itr.Next()));
        Assert.True(itr.HasNext());
        Assert.True(AsciiString.ContentEqualsIgnoreCase(Cs("value3"), itr.Next()));
        Assert.False(itr.HasNext());
    }

    private static void TestValueIteratorSingleValue(IHttp2Headers headers, ICharSequence name, ICharSequence value)
    {
        IIterator<ICharSequence> itr = headers.ValueIterator(name);
        Assert.True(itr.HasNext());
        Assert.True(AsciiString.ContentEqualsIgnoreCase(value, itr.Next()));
        Assert.False(itr.HasNext());
    }

    private static void TestIteratorReadOnly(IHttp2Headers headers)
    {
        IIterator<IEntry<ICharSequence, ICharSequence>> itr = headers.Iterator();
        Assert.True(itr.HasNext());
        itr.Remove();
    }

    private static void TestIteratorEntryReadOnly(IHttp2Headers headers)
    {
        IIterator<IEntry<ICharSequence, ICharSequence>> itr = headers.Iterator();
        Assert.True(itr.HasNext());
        itr.Next().SetValue(Cs("foo"));
    }

    private static ReadOnlyHttp2Headers NewServerHeaders()
    {
        return ReadOnlyHttp2Headers.ServerHeaders(false, new AsciiString("200"), OtherHeaders());
    }

    private static ReadOnlyHttp2Headers NewClientHeaders()
    {
        return ReadOnlyHttp2Headers.ClientHeaders(false, new AsciiString("meth"), new AsciiString("/foo"),
                new AsciiString("schemer"), new AsciiString("respect_my_authority"), OtherHeaders());
    }

    private static ReadOnlyHttp2Headers NewTrailers()
    {
        return ReadOnlyHttp2Headers.Trailers(false, OtherHeaders());
    }

    private static AsciiString[] OtherHeaders()
    {
        return new AsciiString[] {
                new AsciiString("name1"), new AsciiString("value1"),
                new AsciiString("name2"), new AsciiString("value2"),
                new AsciiString("name3"), new AsciiString("value3")
        };
    }

    // CLR: a Java String passed as a CharSequence (see the class comment).
    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }
}
