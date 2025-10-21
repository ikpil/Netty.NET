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

using System;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common;

/**
 * Base implementation of {@link Constant}.
 */
public abstract class AbstractConstant<T> : IConstant<T> 
    where T : AbstractConstant<T>
{
    private static readonly AtomicLong _uniqueIdGenerator = new AtomicLong();
    private readonly int _id;
    private readonly string _name;
    private readonly long _uniquifier;

    /**
     * Creates a new instance.
     */
    protected AbstractConstant(int id, string name)
    {
        _id = id;
        _name = name;
        _uniquifier = _uniqueIdGenerator.getAndIncrement();
    }

    public string name()
    {
        return _name;
    }

    public int id()
    {
        return _id;
    }

    public override string ToString()
    {
        return name();
    }

    public int CompareTo(T o)
    {
        if (this == o)
        {
            return 0;
        }

        AbstractConstant<T> other = o;
        int returnCode = GetHashCode() - other.GetHashCode();
        if (returnCode != 0)
        {
            return returnCode;
        }

        if (_uniquifier < other._uniquifier)
        {
            return -1;
        }

        if (_uniquifier > other._uniquifier)
        {
            return 1;
        }

        throw new Exception("failed to compare two different constants");
    }
}