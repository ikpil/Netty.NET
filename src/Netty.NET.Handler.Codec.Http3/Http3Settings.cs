/*
 * Copyright 2025 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * Represents a collection of HTTP/3 settings as defined by the
 * <a href="https://datatracker.ietf.org/doc/html/rfc9114#section-7.2.4">
 * HTTP/3 specification</a>.
 *
 * <p>This class provides type-safe accessors for standard HTTP/3 settings such as:
 * <ul>
 *   <li>{@code QPACK_MAX_TABLE_CAPACITY} (0x1)</li>
 *   <li>{@code MAX_FIELD_SECTION_SIZE} (0x6)</li>
 *   <li>{@code QPACK_BLOCKED_STREAMS} (0x7)</li>
 *   <li>{@code ENABLE_CONNECT_PROTOCOL} (0x8)</li>
 *   <li>{@code H3_DATAGRAM} (0x33)</>
 * </ul>
 *
 * Non-standard settings are ignored
 * Reserved HTTP/2 setting identifiers are rejected.
 *
 */
// CLR: io.netty.util.collection's LongObjectHashMap<Long> is Dictionary<long, long>, so the entries enumerate in
// insertion order (codec-http2 owner decision; LongObjectHashMap enumerates by hash slot). Equals and GetHashCode
// keep LongObjectHashMap's content equality: the same size and equal values per key, and size ^ (the XOR of
// Long.hashCode(key)). Iterable<Map.Entry<Long, Long>> is IEnumerable<KeyValuePair<long, long>>. A Long parameter
// is a long, so the checkNotNull(value) of a standard setting has no counterpart, and a Long/Boolean result that
// may be null is long?/bool?. The nested interface NonStandardHttp3SettingsValidator takes the I prefix, and the
// lambda (id, v) -> false of the default constructor is the private nested class IgnoreNonStandardSettings.
// toHexString(long) is the lowercase hex format "x" (the two's complement for a negative value, as Java's unsigned
// text); numbers print with the invariant culture. IllegalArgumentException is ArgumentException.
// Http3SettingIdentifier.valueOf(String.valueOf(identifier)) is the identifier itself (its ToString() is the
// constant name). The switch over the enum is an if/else chain over the sealed-class constants (compared by
// reference).
public sealed class Http3Settings : IEnumerable<KeyValuePair<long, long>>
{

    private readonly Dictionary<long, long> settings;
    internal readonly INonStandardHttp3SettingsValidator nonStandardSettingsValidator;
    private const long TRUE = 1L;
    private const long FALSE = 0L;

    /**
     * Creates a new instance
     */
    public Http3Settings() :
        // Ignore non-standard settings by default.
        this(new IgnoreNonStandardSettings())
    {
    }

    /**
     * Creates a new instance
     *
     * @param nonStandardSettingsValidator the {@link NonStandardHttp3SettingsValidator} to use to check if a specific
     *                                     setting that is non-standard should be supported or not.
     */
    public Http3Settings(INonStandardHttp3SettingsValidator nonStandardSettingsValidator)
    {
        this.settings = new Dictionary<long, long>(Http3SettingIdentifier.Values().Length);
        this.nonStandardSettingsValidator = CheckNotNull(nonStandardSettingsValidator, "nonStandardSettingsValidator");
    }

    /**
     * Stores a setting value for the specified identifier.
     * <p>
     * The key and value are validated according to the HTTP/3 specification.
     * Reserved HTTP/2 setting identifiers and negative values are not allowed.
     * Ignore any unknown id/key as per <a href="https://www.rfc-editor.org/rfc/rfc9114.html#section-7.2.4-9>rfc9114</a>
     * @param key   the numeric setting identifier
     * @param value the setting value (non-null)
     * @return the previous value associated with the key, or {@code null} if none
     * @throws IllegalArgumentException if the key or value is invalid
     */
    public long? Put(long key, long value)
    {

        // When HTTP2 settings identifier present - Throw Error
        if (Http3CodecUtils.IsReservedHttp2Setting(key))
        {
            throw new ArgumentException("Setting is reserved for HTTP/2: " + key.ToString(CultureInfo.InvariantCulture));
        }

        Http3SettingIdentifier identifier = Http3SettingIdentifier.FromId(key);

        if (identifier == null)
        {
            // When Non-Standard/Unknown settings identifier present check if we should ignore it or not.
            if (!nonStandardSettingsValidator.Validate(key, value))
            {
                return null;
            }
        }
        else
        {
            //Validation
            VerifyStandardSetting(identifier, value);
        }

        long? previous = settings.TryGetValue(key, out long old) ? old : null;
        settings[key] = value;
        return previous;
    }

    /**
     * Returns the value of the specified setting identifier.
     *
     * @param key the numeric setting identifier
     * @return the setting value, or {@code null} if not set
     */
    public long? Get(long key)
    {
        return settings.TryGetValue(key, out long value) ? value : null;
    }

