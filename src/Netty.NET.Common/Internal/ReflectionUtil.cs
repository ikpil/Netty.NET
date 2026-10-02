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
using System.Security;

namespace Netty.NET.Common.Internal;

public static class ReflectionUtil
{
    /**
     * Try to call {@link AccessibleObject#setAccessible(boolean)} but will catch any {@link SecurityException} and
     * {@link java.lang.reflect.InaccessibleObjectException} and return it.
     * The caller must check if it returns {@code null} and if not handle the returned exception.
     */
    public static Exception trySetAccessible(object obj, bool checkAccessible)
    {
        if (checkAccessible && !PlatformDependent0.isExplicitTryReflectionSetAccessible())
        {
            return new NotSupportedException("Reflective setAccessible(true) disabled");
        }

        try
        {
            //obj.setAccessible(true);
            return null;
        }
        catch (SecurityException e)
        {
            return e;
        }
        catch (Exception e)
        {
            return handleInaccessibleObjectException(e);
        }
    }

    private static MemberAccessException handleInaccessibleObjectException(Exception e)
    {
        // JDK 9 can throw an inaccessible object exception here; since Netty compiles
        // against JDK 7 and this exception was only added in JDK 9, we have to weakly
        // check the type
        if (e is MemberAccessException || e.GetType().FullName == "System.MemberAccessException")
        {
            return e as MemberAccessException;
        }

        throw e;
    }

    private static Type fail(Type type, string typeParamName)
    {
        throw new InvalidOperationException(
            "cannot determine the type of the type parameter '" + typeParamName + "': " + type);
    }

    /**
     * Resolve a type parameter of a class that is a subclass of the given parametrized superclass.
     * @param object The object to resolve the type parameter for
     * @param parametrizedSuperclass The parametrized superclass
     * @param typeParamName The name of the type parameter to resolve
     * @return The resolved type parameter
     * @throws IllegalStateException if the type parameter could not be resolved
     * */
    // CLR adaptation: runtime generic arguments survive construction. Follow the
    // constructed superclass chain instead of reproducing JVM erasure failures.
    public static Type resolveTypeParameter(object obj, Type parametrizedSuperclass, string typeParamName)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(parametrizedSuperclass);
        ArgumentNullException.ThrowIfNull(typeParamName);
        Type thisClass = obj.GetType();
        Type superclassDefinition = parametrizedSuperclass.IsGenericType ?
            parametrizedSuperclass.GetGenericTypeDefinition() : parametrizedSuperclass;
        for (Type currentClass = thisClass; currentClass != null; currentClass = currentClass.BaseType)
        {
            Type genericSuperType = currentClass.BaseType;
            if (genericSuperType == null) continue;
            Type definition = genericSuperType.IsGenericType ? genericSuperType.GetGenericTypeDefinition() : genericSuperType;
            if (definition != superclassDefinition) continue;
            if (parametrizedSuperclass.IsConstructedGenericType && genericSuperType != parametrizedSuperclass) continue;
            Type[] parameters = definition.GetGenericArguments();
            int index = Array.FindIndex(parameters, parameter => parameter.Name == typeParamName);
            if (index < 0)
                throw new InvalidOperationException("unknown type parameter '" + typeParamName + "': " + parametrizedSuperclass);
            Type actualType = genericSuperType.GetGenericArguments()[index];
            if (actualType.IsGenericParameter)
            {
                // Resolved type parameter points to another type parameter.
                return fail(thisClass, typeParamName);
            }
            if (actualType.ContainsGenericParameters) return fail(thisClass, typeParamName);
            return actualType;
        }
        return fail(thisClass, typeParamName);
    }
}
