/*
 * Copyright 2015 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using Netty.NET.Common.Internal;
using static Netty.NET.Common.AsciiString;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Internal use only!
 */
// CLR: DefaultHeaders<CharSequence, V, CharSequenceMap<V>> is the codec-base DefaultHeaders<ICharSequence, V,
// CharSequenceMap<V>>, whose value type must be a reference type (V : class).
public sealed class CharSequenceMap<V> : DefaultHeaders<ICharSequence, V, CharSequenceMap<V>>
    where V : class
{
    public CharSequenceMap() : this(true)
    {
    }

    public CharSequenceMap(bool caseSensitive) : this(caseSensitive, UnsupportedValueConverter<V>.Instance())
    {
    }

    public CharSequenceMap(bool caseSensitive, IValueConverter<V> valueConverter)
        : base(caseSensitive ? CASE_SENSITIVE_HASHER : CASE_INSENSITIVE_HASHER, valueConverter)
    {
    }

    public CharSequenceMap(bool caseSensitive, IValueConverter<V> valueConverter, int arraySizeHint)
        : base(caseSensitive ? CASE_SENSITIVE_HASHER : CASE_INSENSITIVE_HASHER, valueConverter,
                DefaultHeaders.INameValidator<ICharSequence>.NOT_NULL, arraySizeHint)
    {
    }
}
