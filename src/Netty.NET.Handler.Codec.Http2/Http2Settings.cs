/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Settings for one endpoint in an HTTP/2 connection. Each of the values are optional as defined in
 * the spec for the SETTINGS frame. Permits storage of arbitrary key/value pairs but provides helper
 * methods for standard settings.
 */
// CLR: CharObjectHashMap<Long> is the BCL Dictionary<char, long> (owner decision 2026-11-19, confirmed 2026-11-25), so
// the inherited map members keep their BCL names (Count, ContainsKey, TryGetValue, Remove, Clear, enumeration of
// KeyValuePair<char, long>); Java's get(char) returning null when absent is TryGetValue (the private Get here). The
// overridden put(char, Long) is Put(char, long), which verifies the value as Java does; the Dictionary indexer and
// Add are not virtual, so they store without verification. The Java Long/Integer/Boolean getters that return null
// when the setting is absent return long?/int?/bool?, and the value is a long, so checkNotNull(value) has nothing to
// check. Dictionary enumerates in insertion order where CharObjectHashMap enumerates in hash-slot order, so the order
// of the entries in toString() and in a written SETTINGS frame may differ from Java's (a SETTINGS frame holds each
// identifier once; owner decision 2026-11-25). The load factor of CharObjectHashMap has no Dictionary counterpart:
// the constructor checks its range as CharObjectHashMap does and passes only the capacity. toString() keeps the
// CharObjectHashMap text "{key=value, ...}" with keyToString(char) for the keys. Integer.toHexString is the invariant
// "x" format.
public sealed class Http2Settings : Dictionary<char, long>
{
    // CLR: CharObjectHashMap.DEFAULT_LOAD_FACTOR, which DEFAULT_CAPACITY uses.
    private const float DEFAULT_LOAD_FACTOR = 0.5f;

    /**
     * Default capacity based on the number of standard settings from the HTTP/2 spec, adjusted so that adding all of
     * the standard settings will not cause the map capacity to change.
     */
    private const int DEFAULT_CAPACITY = (int) (NUM_STANDARD_SETTINGS / DEFAULT_LOAD_FACTOR) + 1;
    private const long FALSE = 0L;
    private const long TRUE = 1L;

    public Http2Settings() : this(DEFAULT_CAPACITY)
    {
    }

    public Http2Settings(int initialCapacity, float loadFactor) : base(initialCapacity)
    {
        // CLR: the load factor check of the CharObjectHashMap(int, float) constructor.
        if (loadFactor <= 0.0f || loadFactor > 1.0f)
        {
            throw new ArgumentException("loadFactor must be > 0 and <= 1");
        }
    }

    public Http2Settings(int initialCapacity) : base(initialCapacity)
    {
    }

    /**
     * Adds the given setting key/value pair. For standard settings defined by the HTTP/2 spec, performs
     * validation on the values.
     *
     * @throws IllegalArgumentException if verification for a standard HTTP/2 setting fails.
     */
    public long? Put(char key, long value)
    {
        VerifyStandardSetting(key, value);
        long? previous = Get(key);
        this[key] = value;
        return previous;
    }

    /**
     * Gets the {@code SETTINGS_HEADER_TABLE_SIZE} value. If unavailable, returns {@code null}.
     */
    public long? HeaderTableSize()
    {
        return Get(SETTINGS_HEADER_TABLE_SIZE);
    }

    /**
     * Sets the {@code SETTINGS_HEADER_TABLE_SIZE} value.
     *
     * @throws IllegalArgumentException if verification of the setting fails.
     */
    public Http2Settings HeaderTableSize(long value)
    {
        Put(SETTINGS_HEADER_TABLE_SIZE, value);
        return this;
    }

    /**
     * Gets the {@code SETTINGS_ENABLE_PUSH} value. If unavailable, returns {@code null}.
     */
    public bool? PushEnabled()
    {
        long? value = Get(SETTINGS_ENABLE_PUSH);
        if (value == null)
        {
            return null;
        }
        return TRUE.Equals(value.Value);
    }

    /**
     * Sets the {@code SETTINGS_ENABLE_PUSH} value.
     */
    public Http2Settings PushEnabled(bool enabled)
    {
        Put(SETTINGS_ENABLE_PUSH, enabled ? TRUE : FALSE);
        return this;
    }

    /**
     * Gets the {@code SETTINGS_MAX_CONCURRENT_STREAMS} value. If unavailable, returns {@code null}.
     */
    public long? MaxConcurrentStreams()
    {
        return Get(SETTINGS_MAX_CONCURRENT_STREAMS);
    }

    /**
     * Sets the {@code SETTINGS_MAX_CONCURRENT_STREAMS} value.
     *
     * @throws IllegalArgumentException if verification of the setting fails.
     */
    public Http2Settings MaxConcurrentStreams(long value)
    {
        Put(SETTINGS_MAX_CONCURRENT_STREAMS, value);
        return this;
    }

