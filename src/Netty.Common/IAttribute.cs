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
 * An attribute which allows to store a value reference. It may be updated atomically and so is thread-safe.
 *
 * @param <T>   the type of the value it holds.
 */
public interface IAttribute<T> 
{

    /**
     * Returns the key of this attribute.
     */
    AttributeKey<T> key();

    /**
     * Returns the current value, which may be {@code null}
     */
    T get();

    /**
     * Sets the value
     */
    void set(T value);

    /**
     *  Atomically sets to the given value and returns the old value which may be {@code null} if non was set before.
     */
    T getAndSet(T value);

    /**
     *  Atomically sets to the given value if this {@link IAttribute}'s value is {@code null}.
     *  If it was not possible to set the value as it contains a value it will just return the current value.
     */
    T setIfAbsent(T value);

    /**
     * Removes this attribute from the {@link IAttributeMap} and returns the old value. Subsequent {@link #get()}
     * calls will return {@code null}.
     *
     * If you only want to return the old value and clear the {@link IAttribute} while still keep it in the
     * {@link IAttributeMap} use {@link #getAndSet(object)} with a value of {@code null}.
     *
     * <p>
     * Be aware that even if you call this method another thread that has obtained a reference to this {@link IAttribute}
     * via {@link IAttributeMap#attr(AttributeKey)} will still operate on the same instance. That said if now another
     * thread or even the same thread later will call {@link IAttributeMap#attr(AttributeKey)} again, a new
     * {@link IAttribute} instance is created and so is not the same as the previous one that was removed. Because of
     * this special caution should be taken when you call {@link #remove()} or {@link #getAndRemove()}.
     *
     * @deprecated please consider using {@link #getAndSet(object)} (with value of {@code null}).
     */
    T getAndRemove();

    /**
     * Atomically sets the value to the given updated value if the current value == the expected value.
     * If it the set was successful it returns {@code true} otherwise {@code false}.
     */
    bool compareAndSet(T oldValue, T newValue);

    /**
     * Removes this attribute from the {@link IAttributeMap}. Subsequent {@link #get()} calls will return @{code null}.
     *
     * If you only want to remove the value and clear the {@link IAttribute} while still keep it in
     * {@link IAttributeMap} use {@link #set(object)} with a value of {@code null}.
     *
     * <p>
     * Be aware that even if you call this method another thread that has obtained a reference to this {@link IAttribute}
     * via {@link IAttributeMap#attr(AttributeKey)} will still operate on the same instance. That said if now another
     * thread or even the same thread later will call {@link IAttributeMap#attr(AttributeKey)} again, a new
     * {@link IAttribute} instance is created and so is not the same as the previous one that was removed. Because of
     * this special caution should be taken when you call {@link #remove()} or {@link #getAndRemove()}.
     *
     * @deprecated please consider using {@link #set(object)} (with value of {@code null}).
     */
    void remove();
}
