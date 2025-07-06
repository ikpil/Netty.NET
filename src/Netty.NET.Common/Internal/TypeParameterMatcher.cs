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

namespace Netty.NET.Common.Internal;




public abstract class TypeParameterMatcher {

    private static readonly TypeParameterMatcher NOOP = new TypeParameterMatcher() {
        @Override
        public bool match(object msg) {
            return true;
        }
    };

    public static TypeParameterMatcher get(final Class<?> parameterType) {
        final Map<Class<?>, TypeParameterMatcher> getCache =
                InternalThreadLocalMap.get().typeParameterMatcherGetCache();

        TypeParameterMatcher matcher = getCache.get(parameterType);
        if (matcher == null) {
            if (parameterType == object.class) {
                matcher = NOOP;
            } else {
                matcher = new ReflectiveMatcher(parameterType);
            }
            getCache.put(parameterType, matcher);
        }

        return matcher;
    }

    public static TypeParameterMatcher find(
            final object object, final Class<?> parametrizedSuperclass, final string typeParamName) {

        final Map<Class<?>, Map<string, TypeParameterMatcher>> findCache =
                InternalThreadLocalMap.get().typeParameterMatcherFindCache();
        final Class<?> thisClass = object.getClass();

        Map<string, TypeParameterMatcher> map = findCache.get(thisClass);
        if (map == null) {
            map = new HashMap<string, TypeParameterMatcher>();
            findCache.put(thisClass, map);
        }

        TypeParameterMatcher matcher = map.get(typeParamName);
        if (matcher == null) {
            matcher = get(ReflectionUtil.resolveTypeParameter(object, parametrizedSuperclass, typeParamName));
            map.put(typeParamName, matcher);
        }

        return matcher;
    }

    public abstract bool match(object msg);

    private static readonly class ReflectiveMatcher extends TypeParameterMatcher {
        private readonly Class<?> type;

        ReflectiveMatcher(Class<?> type) {
            this.type = type;
        }

        @Override
        public bool match(object msg) {
            return type.isInstance(msg);
        }
    }

    TypeParameterMatcher() { }
}
