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
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * The default {@link SpdySettingsFrame} implementation.
 */
// CLR: TreeMap<Integer, Setting> is SortedDictionary<int, Setting>, which enumerates its entries in ascending key order
// as TreeMap does (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/TreeMap.html,
// https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.sorteddictionary-2?view=net-10.0). TreeMap's
// keySet() is a live view of the map; ids() returns a SortedSet<int> snapshot of the keys in the same ascending order,
// because the BCL has no ISet view of dictionary keys (the callers only iterate it before changing the frame).
// Set<Map.Entry<Integer, Setting>> is the dictionary's KeyValuePair enumeration. IllegalArgumentException is
// ArgumentException (docs/codec-base-porting.md); a boolean is appended as Java's "true"/"false" and numbers with the
// invariant culture; StringBuilder.setLength is the Length setter. Java methods are virtual unless final.
public class DefaultSpdySettingsFrame : ISpdySettingsFrame
{

    private bool clear;
    private readonly SortedDictionary<int, Setting> settingsMap = new SortedDictionary<int, Setting>();

    public virtual ISet<int> Ids()
    {
        return new SortedSet<int>(settingsMap.Keys);
    }

    public virtual bool IsSet(int id)
    {
        return settingsMap.ContainsKey(id);
    }

    public virtual int GetValue(int id)
    {
        settingsMap.TryGetValue(id, out Setting setting);
        return setting != null ? setting.GetValue() : -1;
    }

    public virtual ISpdySettingsFrame SetValue(int id, int value)
    {
        return SetValue(id, value, false, false);
    }

    public virtual ISpdySettingsFrame SetValue(int id, int value, bool persistValue, bool persisted)
    {
        if (id < 0 || id > SpdyCodecUtil.SPDY_SETTINGS_MAX_ID)
        {
            throw new ArgumentException("Setting ID is not valid: " + id.ToString(CultureInfo.InvariantCulture));
        }
        int key = id;
        settingsMap.TryGetValue(key, out Setting setting);
        if (setting != null)
        {
            setting.SetValue(value);
            setting.SetPersist(persistValue);
            setting.SetPersisted(persisted);
        }
        else
        {
            settingsMap[key] = new Setting(value, persistValue, persisted);
        }
        return this;
    }

    public virtual ISpdySettingsFrame RemoveValue(int id)
    {
        settingsMap.Remove(id);
        return this;
    }

    public virtual bool IsPersistValue(int id)
    {
        settingsMap.TryGetValue(id, out Setting setting);
        return setting != null && setting.IsPersist();
    }

    public virtual ISpdySettingsFrame SetPersistValue(int id, bool persistValue)
    {
        settingsMap.TryGetValue(id, out Setting setting);
        if (setting != null)
        {
            setting.SetPersist(persistValue);
        }
        return this;
    }

    public virtual bool IsPersisted(int id)
    {
        settingsMap.TryGetValue(id, out Setting setting);
        return setting != null && setting.IsPersisted();
    }

    public virtual ISpdySettingsFrame SetPersisted(int id, bool persisted)
    {
        settingsMap.TryGetValue(id, out Setting setting);
        if (setting != null)
        {
            setting.SetPersisted(persisted);
        }
        return this;
    }

    public virtual bool ClearPreviouslyPersistedSettings()
    {
        return clear;
    }

    public virtual ISpdySettingsFrame SetClearPreviouslyPersistedSettings(bool clear)
    {
        this.clear = clear;
        return this;
    }

    private IEnumerable<KeyValuePair<int, Setting>> GetSettings()
    {
        return settingsMap;
    }

    private void AppendSettings(StringBuilder buf)
    {
        foreach (KeyValuePair<int, Setting> e in GetSettings())
        {
            Setting setting = e.Value;
            buf.Append("--> ");
            buf.Append(e.Key.ToString(CultureInfo.InvariantCulture));
            buf.Append(':');
            buf.Append(setting.GetValue().ToString(CultureInfo.InvariantCulture));
            buf.Append(" (persist value: ");
            buf.Append(setting.IsPersist() ? "true" : "false");
            buf.Append("; persisted: ");
            buf.Append(setting.IsPersisted() ? "true" : "false");
            buf.Append(')');
            buf.Append(StringUtil.NEWLINE);
        }
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder()
            .Append(StringUtil.SimpleClassName(this))
            .Append(StringUtil.NEWLINE);
        AppendSettings(buf);

        buf.Length = buf.Length - StringUtil.NEWLINE.Length;
        return buf.ToString();
    }

    private sealed class Setting
    {

        private int value;
        private bool persist;
        private bool persisted;

        internal Setting(int value, bool persist, bool persisted)
        {
            this.value = value;
            this.persist = persist;
            this.persisted = persisted;
        }

        internal int GetValue()
        {
            return value;
        }

        internal void SetValue(int value)
        {
            this.value = value;
        }

        internal bool IsPersist()
        {
            return persist;
        }

        internal void SetPersist(bool persist)
        {
            this.persist = persist;
        }

        internal bool IsPersisted()
        {
            return persisted;
        }

        internal void SetPersisted(bool persisted)
        {
            this.persisted = persisted;
        }
    }
}
