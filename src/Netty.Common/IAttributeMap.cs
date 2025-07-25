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

namespace Netty.NET.Common;

/**
 * Holds {@link IAttribute}s which can be accessed via {@link AttributeKey}.
 *
 * Implementations must be Thread-safe.
 */
public interface IAttributeMap
{
    /**
     * Get the {@link IAttribute} for the given {@link AttributeKey}. This method will never return null, but may return
     * an {@link IAttribute} which does not have a value set yet.
     */
    IAttribute<T> attr<T>(AttributeKey<T> key) where T : class;

    /**
     * Returns {@code true} if and only if the given {@link IAttribute} exists in this {@link IAttributeMap}.
     */
    bool hasAttr<T>(AttributeKey<T> key) where T : class;
}