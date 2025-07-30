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
using Netty.NET.Common.Internal.Logging;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common.Internal;

/**
 * A collection of utility methods to retrieve and parse the values of the Java system properties.
 */
public static class SystemPropertyUtil
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(SystemPropertyUtil));

    /**
     * Returns {@code true} if and only if the system property with the specified {@code key}
     * exists.
     */
    public static bool contains(string key)
    {
        return get(key) != null;
    }

    /**
     * Returns the value of the Java system property with the specified
     * {@code key}, while falling back to {@code null} if the property access fails.
     *
     * @return the property value or {@code null}
     */
    public static string get(string key)
    {
        return get(key, null);
    }

    /**
     * Returns the value of the Java system property with the specified
     * {@code key}, while falling back to the specified default value if
     * the property access fails.
     *
     * @return the property value.
     *         {@code def} if there's no such property or if an access to the
     *         specified property is not allowed.
     */
    public static string get(string key, string def)
    {
        checkNonEmpty(key, "key");

        string value = null;
        try
        {
            value = Environment.GetEnvironmentVariable(key) ?? def;
        }
        catch (Exception e)
        {
            logger.warn($"Unable to retrieve a system property '{key}'; default values will be used.", e);
        }

        return value;
    }

    /**
     * Returns the value of the Java system property with the specified
     * {@code key}, while falling back to the specified default value if
     * the property access fails.
     *
     * @return the property value.
     *         {@code def} if there's no such property or if an access to the
     *         specified property is not allowed.
     */
    public static bool getBoolean(string key, bool def)
    {
        string value = get(key);
        if (value == null)
        {
            return def;
        }

        value = value.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return def;
        }

        if ("true".Equals(value, StringComparison.OrdinalIgnoreCase)
            || "yes".Equals(value, StringComparison.OrdinalIgnoreCase)
            || "1".Equals(value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ("false".Equals(value, StringComparison.OrdinalIgnoreCase)
            || "no".Equals(value, StringComparison.OrdinalIgnoreCase)
            || "0".Equals(value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        logger.warn($"Unable to parse the bool system property '{key}':{value} - using the default value: {def}");

        return def;
    }

    /**
     * Returns the value of the Java system property with the specified
     * {@code key}, while falling back to the specified default value if
     * the property access fails.
     *
     * @return the property value.
     *         {@code def} if there's no such property or if an access to the
     *         specified property is not allowed.
     */
    public static int getInt(string key, int def)
    {
        string value = get(key);
        if (value == null)
        {
            return def;
        }

        value = value.Trim();
        try
        {
            return int.Parse(value);
        }
        catch (Exception e)
        {
            // Ignore
        }

        logger.warn($"Unable to parse the integer system property '{key}':{value} - using the default value: {def}");

        return def;
    }

    /**
     * Returns the value of the Java system property with the specified
     * {@code key}, while falling back to the specified default value if
     * the property access fails.
     *
     * @return the property value.
     *         {@code def} if there's no such property or if an access to the
     *         specified property is not allowed.
     */
    public static long getLong(string key, long def)
    {
        string value = get(key);
        if (value == null)
        {
            return def;
        }

        value = value.Trim();
        try
        {
            return long.Parse(value);
        }
        catch (Exception e)
        {
            // Ignore
        }

        logger.warn($"Unable to parse the long integer system property '{key}':{value} - using the default value: {def}");

        return def;
    }
}