    /**
     * Gets the {@code SETTINGS_INITIAL_WINDOW_SIZE} value. If unavailable, returns {@code null}.
     */
    public int? InitialWindowSize()
    {
        return GetIntValue(SETTINGS_INITIAL_WINDOW_SIZE);
    }

    /**
     * Sets the {@code SETTINGS_INITIAL_WINDOW_SIZE} value.
     *
     * @throws IllegalArgumentException if verification of the setting fails.
     */
    public Http2Settings InitialWindowSize(int value)
    {
        Put(SETTINGS_INITIAL_WINDOW_SIZE, value);
        return this;
    }

    /**
     * Gets the {@code SETTINGS_MAX_FRAME_SIZE} value. If unavailable, returns {@code null}.
     */
    public int? MaxFrameSize()
    {
        return GetIntValue(SETTINGS_MAX_FRAME_SIZE);
    }

    /**
     * Sets the {@code SETTINGS_MAX_FRAME_SIZE} value.
     *
     * @throws IllegalArgumentException if verification of the setting fails.
     */
    public Http2Settings MaxFrameSize(int value)
    {
        Put(SETTINGS_MAX_FRAME_SIZE, value);
        return this;
    }

    /**
     * Gets the {@code SETTINGS_MAX_HEADER_LIST_SIZE} value. If unavailable, returns {@code null}.
     */
    public long? MaxHeaderListSize()
    {
        return Get(SETTINGS_MAX_HEADER_LIST_SIZE);
    }

    /**
     * Sets the {@code SETTINGS_MAX_HEADER_LIST_SIZE} value.
     *
     * @throws IllegalArgumentException if verification of the setting fails.
     */
    public Http2Settings MaxHeaderListSize(long value)
    {
        Put(SETTINGS_MAX_HEADER_LIST_SIZE, value);
        return this;
    }

    /**
     * Gets the {@code SETTINGS_ENABLE_CONNECT_PROTOCOL} value. If unavailable, returns {@code null}.
     */
    public bool? ConnectProtocolEnabled()
    {
        long? value = Get(SETTINGS_ENABLE_CONNECT_PROTOCOL);
        if (value == null)
        {
            return null;
        }
        return TRUE.Equals(value.Value);
    }

    /**
     * Sets the {@code SETTINGS_ENABLE_CONNECT_PROTOCOL} value.
     */
    public Http2Settings ConnectProtocolEnabled(bool enabled)
    {
        Put(SETTINGS_ENABLE_CONNECT_PROTOCOL, enabled ? TRUE : FALSE);
        return this;
    }

    /**
     * Clears and then copies the given settings into this object.
     */
    // CLR: putAll(settings) of CharObjectHashMap calls put(key, value) for each entry, so the copied values are verified.
    public Http2Settings CopyFrom(Http2Settings settings)
    {
        Clear();
        foreach (KeyValuePair<char, long> entry in settings)
        {
            Put(entry.Key, entry.Value);
        }
        return this;
    }

    /**
     * A helper method that returns {@link Long#intValue()} on the return of {@link #get(char)}, if present. Note that
     * if the range of the value exceeds {@link Integer#MAX_VALUE}, the {@link #get(char)} method should
     * be used instead to avoid truncation of the value.
     */
    public int? GetIntValue(char key)
    {
        long? value = Get(key);
        if (value == null)
        {
            return null;
        }
        return unchecked((int) value.Value);
    }

    // CLR: CharObjectHashMap.get(char), which returns null for a missing key.
    private long? Get(char key)
    {
        return TryGetValue(key, out long value) ? value : null;
    }

