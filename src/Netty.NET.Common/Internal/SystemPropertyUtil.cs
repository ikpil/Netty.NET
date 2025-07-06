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
namespace Netty.NET.Common.Internal;









/**
 * A collection of utility methods to retrieve and parse the values of the Java system properties.
 */
public final class SystemPropertyUtil {

    private static readonly InternalLogger logger = InternalLoggerFactory.getInstance(SystemPropertyUtil.class);

    /**
     * Returns {@code true} if and only if the system property with the specified {@code key}
     * exists.
     */
    public static bool contains(string key) {
        return get(key) != null;
    }

    /**
     * Returns the value of the Java system property with the specified
     * {@code key}, while falling back to {@code null} if the property access fails.
     *
     * @return the property value or {@code null}
     */
    public static string get(string key) {
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
    public static string get(final string key, string def) {
        checkNonEmpty(key, "key");

        string value = null;
        try {
            if (System.getSecurityManager() == null) {
                value = System.getProperty(key);
            } else {
                value = AccessController.doPrivileged(new PrivilegedAction<string>() {
                    @Override
                    public string run() {
                        return System.getProperty(key);
                    }
                });
            }
        } catch (SecurityException e) {
            logger.warn("Unable to retrieve a system property '{}'; default values will be used.", key, e);
        }

        if (value == null) {
            return def;
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
    public static bool getBoolean(string key, bool def) {
        string value = get(key);
        if (value == null) {
            return def;
        }

        value = value.trim().toLowerCase();
        if (value.isEmpty()) {
            return def;
        }

        if ("true".equals(value) || "yes".equals(value) || "1".equals(value)) {
            return true;
        }

        if ("false".equals(value) || "no".equals(value) || "0".equals(value)) {
            return false;
        }

        logger.warn(
                "Unable to parse the bool system property '{}':{} - using the default value: {}",
                key, value, def
        );

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
    public static int getInt(string key, int def) {
        string value = get(key);
        if (value == null) {
            return def;
        }

        value = value.trim();
        try {
            return int.parseInt(value);
        } catch (Exception e) {
            // Ignore
        }

        logger.warn(
                "Unable to parse the integer system property '{}':{} - using the default value: {}",
                key, value, def
        );

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
    public static long getLong(string key, long def) {
        string value = get(key);
        if (value == null) {
            return def;
        }

        value = value.trim();
        try {
            return long.parseLong(value);
        } catch (Exception e) {
            // Ignore
        }

        logger.warn(
                "Unable to parse the long integer system property '{}':{} - using the default value: {}",
                key, value, def
        );

        return def;
    }

    private SystemPropertyUtil() {
        // Unused
    }
}
