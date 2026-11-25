/*
 * Copyright 2013 The Netty Project
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

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * A SPDY Protocol SETTINGS Frame
 */
// CLR: the Java interface takes the I prefix (owner rule). The interface constants are constants of the interface
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface), and Set<Integer> is
// ISet<int>.
public interface ISpdySettingsFrame : ISpdyFrame
{

    public const int SETTINGS_MINOR_VERSION                  = 0;
    public const int SETTINGS_UPLOAD_BANDWIDTH               = 1;
    public const int SETTINGS_DOWNLOAD_BANDWIDTH             = 2;
    public const int SETTINGS_ROUND_TRIP_TIME                = 3;
    public const int SETTINGS_MAX_CONCURRENT_STREAMS         = 4;
    public const int SETTINGS_CURRENT_CWND                   = 5;
    public const int SETTINGS_DOWNLOAD_RETRANS_RATE          = 6;
    public const int SETTINGS_INITIAL_WINDOW_SIZE            = 7;
    public const int SETTINGS_CLIENT_CERTIFICATE_VECTOR_SIZE = 8;

    /**
     * Returns a {@code Set} of the setting IDs.
     * The set's iterator will return the IDs in ascending order.
     */
    ISet<int> Ids();

    /**
     * Returns {@code true} if the setting ID has a value.
     */
    bool IsSet(int id);

    /**
     * Returns the value of the setting ID.
     * Returns -1 if the setting ID is not set.
     */
    int GetValue(int id);

    /**
     * Sets the value of the setting ID.
     * The ID cannot be negative and cannot exceed 16777215.
     */
    ISpdySettingsFrame SetValue(int id, int value);

    /**
     * Sets the value of the setting ID.
     * Sets if the setting should be persisted (should only be set by the server).
     * Sets if the setting is persisted (should only be set by the client).
     * The ID cannot be negative and cannot exceed 16777215.
     */
    ISpdySettingsFrame SetValue(int id, int value, bool persistVal, bool persisted);

    /**
     * Removes the value of the setting ID.
     * Removes all persistence information for the setting.
     */
    ISpdySettingsFrame RemoveValue(int id);

    /**
     * Returns {@code true} if this setting should be persisted.
     * Returns {@code false} if this setting should not be persisted
     *         or if the setting ID has no value.
     */
    bool IsPersistValue(int id);

    /**
     * Sets if this setting should be persisted.
     * Has no effect if the setting ID has no value.
     */
    ISpdySettingsFrame SetPersistValue(int id, bool persistValue);

    /**
     * Returns {@code true} if this setting is persisted.
     * Returns {@code false} if this setting should not be persisted
     *         or if the setting ID has no value.
     */
    bool IsPersisted(int id);

    /**
     * Sets if this setting is persisted.
     * Has no effect if the setting ID has no value.
     */
    ISpdySettingsFrame SetPersisted(int id, bool persisted);

    /**
     * Returns {@code true} if previously persisted settings should be cleared.
     */
    bool ClearPreviouslyPersistedSettings();

    /**
     * Sets if previously persisted settings should be cleared.
     */
    ISpdySettingsFrame SetClearPreviouslyPersistedSettings(bool clear);
}
