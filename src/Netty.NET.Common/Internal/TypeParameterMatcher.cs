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

using System;
using System.Collections.Generic;

namespace Netty.NET.Common.Internal;

public abstract class TypeParameterMatcher
{
    private static readonly TypeParameterMatcher NOOP = new NoopTypeParameterMatcher();

    public static TypeParameterMatcher get(Type parameterType)
    {
        IDictionary<Type, TypeParameterMatcher> getCache =
            InternalThreadLocalMap.get().typeParameterMatcherGetCache();

        getCache.TryGetValue(parameterType, out TypeParameterMatcher matcher);
        if (matcher == null)
        {
            if (parameterType == typeof(object))
            {
                matcher = NOOP;
            }
            else
            {
                matcher = new ReflectiveMatcher(parameterType);
            }

            getCache.Add(parameterType, matcher);
        }

        return matcher;
    }

    public static TypeParameterMatcher find(object obj, Type parametrizedSuperclass, string typeParamName)
    {
        IDictionary<Type, IDictionary<string, TypeParameterMatcher>> findCache =
            InternalThreadLocalMap.get().typeParameterMatcherFindCache();
        Type thisClass = obj.GetType();

        findCache.TryGetValue(thisClass, out IDictionary<string, TypeParameterMatcher> map);
        if (map == null)
        {
            map = new Dictionary<string, TypeParameterMatcher>();
            findCache.Add(thisClass, map);
        }

        map.TryGetValue(typeParamName, out TypeParameterMatcher matcher);
        if (matcher == null)
        {
            matcher = get(ReflectionUtil.resolveTypeParameter(obj, parametrizedSuperclass, typeParamName));
            map.Add(typeParamName, matcher);
        }

        return matcher;
    }

    public abstract bool match(object msg);
}