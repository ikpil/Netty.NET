/*
 * Copyright 2013 The Netty Project
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

using System.Collections.Generic;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Spdy;

// CLR: CharSequence is ICharSequence and the codec-base DefaultHeaders<K, V, T> and HeadersUtils keep their Java
// members (docs/codec-base-porting.md, Headers), as codec-stomp's DefaultStompHeaders; the static imports
// AsciiString.CASE_SENSITIVE_HASHER and CASE_INSENSITIVE_HASHER are the Common AsciiString IEqualityComparer fields.
// NameValidator<CharSequence> is DefaultHeaders.INameValidator<ICharSequence>, whose NOT_NULL constant needs no
// unchecked cast; the anonymous SpdyNameValidator is the private nested SpdyNameValidator_ class behind the field of
// the Java name. contains(name, value) overrides the virtual DefaultHeaders.Contains(K, V). The nested INSTANCE hides
// CharSequenceValueConverter.INSTANCE as the Java field does (new modifier,
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/new-modifier).
public class DefaultSpdyHeaders : DefaultHeaders<ICharSequence, ICharSequence, ISpdyHeaders>, ISpdyHeaders
{
    private static readonly DefaultHeaders.INameValidator<ICharSequence> SpdyNameValidator = new SpdyNameValidator_();

    private sealed class SpdyNameValidator_ : DefaultHeaders.INameValidator<ICharSequence>
    {
        public void ValidateName(ICharSequence name)
        {
            SpdyCodecUtil.ValidateHeaderName(name);
        }
    }

    public DefaultSpdyHeaders() : this(true)
    {
    }

    public DefaultSpdyHeaders(bool validate)
        : base(AsciiString.CASE_INSENSITIVE_HASHER,
                validate ? HeaderValueConverterAndValidator.INSTANCE : CharSequenceValueConverter.INSTANCE,
                validate ? SpdyNameValidator : DefaultHeaders.INameValidator<ICharSequence>.NOT_NULL)
    {
    }

    public string GetAsString(ICharSequence name)
    {
        return HeadersUtils.GetAsString(this, name);
    }

    public IList<string> GetAllAsString(ICharSequence name)
    {
        return HeadersUtils.GetAllAsString(this, name);
    }

    public IIterator<IEntry<string, string>> IteratorAsString()
    {
        return HeadersUtils.IteratorAsString(this);
    }

    public override bool Contains(ICharSequence name, ICharSequence value)
    {
        return Contains(name, value, false);
    }

    public bool Contains(ICharSequence name, ICharSequence value, bool ignoreCase)
    {
        return Contains(name, value,
                ignoreCase ? AsciiString.CASE_INSENSITIVE_HASHER : AsciiString.CASE_SENSITIVE_HASHER);
    }

    private sealed class HeaderValueConverterAndValidator : CharSequenceValueConverter
    {
        public new static readonly HeaderValueConverterAndValidator INSTANCE = new HeaderValueConverterAndValidator();

        public override ICharSequence ConvertObject(object value)
        {
            ICharSequence seq = base.ConvertObject(value);
            SpdyCodecUtil.ValidateHeaderValue(seq);
            return seq;
        }
    }
}
