/*
 * Copyright 2014 The Netty Project
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
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;

/**
 * A {@link ChannelFactory} that instantiates a new {@link Channel} by invoking its default constructor reflectively.
 */
// CLR: io.netty.channel.ChannelFactory<T> is the BCL Func<T>; pass NewChannel where a factory is expected.
public class ReflectiveChannelFactory<T> where T : IChannel
{
    private readonly ConstructorInfo _constructor;

    // CLR: the annotation keeps the public parameterless constructor for trimming and NativeAOT.
    public ReflectiveChannelFactory(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type clazz)
    {
        ObjectUtil.CheckNotNull(clazz, "clazz");
        // CLR: Type carries no Class<? extends T> bound, so the bound is checked here instead of by the compiler.
        if (!typeof(T).IsAssignableFrom(clazz))
        {
            throw new ArgumentException("Class " + StringUtil.SimpleClassName(clazz) + " is not a " +
                    StringUtil.SimpleClassName(typeof(T)), nameof(clazz));
        }
        // CLR: Type.GetConstructor returns null where Class.getConstructor throws NoSuchMethodException.
        ConstructorInfo constructor = clazz.GetConstructor(Type.EmptyTypes);
        if (constructor == null)
        {
            throw new ArgumentException("Class " + StringUtil.SimpleClassName(clazz) +
                    " does not have a public non-arg constructor", nameof(clazz));
        }
        _constructor = constructor;
    }

    public T NewChannel()
    {
        try
        {
            return (T)_constructor.Invoke(null);
        }
        catch (Exception t)
        {
            throw new ChannelException("Unable to create Channel from class " + _constructor.DeclaringType, t);
        }
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(typeof(ReflectiveChannelFactory<T>)) +
                '(' + StringUtil.SimpleClassName(_constructor.DeclaringType) + ".class)";
    }
}
