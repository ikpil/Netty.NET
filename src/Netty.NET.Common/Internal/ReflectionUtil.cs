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

namespace Netty.NET.Common.Internal;

public static class ReflectionUtil
{


    private static Type Fail(Type type, string typeParamName)
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
    public static Type ResolveTypeParameter(object obj, Type parametrizedSuperclass, string typeParamName)
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
                return Fail(thisClass, typeParamName);
            }
            if (actualType.ContainsGenericParameters) return Fail(thisClass, typeParamName);
            return actualType;
        }
        return Fail(thisClass, typeParamName);
    }
}