    private static void VerifyStandardSetting(int key, long value)
    {
        switch (key)
        {
            case SETTINGS_HEADER_TABLE_SIZE:
                if (value < MIN_HEADER_TABLE_SIZE || value > MAX_HEADER_TABLE_SIZE)
                {
                    throw new ArgumentException("Setting HEADER_TABLE_SIZE is invalid: " + Str(value) +
                            ", expected [" + Str(MIN_HEADER_TABLE_SIZE) + ", " + Str(MAX_HEADER_TABLE_SIZE) + ']');
                }
                break;
            case SETTINGS_ENABLE_PUSH:
                if (value != 0L && value != 1L)
                {
                    throw new ArgumentException("Setting ENABLE_PUSH is invalid: " + Str(value) +
                            ", expected [0, 1]");
                }
                break;
            case SETTINGS_MAX_CONCURRENT_STREAMS:
                if (value < MIN_CONCURRENT_STREAMS || value > MAX_CONCURRENT_STREAMS)
                {
                    throw new ArgumentException("Setting MAX_CONCURRENT_STREAMS is invalid: " + Str(value) +
                            ", expected [" + Str(MIN_CONCURRENT_STREAMS) + ", " + Str(MAX_CONCURRENT_STREAMS) + ']');
                }
                break;
            case SETTINGS_INITIAL_WINDOW_SIZE:
                if (value < MIN_INITIAL_WINDOW_SIZE || value > MAX_INITIAL_WINDOW_SIZE)
                {
                    throw new ArgumentException("Setting INITIAL_WINDOW_SIZE is invalid: " + Str(value) +
                            ", expected [" + Str(MIN_INITIAL_WINDOW_SIZE) + ", " + Str(MAX_INITIAL_WINDOW_SIZE) + ']');
                }
                break;
            case SETTINGS_MAX_FRAME_SIZE:
                if (!IsMaxFrameSizeValid(unchecked((int) value)))
                {
                    throw new ArgumentException("Setting MAX_FRAME_SIZE is invalid: " + Str(value) +
                            ", expected [" + Str(MAX_FRAME_SIZE_LOWER_BOUND) + ", " + Str(MAX_FRAME_SIZE_UPPER_BOUND) + ']');
                }
                break;
            case SETTINGS_MAX_HEADER_LIST_SIZE:
                if (value < MIN_HEADER_LIST_SIZE || value > MAX_HEADER_LIST_SIZE)
                {
                    throw new ArgumentException("Setting MAX_HEADER_LIST_SIZE is invalid: " + Str(value) +
                            ", expected [" + Str(MIN_HEADER_LIST_SIZE) + ", " + Str(MAX_HEADER_LIST_SIZE) + ']');
                }
                break;
            case SETTINGS_ENABLE_CONNECT_PROTOCOL:
                if (value != 0L && value != 1L)
                {
                    throw new ArgumentException("Setting ENABLE_CONNECT_PROTOCOL is invalid: " + Str(value) +
                            ", expected [0, 1]");
                }
                break;
            default:
                // Non-standard HTTP/2 setting
                if (value < 0 || value > MAX_UNSIGNED_INT)
                {
                    throw new ArgumentException("Non-standard setting 0x" + key.ToString("x", CultureInfo.InvariantCulture) +
                            " is invalid: " + Str(value) + ", expected unsigned 32-bit value");
                }
                break;
        }
    }

    // CLR: CharObjectHashMap.keyToString(char), overridden in Java; a private method here because Dictionary has no
    // such hook (it serves ToString()).
    private static string KeyToString(char key)
    {
        switch (key)
        {
            case SETTINGS_HEADER_TABLE_SIZE:
                return "HEADER_TABLE_SIZE";
            case SETTINGS_ENABLE_PUSH:
                return "ENABLE_PUSH";
            case SETTINGS_MAX_CONCURRENT_STREAMS:
                return "MAX_CONCURRENT_STREAMS";
            case SETTINGS_INITIAL_WINDOW_SIZE:
                return "INITIAL_WINDOW_SIZE";
            case SETTINGS_MAX_FRAME_SIZE:
                return "MAX_FRAME_SIZE";
            case SETTINGS_MAX_HEADER_LIST_SIZE:
                return "MAX_HEADER_LIST_SIZE";
            case SETTINGS_ENABLE_CONNECT_PROTOCOL:
                return "ENABLE_CONNECT_PROTOCOL";
            default:
                // Unknown keys.
                return "0x" + ((int) key).ToString("x", CultureInfo.InvariantCulture);
        }
    }

    // CLR: CharObjectHashMap.toString(): "{}" when empty, otherwise "{" + keyToString(key) + "=" + value joined with
    // ", " + "}", in Dictionary order.
    public override string ToString()
    {
        if (Count == 0)
        {
            return "{}";
        }
        StringBuilder sb = new StringBuilder(4 * Count);
        sb.Append('{');
        bool first = true;
        foreach (KeyValuePair<char, long> entry in this)
        {
            if (!first)
            {
                sb.Append(", ");
            }
            sb.Append(KeyToString(entry.Key)).Append('=').Append(Str(entry.Value));
            first = false;
        }
        return sb.Append('}').ToString();
    }

    // CLR: CharObjectHashMap.equals(Object): a char map with the same size and an equal value for each key
    // (Dictionary keeps Object.Equals, reference equality); DefaultHttp2SettingsFrame compares its settings with it.
    public override bool Equals(object obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }
        if (!(obj is IDictionary<char, long> other))
        {
            return false;
        }
        if (Count != other.Count)
        {
            return false;
        }
        foreach (KeyValuePair<char, long> entry in this)
        {
            if (!other.TryGetValue(entry.Key, out long otherValue) || entry.Value != otherValue)
            {
                return false;
            }
        }
        return true;
    }

    // CLR: CharObjectHashMap.hashCode(): the size XOR-ed with every key (the hash of a char key is its value), which
    // does not depend on the order of the entries.
    public override int GetHashCode()
    {
        int hash = Count;
        foreach (char key in Keys)
        {
            hash ^= key;
        }
        return hash;
    }

    public static Http2Settings DefaultSettings()
    {
        return new Http2Settings().MaxHeaderListSize(DEFAULT_HEADER_LIST_SIZE)
                .MaxConcurrentStreams(DEFAULT_MAX_CONCURRENT_STREAMS);
    }

    // CLR: Java string concatenation of a long (Long.toString), invariant culture.
    private static string Str(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
