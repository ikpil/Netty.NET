/*
 * Copyright 2020 The Netty Project
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
using System.Collections.Generic;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * See <a href="https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.4">SETTINGS</a>.
 */
// CLR: the interface takes the I prefix (docs/codec-http2-porting.md, Shared CLR adaptations); Iterable<Map.Entry<Long,
// Long>> is IEnumerable<KeyValuePair<long, long>>. The @Deprecated constants HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY,
// HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS, HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL and HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE
// ("Use Http3SettingIdentifier#... instead") and the @Deprecated default methods get(long) and getOrDefault(long, long)
// ("Use typed accessors via settings() instead") name their replacements, so they are not ported (owner decision
// 2026-11-16, docs/codec-http-porting.md); their Javadoc is recorded in the manifest. put(long, Long) carries a
// @deprecated tag but no @Deprecated annotation, so it is ported, without [Obsolete] as Java does not mark it. Its value
// is a long: the null that checkNotNull rejects for the standard settings has no counterpart, and the previous value is
// a long? (null when none was stored). The default methods are default interface members; type() is the explicit
// default member of IHttp3Frame.Type(), and settings() throws NotSupportedException (UnsupportedOperationException).
public interface IHttp3SettingsFrame : IHttp3ControlStreamFrame, IEnumerable<KeyValuePair<long, long>>
{

    Http3Settings Settings()
    {
        throw new NotSupportedException(
                "Http3SettingsFrame.settings() not implemented in this version");
    }

    long IHttp3Frame.Type()
    {
        return Http3CodecUtils.HTTP3_SETTINGS_FRAME_TYPE;
    }

    /**
     * Put a setting in the frame.
     *
     * @param key       the key of the setting
     * @param value     the value of the setting.
     * @return          the previous stored valued for the given key or {@code null} if none was stored before.
     * @deprecated Use typed accessors via {@link #settings()} instead.
     * * For example, {@code frame.settings().enableConnectProtocol(true)}.
     */
    long? Put(long key, long value)
    {
        return Settings().Put(key, value);
    }
}