    /**
     * Returns the value associated with the specified setting identifier,
     * or {@code defaultValue} if no value is present for the given key.
     *
     * @param key the numeric setting identifier
     * @param defaultValue the value to return if the setting is not present
     * @return the configured value for the specified key, or {@code defaultValue},
     * if the key is not found
     */
    public long GetOrDefault(long key, long defaultValue)
    {
        return settings.GetValueOrDefault(key, defaultValue);
    }

    /**
     * Returns the {@code QPACK_MAX_TABLE_CAPACITY} value.
     *
     * @return the current QPACK maximum table capacity, or {@code null} if not set
     */
    public long? QpackMaxTableCapacity()
    {
        return Get(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id());
    }

    /**
     * Sets the {@code QPACK_MAX_TABLE_CAPACITY} value.
     *
     * @param value QPACK maximum table capacity (must be ≥ 0)
     * @return this instance for method chaining
     */
    public Http3Settings QpackMaxTableCapacity(long value)
    {
        Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(), value);
        return this;
    }

    /**
     * Returns the {@code MAX_FIELD_SECTION_SIZE} value.
     *
     * @return the maximum field section size, or {@code null} if not set
     */
    public long? MaxFieldSectionSize()
    {
        return Get(Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id());
    }

    /**
     * Sets the {@code MAX_FIELD_SECTION_SIZE} value.
     *
     * @param value maximum field section size (must be ≥ 0)
     * @return this instance for method chaining
     */
    public Http3Settings MaxFieldSectionSize(long value)
    {
        Put(Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id(), value);
        return this;
    }

    /**
     * Returns the {@code QPACK_BLOCKED_STREAMS} value.
     *
     * @return the number of blocked streams, or {@code null} if not set
     */
    public long? QpackBlockedStreams()
    {
        return Get(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id());
    }

    /**
     * Sets the {@code QPACK_BLOCKED_STREAMS} value.
     *
     * @param value number of blocked streams (must be ≥ 0)
     * @return this instance for method chaining
     */
    public Http3Settings QpackBlockedStreams(long value)
    {
        Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(), value);
        return this;
    }

    /**
     * Returns whether the {@code ENABLE_CONNECT_PROTOCOL} setting is enabled.
     *
     * @return {@code true} if enabled, {@code false} if disabled, or {@code null} if not set
     */
    public bool? ConnectProtocolEnabled()
    {
        long? value = Get(Http3SettingIdentifier.HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL.Id());
        return value == null ? null : TRUE == value.Value;
    }

    /**
     * Sets the {@code ENABLE_CONNECT_PROTOCOL} flag.
     *
     * @param enabled whether to enable the CONNECT protocol
     * @return this instance for method chaining
     */
    public Http3Settings EnableConnectProtocol(bool enabled)
    {
        Put(Http3SettingIdentifier.HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL.Id(), enabled ? TRUE : FALSE);
        return this;
    }

    /**
     * Returns whether the {@code H3_DATAGRAM} setting is enabled.
     *
     * @return {@code true} if enabled, {@code false} if disabled, or {@code null} if not set
     */
    public bool? H3DatagramEnabled()
    {
        long? value = Get(Http3SettingIdentifier.HTTP3_SETTINGS_H3_DATAGRAM.Id());
        return value == null ? null : TRUE == value.Value;
    }

    /**
     * Sets the {@code H3_DATAGRAM} settings identifier.
     *
     * @param enabled whether to enable the H3 Datagram
     * @return this instance for method chaining
     */
    public Http3Settings EnableH3Datagram(bool enabled)
    {
        Put(Http3SettingIdentifier.HTTP3_SETTINGS_H3_DATAGRAM.Id(), enabled ? TRUE : FALSE);
        return this;
    }

    /**
     * Replaces all current settings with those from another {@link Http3Settings} instance.
     *
     * @param http3Settings the source settings (non-null)
     * @return this instance for method chaining
     */
    public Http3Settings PutAll(Http3Settings http3Settings)
    {
        CheckNotNull(http3Settings, "http3Settings");

        foreach (KeyValuePair<long, long> entry in http3Settings.settings)
        {
            settings[entry.Key] = entry.Value;
        }

        return this;
    }

    /**
     * Returns a new {@link Http3Settings} instance with default values:
     * <ul>
     *   <li>{@code QPACK_MAX_TABLE_CAPACITY} = 0</li>
     *   <li>{@code QPACK_BLOCKED_STREAMS} = 0</li>
     *   <li>{@code ENABLE_CONNECT_PROTOCOL} = false</li>
     *   <li>{@code MAX_FIELD_SECTION_SIZE} = 8192</li>
     *   <li>{@code H3_DATAGRAM} = false </>
     * </ul>
     *
     * @return a default {@link Http3Settings} instance
     */
    public static Http3Settings DefaultSettings()
    {
        return new Http3Settings()
                .QpackMaxTableCapacity(0)
                .QpackBlockedStreams(0)
                .MaxFieldSectionSize(Http3CodecUtils.DEFAULT_MAX_FIELD_SECTION_SIZE)
                .EnableConnectProtocol(false)
                .EnableH3Datagram(false);
    }

    /**
     * Returns an iterator over the settings entries in this object.
     * Each entry’s key is the numeric setting identifier, and the value is its numeric value.
     *
     * @return an iterator over immutable {@link Map.Entry} objects
     */
    public IEnumerator<KeyValuePair<long, long>> GetEnumerator()
    {
        return settings.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /**
     * Compares this settings object to another for equality.
     * Two instances are equal if they contain the same key–value pairs.
     *
     * @param o the other object
     * @return {@code true} if equal, {@code false} otherwise
     */
    public override bool Equals(object o)
    {
        if (ReferenceEquals(this, o))
        {
            return true;
        }
        if (!(o is Http3Settings))
        {
            return false;
        }

        Http3Settings that = (Http3Settings) o;
        // CLR: LongObjectHashMap.equals: the same size and an equal value for every key.
        if (settings.Count != that.settings.Count)
        {
            return false;
        }

        foreach (KeyValuePair<long, long> entry in settings)
        {
            if (!that.settings.TryGetValue(entry.Key, out long otherValue) || otherValue != entry.Value)
            {
                return false;
            }
        }

        return true;
    }

    /**
     * Returns the hash code of this settings object, based on its key–value pairs.
     *
     * @return the hash code
     */
    public override int GetHashCode()
    {
        // CLR: LongObjectHashMap.hashCode: the size XOR Long.hashCode of every key.
        int hash = settings.Count;

        foreach (long key in settings.Keys)
        {
            hash ^= unchecked((int) (key ^ (key >>> 32)));
        }

        return hash;
    }

    /**
     * Returns a string representation of this settings object in the form:
     * <pre>
     * Http3Settings{0x1=100, 0x6=16384, 0x7=0}
     * </pre>
     *
     * @return a human-readable string representation of the settings
     */
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder("Http3Settings{");
        bool first = true;

        foreach (KeyValuePair<long, long> e in settings)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append("0x").Append(e.Key.ToString("x", CultureInfo.InvariantCulture)).Append('=')
                    .Append(e.Value.ToString(CultureInfo.InvariantCulture));
        }

        return sb.Append('}').ToString();
    }

    /**
     * Validates a setting identifier and value pair against HTTP/3.
     * Note that it can only validate the valid HTTP/3 settings
     * Does not validate non-standard settings
     * @param identifier the setting identifier
     * @param value the setting value
     * @throws IllegalArgumentException if the identifier or value violates the protocol specification
     */
    private static void VerifyStandardSetting(Http3SettingIdentifier identifier, long value)
    {
        CheckNotNull(identifier, "identifier");

        if (identifier == Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY ||
            identifier == Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS ||
            identifier == Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE)
        {
            if (value < 0)
            {
                throw new ArgumentException("Setting 0x" + identifier.Id().ToString("x", CultureInfo.InvariantCulture)
                        + " invalid: " + value.ToString(CultureInfo.InvariantCulture) + " (must be >= 0)");
            }
        }
        else if (identifier == Http3SettingIdentifier.HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL ||
                 identifier == Http3SettingIdentifier.HTTP3_SETTINGS_H3_DATAGRAM)
        {
            if (value != 0L && value != 1L)
            {
                throw new ArgumentException(
                        "Invalid: " + value.ToString(CultureInfo.InvariantCulture) + "for "
                                + identifier
                                + " (expected 0 or 1)");
            }
        }
        else
        {
            if (value < 0)
            {
                throw new ArgumentException("Setting 0x"
                        + identifier.Id().ToString("x", CultureInfo.InvariantCulture) + " invalid: "
                        + value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /**
     * Allows to handle non-standard settings. By default non-standard settings will be ignore as defined by the
     * RFC.
     */
    public interface INonStandardHttp3SettingsValidator
    {
        /**
         * Validate the setting with the given id and value.
         *
         * @param id        the id of the setting
         * @param value     the value of the setting
         * @return          {@code true} if the settings is supported, {@code false} otherwise.
         * @throws IllegalArgumentException if the given {@code value} is not supported for the id.
         */
        bool Validate(long id, long value);
    }

    // CLR: the lambda (id, v) -> false of the default constructor.
    private sealed class IgnoreNonStandardSettings : INonStandardHttp3SettingsValidator
    {
        public bool Validate(long id, long value)
        {
            return false;
        }
    }
}
