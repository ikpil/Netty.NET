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

using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

/**
 * Abstract base class for classes wants to implement {@link ReferenceCounted}.
 */
public abstract class AbstractReferenceCounted : IReferenceCounted
{
    private int _refCnt = 1;

    public int RefCnt()
    {
        return ReferenceCountUpdater.GetCount(ref _refCnt);
    }

    /**
     * An unsafe operation intended for use by a subclass that sets the reference count of the object directly
     */
    protected internal void SetRefCnt(int refCnt)
    {
        ReferenceCountUpdater.SetCount(ref _refCnt, refCnt);
    }

    public IReferenceCounted Retain()
    {
        return Retain(1);
    }

    public virtual IReferenceCounted Retain(int increment)
    {
        ReferenceCountUpdater.Retain(ref _refCnt, increment);
        return this;
    }

    public IReferenceCounted Touch()
    {
        return Touch(null);
    }

    public abstract IReferenceCounted Touch(object hint);

    public bool Release()
    {
        return Release(1);
    }

    public bool Release(int decrement)
    {
        if (ReferenceCountUpdater.Release(ref _refCnt, decrement))
        {
            Deallocate();
            return true;
        }
        return false;
    }

    /**
     * Called once {@link #refCnt()} is equals 0.
     */
    protected abstract void Deallocate();
}
