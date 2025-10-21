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
     * Try to call {@link AccessibleObject#setAccessible(bool)} but will catch any {@link SecurityException} and
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
     * @throws InvalidOperationException if the type parameter could not be resolved
     * */
    public static Type resolveTypeParameter(object obj, Type parametrizedSuperclass, string typeParamName)
    {
        Type currentClass = obj.GetType();
        while (currentClass != null)
        {
            if (currentClass.BaseType != parametrizedSuperclass)
            {
                currentClass = currentClass.BaseType;
                continue;
            }

            int typeParamIndex = -1;
            var typeParams = currentClass.GenericTypeArguments;
            for (int i = 0; i < typeParams.Length; i++)
            {
                if (typeParamName == typeParams[i].Name)
                {
                    typeParamIndex = i;
                    break;
                }
            }

            if (typeParamIndex < 0)
            {
                throw new InvalidOperationException("unknown type parameter '" + typeParamName + "': " + parametrizedSuperclass);
            }

            Type genericSuperType = currentClass.BaseType;
            if (!genericSuperType!.IsGenericType)
            {
                return typeof(object);
            }

            Type[] actualTypeParams = genericSuperType.GetGenericArguments();
            Type actualTypeParam = actualTypeParams[typeParamIndex];
            if (actualTypeParam.IsGenericType)
            {
                actualTypeParam = actualTypeParam.GetGenericTypeDefinition();
            }

            if (actualTypeParam.IsClass || actualTypeParam.IsInterface)
            {
                return actualTypeParam;
            }

            if (actualTypeParam.IsArray)
            {
                var componentType = actualTypeParam.GetElementType();
                if (componentType!.IsGenericType)
                    componentType = componentType.GetGenericTypeDefinition();
                if (componentType.IsClass || componentType.IsInterface)
                    return componentType.MakeArrayType();
            }

            if (actualTypeParam.IsGenericParameter)
            {
                if (!(actualTypeParam.DeclaringType is Type declaringType))
                    return typeof(object);

                currentClass = obj.GetType();
                parametrizedSuperclass = declaringType;
                typeParamName = actualTypeParam.Name;
                if (parametrizedSuperclass.IsAssignableFrom(currentClass))
                    continue;

                return typeof(object);
            }
        }

        return fail(currentClass, typeParamName);
    }
}