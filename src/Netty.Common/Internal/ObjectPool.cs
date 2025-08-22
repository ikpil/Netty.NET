/*
 * Copyright 2019 The Netty Project
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

/**
 * Light-weight object pool.
 *
 * @param <T> the type of the pooled object
 */
public abstract class ObjectPool<T>
{
    protected ObjectPool()
    {
    }

    /**
     * Get a {@link object} from the {@link ObjectPool}. The returned {@link object} may be created via
     * {@link ObjectCreator#newObject(Handle)} if no pooled {@link object} is ready to be reused.
     */
    public abstract T get();

    /**
     * Creates a new {@link ObjectPool} which will use the given {@link ObjectCreator} to create the {@link object}
     * that should be pooled.
     */
    public static ObjectPool<T> newPool(IObjectCreator<T> creator)
    {
        return new RecyclerObjectPool<T>(ObjectUtil.checkNotNull(creator, "creator"));
    }
}