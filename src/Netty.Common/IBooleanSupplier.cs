/*
 * Copyright 2016 The Netty Project
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
 * Represents a supplier of {@code bool}-valued results.
 */
public interface IBooleanSupplier
{
    /**
     * Gets a bool value.
     * @return a bool value.
     * @throws Exception If an exception occurs.
     */
    bool get();
}

/**
 * A supplier which always returns {@code false} and never throws.
 */
public class FalseSupplier : IBooleanSupplier
{
    public static readonly FalseSupplier INSTANCE = new FalseSupplier();

    private FalseSupplier()
    {
    }

    public bool get()
    {
        return false;
    }
}

/**
 * A supplier which always returns {@code true} and never throws.
 */
public class TrueSupplier : IBooleanSupplier
{
    public static readonly TrueSupplier INSTANCE = new TrueSupplier();

    private TrueSupplier()
    {
    }

    public bool get()
    {
        return true;
    }
}