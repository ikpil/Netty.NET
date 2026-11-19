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

using System.Collections.Generic;

namespace Netty.NET.Handler.Codec;

/**
 * A concrete implementation of {@link DefaultHeaders} that allows for direct instantiation.
 * @param <K> the type of the header name.
 * @param <V> the type of the header value.
 */
// CLR: HashingStrategy<K> is IEqualityComparer<K>, and the NameValidator/ValueValidator interfaces are on the
// non-generic DefaultHeaders class (see DefaultHeaders.cs).
public sealed class DefaultHeadersImpl<K, V> : DefaultHeaders<K, V, DefaultHeadersImpl<K, V>>
    where K : class
    where V : class
{
    public DefaultHeadersImpl(IEqualityComparer<K> nameHashingStrategy,
            IValueConverter<V> valueConverter, DefaultHeaders.INameValidator<K> nameValidator)
        : base(nameHashingStrategy, valueConverter, nameValidator)
    {
    }

    public DefaultHeadersImpl(IEqualityComparer<K> nameHashingStrategy, IValueConverter<V> valueConverter,
                              DefaultHeaders.INameValidator<K> nameValidator, int arraySizeHint,
                              DefaultHeaders.IValueValidator<V> valueValidator)
        : base(nameHashingStrategy, valueConverter, nameValidator, arraySizeHint, valueValidator)
    {
    }
}
