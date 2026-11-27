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
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the Java enum with a constructor, a field and methods is a sealed class with static readonly instances, as
// codec-http2 Http2Error (docs/codec-http2-porting.md, Shared CLR adaptations); the implicit values() is Values() and
// ToString() returns the constant name. LOOKUP, Collections.unmodifiableMap of the stream collected with
// Collectors.toMap(id, identity), is a ReadOnlyDictionary over a Dictionary filled from Values() (a duplicate id
// throws in both: IllegalStateException in Java, ArgumentException from Dictionary.Add). fromId returns null for an
// unknown id, as Map.get does (TryGetValue). The @Nullable annotation has no counterpart (no nullable annotation context).
public sealed class Http3SettingIdentifier
{

    /**
     * QPACK maximum table capacity setting identifier (<b>0x1</b>).
     * <p>
     * Defined in <a href="https://datatracker.ietf.org/doc/html/rfc9204#section-5">
     * RFC 9204, Section 5 (SETTINGS_QPACK_MAX_TABLE_CAPACITY)</a> and registered in
     * the <a href="https://www.iana.org/assignments/http3-parameters/http3-parameters.xhtml#settings">
     * HTTP/3 SETTINGS registry (IANA)</a>.
     * <br>
     * Controls the maximum size of the dynamic table used by QPACK.
     */
    public static readonly Http3SettingIdentifier HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY =
        new Http3SettingIdentifier(nameof(HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY), 0x1);

    /**
     * Maximum field section size setting identifier (<b>0x6</b>).
     * <p>
     * Defined in <a href="https://datatracker.ietf.org/doc/html/rfc9114#section-7.2.4.1">
     * RFC 9114, Section 7.2.4.1 (SETTINGS_MAX_FIELD_SECTION_SIZE)</a> , also referenced
     * in the <a href="https://datatracker.ietf.org/doc/html/rfc9114#section-7.2.4.1">
     * HTTP/3 SETTINGS registry (RFC 9114, Section 7.2.4.1)</a> and registered in
     * the <a href="https://www.iana.org/assignments/http3-parameters/http3-parameters.xhtml#settings">
     * HTTP/3 SETTINGS registry (IANA)</a>.
     * <br>
     * Specifies the upper bound on the total size of HTTP field sections accepted by a peer.
     */
    public static readonly Http3SettingIdentifier HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE =
        new Http3SettingIdentifier(nameof(HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE), 0x6);

    /**
     * QPACK blocked streams setting identifier (<b>0x7</b>).
     * <p>
     * Defined in <a href="https://datatracker.ietf.org/doc/html/rfc9204#section-5">
     * RFC 9204, Section 5 (SETTINGS_QPACK_BLOCKED_STREAMS)</a> and registered in
     * the <a href="https://www.iana.org/assignments/http3-parameters/http3-parameters.xhtml#settings">
     * HTTP/3 SETTINGS registry (IANA)</a>.
     * <br>
     * Indicates the maximum number of streams that can be blocked waiting for QPACK instructions.
     */
    public static readonly Http3SettingIdentifier HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS =
        new Http3SettingIdentifier(nameof(HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS), 0x7);

    /**
     * ENABLE_CONNECT_PROTOCOL setting identifier (<b>0x8</b>).
     * <p>
     * Defined and registered in <a href="https://datatracker.ietf.org/doc/html/rfc9220#section-5">
     * RFC 9220, Section 5 (IANA Considerations)</a> and registered in
     * the <a href="https://www.iana.org/assignments/http3-parameters/http3-parameters.xhtml#settings">
     * HTTP/3 SETTINGS registry (IANA)</a>.
     * <br>
     * Enables use of the CONNECT protocol in HTTP/3 when set to 1; disabled when 0.
     */
    public static readonly Http3SettingIdentifier HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL =
        new Http3SettingIdentifier(nameof(HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL), 0x8);

    /**
     * ENABLE_H3_DATAGRAM setting identifier (<b>0x8</b>).
     * <p>
     * Defined and registered in <a href="https://datatracker.ietf.org/doc/html/rfc9297#name-http-3-setting">
     * RFC 9220, Section 5 (IANA Considerations)</a> and registered in
     * the <a href="https://www.iana.org/assignments/http3-parameters/http3-parameters.xhtml#settings">
     * HTTP/3 SETTINGS registry (IANA)</a>.
     * <br>
     * Enables use of the CONNECT protocol in HTTP/3 when set to 1; disabled when 0.
     */
    public static readonly Http3SettingIdentifier HTTP3_SETTINGS_H3_DATAGRAM =
        new Http3SettingIdentifier(nameof(HTTP3_SETTINGS_H3_DATAGRAM), 0x33);

    private readonly string name;
    private readonly long id;

    private static readonly IReadOnlyDictionary<long, Http3SettingIdentifier> LOOKUP = NewLookup();

    private Http3SettingIdentifier(string name, long id)
    {
        this.name = name;
        this.id = id;
    }

    // CLR: Collections.unmodifiableMap(Arrays.stream(values()).collect(Collectors.toMap(Http3SettingIdentifier::id,
    // Function.identity()))).
    private static IReadOnlyDictionary<long, Http3SettingIdentifier> NewLookup()
    {
        Dictionary<long, Http3SettingIdentifier> map = new Dictionary<long, Http3SettingIdentifier>();
        foreach (Http3SettingIdentifier identifier in Values())
        {
            map.Add(identifier.Id(), identifier);
        }
        return new ReadOnlyDictionary<long, Http3SettingIdentifier>(map);
    }

    // CLR: Java's implicit values().
    public static Http3SettingIdentifier[] Values()
    {
        return new[]
        {
            HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY, HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE,
            HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS, HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL, HTTP3_SETTINGS_H3_DATAGRAM
        };
    }

    /**
     * Returns the Identifier of {@link Http3SettingIdentifier}
     * for example:
     * SETTINGS_QPACK_MAX_TABLE_CAPACITY = 0x1 = 1 in the settings frame
     * <br>
     * @return long(represented as hexadecimal above) value of the Identifier
     */
    public long Id()
    {
        return id;
    }

    /**
     * Returns {@link Http3SettingIdentifier}
     * @param id
     * @return {@link Http3SettingIdentifier} enum which represents @param id, null otherwise
     */
    public static Http3SettingIdentifier FromId(long id)
    {
        return LOOKUP.TryGetValue(id, out Http3SettingIdentifier identifier) ? identifier : null;
    }

    // CLR: Enum.toString() is the constant name.
    public override string ToString()
    {
        return name;
    }
}
