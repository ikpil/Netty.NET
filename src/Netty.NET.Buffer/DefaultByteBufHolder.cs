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
using Netty.NET.Common;

namespace Netty.NET.Buffer;

/**
 * Default implementation of a {@link ByteBufHolder} that holds it's data in a {@link ByteBuf}.
 *
 */
public class DefaultByteBufHolder : IByteBufHolder
{
    private readonly ByteBuf _data;

    public DefaultByteBufHolder(ByteBuf data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
    }

    public virtual ByteBuf Content => ByteBufUtil.EnsureAccessible(_data);

    /**
     * {@inheritDoc}
     * <p>
     * This method calls {@code replace(content().copy())} by default.
     */
    public virtual IByteBufHolder Copy() => Replace(_data.Copy());

    /**
     * {@inheritDoc}
     * <p>
     * This method calls {@code replace(content().duplicate())} by default.
     */
    public virtual IByteBufHolder Duplicate() => Replace(_data.Duplicate());

    /**
     * {@inheritDoc}
     * <p>
     * This method calls {@code replace(content().retainedDuplicate())} by default.
     */
    public virtual IByteBufHolder RetainedDuplicate() => Replace(_data.RetainedDuplicate());

    /**
     * {@inheritDoc}
     * <p>
     * Override this method to return a new instance of this object whose content is set to the specified
     * {@code content}. The default implementation of {@link #copy()}, {@link #duplicate()} and
     * {@link #retainedDuplicate()} invokes this method to create a copy.
     */
    public virtual IByteBufHolder Replace(ByteBuf content) => new DefaultByteBufHolder(content);

    public virtual int ReferenceCount => _data.ReferenceCount;

    public virtual IByteBufHolder Retain()
    {
        _data.Retain();
        return this;
    }

    public virtual IByteBufHolder Retain(int increment)
    {
        _data.Retain(increment);
        return this;
    }

    public virtual IByteBufHolder Touch()
    {
        _data.Touch();
        return this;
    }

    public virtual IByteBufHolder Touch(object hint)
    {
        _data.Touch(hint);
        return this;
    }

    public virtual bool Release() => _data.Release();
    public virtual bool Release(int decrement) => _data.Release(decrement);

    IReferenceCounted IReferenceCounted.Retain() => Retain();
    IReferenceCounted IReferenceCounted.Retain(int increment) => Retain(increment);
    IReferenceCounted IReferenceCounted.Touch() => Touch();
    IReferenceCounted IReferenceCounted.Touch(object hint) => Touch(hint);

    /**
     * Return {@link ByteBuf#toString()} without checking the reference count first. This is useful to implement
     * {@link #toString()}.
     */
    protected string ContentToString() => _data.ToString();

    public override string ToString() => GetType().Name + '(' + ContentToString() + ')';

    /**
     * This implementation of the {@code equals} operation is restricted to
     * work only with instances of the same class. The reason for that is that
     * Netty library already has a number of classes that extend {@link DefaultByteBufHolder} and
     * override {@code equals} method with an additional comparison logic and we
     * need the symmetric property of the {@code equals} operation to be preserved.
     *
     * @param   o   the reference object with which to compare.
     * @return  {@code true} if this object is the same as the obj
     *          argument; {@code false} otherwise.
     */
    public override bool Equals(object obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        return obj is not null && GetType() == obj.GetType() && _data.Equals(((DefaultByteBufHolder)obj)._data);
    }

    public override int GetHashCode() => _data.GetHashCode();
}
