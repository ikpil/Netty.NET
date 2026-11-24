/*
 * Copyright 2020 The Netty Project
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
using System.Net;

namespace Netty.NET.Handler.IpFilter;

/**
 * This comparator is only used for searching.
 */
// CLR: Comparator<Object> is IComparer<object>. Arrays.binarySearch(a, key, c) and Array.BinarySearch<object>(array,
// value, comparer) both pass the array element first: the CLR search calls "comparer.Compare(array[i], value)" and
// returns the complement of the insertion point when the value is not found (ArraySortHelper.InternalBinarySearch,
// https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/ArraySortHelper.cs),
// so o1 is the IpSubnetFilterRule and o2 the IPEndPoint, as in Java.
internal sealed class IpSubnetFilterRuleComparator : IComparer<object>
{
    internal static readonly IpSubnetFilterRuleComparator INSTANCE = new IpSubnetFilterRuleComparator();

    private IpSubnetFilterRuleComparator()
    {
        // Prevent outside initialization
    }

    public int Compare(object o1, object o2)
    {
        return ((IpSubnetFilterRule) o1).CompareTo((IPEndPoint) o2);
    }
}
