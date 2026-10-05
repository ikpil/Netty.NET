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

namespace Netty.NET.Common;

/**
 * Handle for an pooled {@link Object} that will be used to notify the {@link ObjectPool} once it can
 * reuse the pooled {@link Object} again.
 * @param <T>
 */
// CLR: the Recycler owns this handle directly; no deprecated ObjectPool base is needed.
public interface IRecyclerHandle<T>
{
    /**
     * Recycle the {@link Object} if possible and so make it ready to be reused.
     */
    void Recycle(T self);
}
