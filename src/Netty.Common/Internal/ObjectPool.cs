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
public abstract class ObjectPool<T> {

    ObjectPool() { }

    /**
     * Get a {@link object} from the {@link ObjectPool}. The returned {@link object} may be created via
     * {@link ObjectCreator#newObject(Handle)} if no pooled {@link object} is ready to be reused.
     */
    public abstract T get();

    /**
     * Handle for an pooled {@link object} that will be used to notify the {@link ObjectPool} once it can
     * reuse the pooled {@link object} again.
     * @param <T>
     */
    public interface Handle<T> {
        /**
         * Recycle the {@link object} if possible and so make it ready to be reused.
         */
        void recycle(T self);
    }

    /**
     * Creates a new object which references the given {@link Handle} and calls {@link Handle#recycle(object)} once
     * it can be re-used.
     *
     * @param <T> the type of the pooled object
     */
    public interface ObjectCreator<T> {

        /**
         * Creates an returns a new {@link object} that can be used and later recycled via
         * {@link Handle#recycle(object)}.
         *
         * @param handle can NOT be null.
         */
        T newObject(Handle<T> handle);
    }

    /**
     * Creates a new {@link ObjectPool} which will use the given {@link ObjectCreator} to create the {@link object}
     * that should be pooled.
     */
    public static <T> ObjectPool<T> newPool(final ObjectCreator<T> creator) {
        return new RecyclerObjectPool<T>(ObjectUtil.checkNotNull(creator, "creator"));
    }

    private static readonly class RecyclerObjectPool<T> extends ObjectPool<T> {
        private readonly Recycler<T> recycler;

        RecyclerObjectPool(final ObjectCreator<T> creator) {
             recycler = new Recycler<T>() {
                @Override
                protected T newObject(Handle<T> handle) {
                    return creator.newObject(handle);
                }
            };
        }

        @Override
        public T get() {
            return recycler.get();
        }
    }
}
