/*
 * Copyright 2012 The Netty Project
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

using System.Diagnostics;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

/**
 * Default {@link IAttributeMap} implementation which not exibit any blocking behaviour on attribute lookup while using a
 * copy-on-write approach on the modify path.<br> Attributes lookup and remove exibit {@code O(logn)} time worst-case
 * complexity, hence {@code attribute::set(null)} is to be preferred to {@code remove}.
 */
public class DefaultAttributeMap : IAttributeMap
{
    private static readonly IDefaultAttribute[] EMPTY_ATTRIBUTES = new IDefaultAttribute[0];
    private readonly AtomicReference<IDefaultAttribute[]> _attributes = new AtomicReference<IDefaultAttribute[]>(EMPTY_ATTRIBUTES);

    /**
     * Similarly to {@code Arrays::binarySearch} it perform a binary search optimized for this use case, in order to
     * save polymorphic calls (on comparator side) and unnecessary class checks.
     */
    private static int searchAttributeByKey(IDefaultAttribute[] sortedAttributes, IAttributeKey key)
    {
        int low = 0;
        int high = sortedAttributes.Length - 1;

        while (low <= high)
        {
            int mid = low + high >>> 1;
            IDefaultAttribute midVal = sortedAttributes[mid];
            IAttributeKey midValKey = midVal.key();
            if (midValKey == key)
            {
                return mid;
            }

            int midValKeyId = midValKey.id();
            int keyId = key.id();
            Debug.Assert(midValKeyId != keyId);
            bool searchRight = midValKeyId < keyId;
            if (searchRight)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return -(low + 1);
    }

    private static void orderedCopyOnInsert(IDefaultAttribute[] sortedSrc, int srcLength, IDefaultAttribute[] copy,
        IDefaultAttribute toInsert)
    {
        // let's walk backward, because as a rule of thumb, toInsert.key.id() tends to be higher for new keys
        int id = toInsert.key().id();
        int i;
        for (i = srcLength - 1; i >= 0; i--)
        {
            IDefaultAttribute attribute = sortedSrc[i];
            Debug.Assert(attribute.key().id() != id);
            if (attribute.key().id() < id)
            {
                break;
            }

            copy[i + 1] = sortedSrc[i];
        }

        copy[i + 1] = toInsert;
        int toCopy = i + 1;
        if (toCopy > 0)
        {
            Arrays.arraycopy(sortedSrc, 0, copy, 0, toCopy);
        }
    }


    //@SuppressWarnings("unchecked")
    public IAttribute<T> attr<T>(AttributeKey<T> key) where T : class
    {
        ObjectUtil.checkNotNull(key, "key");
        DefaultAttribute<T> newAttribute = null;
        for (;;)
        {
            IDefaultAttribute[] attributes = _attributes.get();
            int index = searchAttributeByKey(attributes, key);
            IDefaultAttribute[] newAttributes;
            if (index >= 0)
            {
                DefaultAttribute<T> attribute = attributes[index] as DefaultAttribute<T>;
                Debug.Assert(attribute.key() == key);
                if (!attribute.isRemoved())
                {
                    return attribute;
                }

                // let's try replace the removed attribute with a new one
                if (newAttribute == null)
                {
                    newAttribute = new DefaultAttribute<T>(this, key);
                }

                int count = attributes.Length;
                newAttributes = Arrays.copyOf(attributes, count);
                newAttributes[index] = newAttribute;
            }
            else
            {
                if (newAttribute == null)
                {
                    newAttribute = new DefaultAttribute<T>(this, key);
                }

                int count = attributes.Length;
                newAttributes = new IDefaultAttribute[count + 1];
                orderedCopyOnInsert(attributes, count, newAttributes, newAttribute);
            }

            if (_attributes.compareAndSet(attributes, newAttributes))
            {
                return newAttribute;
            }
        }
    }

    public bool hasAttr<T>(AttributeKey<T> key) where T : class
    {
        ObjectUtil.checkNotNull(key, "key");
        return searchAttributeByKey(_attributes.get(), key) >= 0;
    }

    internal void removeAttributeIfMatch<T>(AttributeKey<T> key, DefaultAttribute<T> value) where T : class
    {
        for (;;)
        {
            IDefaultAttribute[] attributes = _attributes.get();
            int index = searchAttributeByKey(attributes, key);
            if (index < 0)
            {
                return;
            }

            IDefaultAttribute attribute = attributes[index];
            Debug.Assert(attribute.key() == key);
            if (attribute != value)
            {
                return;
            }

            int count = attributes.Length;
            int newCount = count - 1;
            IDefaultAttribute[] newAttributes =
                newCount == 0 ? EMPTY_ATTRIBUTES : new IDefaultAttribute[newCount];

            // perform 2 bulk copies
            Arrays.arraycopy(attributes, 0, newAttributes, 0, index);
            int remaining = count - index - 1;
            if (remaining > 0)
            {
                Arrays.arraycopy(attributes, index + 1, newAttributes, index, remaining);
            }

            if (_attributes.compareAndSet(attributes, newAttributes))
            {
                return;
            }
        }
    }
}