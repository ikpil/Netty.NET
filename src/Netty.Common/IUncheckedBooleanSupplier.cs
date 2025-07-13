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

namespace Netty.NET.Common;

/**
 * Represents a supplier of {@code bool}-valued results which doesn't throw any checked exceptions.
 */
public interface IUncheckedBooleanSupplier : IBooleanSupplier
{
    /**
     * Gets a bool value.
     * @return a bool value.
     */
    bool get();
}

/**
 * A supplier which always returns {@code false} and never throws.
 */
public class UncheckedFalseSupplier : IUncheckedBooleanSupplier
{
    public static readonly UncheckedFalseSupplier INSTANCE = new UncheckedFalseSupplier();

    private UncheckedFalseSupplier()
    {
    }

    public bool get()
    {
        return false;
    }
};

/**
 * A supplier which always returns {@code true} and never throws.
 */
public class UncheckedTrueSupplier : IUncheckedBooleanSupplier
{
    public static readonly UncheckedTrueSupplier INSTANCE = new UncheckedTrueSupplier();

    private UncheckedTrueSupplier()
    {
    }

    public bool get()
    {
        return true;
    }
};