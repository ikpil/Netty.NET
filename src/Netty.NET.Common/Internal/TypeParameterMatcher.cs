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

    internal TypeParameterMatcher() { }

    public static TypeParameterMatcher Get(Type parameterType)
    {
        ArgumentNullException.ThrowIfNull(parameterType);
        IDictionary<Type, TypeParameterMatcher> getCache =
            InternalThreadLocalMap.Get().TypeParameterMatcherGetCache();

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

    public static TypeParameterMatcher Find(object obj, Type parametrizedSuperclass, string typeParamName)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(parametrizedSuperclass);
        ArgumentNullException.ThrowIfNull(typeParamName);
        IDictionary<Type, IDictionary<(Type Superclass, string Name), TypeParameterMatcher>> findCache =
            InternalThreadLocalMap.Get().TypeParameterMatcherFindCache();
        Type thisClass = obj.GetType();

        findCache.TryGetValue(thisClass, out IDictionary<(Type Superclass, string Name), TypeParameterMatcher> map);
        if (map == null)
        {
            map = new Dictionary<(Type Superclass, string Name), TypeParameterMatcher>();
            findCache.Add(thisClass, map);
        }

        var key = (parametrizedSuperclass, typeParamName);
        map.TryGetValue(key, out TypeParameterMatcher matcher);
        if (matcher == null)
        {
            matcher = Get(ReflectionUtil.ResolveTypeParameter(obj, parametrizedSuperclass, typeParamName));
            map.Add(key, matcher);
        }

        return matcher;
    }

    public abstract bool Match(object msg);
}
