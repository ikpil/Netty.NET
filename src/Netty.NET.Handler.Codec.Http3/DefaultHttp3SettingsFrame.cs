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

using System.Collections;
using System.Collections.Generic;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * Default implementation of {@link Http3SettingsFrame}.
 *
 */
// CLR: the interface is IHttp3SettingsFrame; Iterable is IEnumerable<KeyValuePair<long, long>>. The @Deprecated overrides
// get(long) and put(long, Long) ("use settings() and manipulate the Http3Settings directly") name their replacement, so
// they are not ported (owner decision 2026-11-16, docs/codec-http-porting.md) and their comments are recorded in the
// manifest; put is the IHttp3SettingsFrame default member, which calls settings().put as the override did, so copyOf
// calls it through the interface. getClass() != o.getClass() is GetType() != o.GetType().
public sealed class DefaultHttp3SettingsFrame : IHttp3SettingsFrame
{

    private readonly Http3Settings settings;

    public DefaultHttp3SettingsFrame(Http3Settings settings)
    {
        this.settings = ObjectUtil.CheckNotNull(settings, "settings");
    }

    public DefaultHttp3SettingsFrame()
    {
        this.settings = new Http3Settings();
    }

    public Http3Settings Settings()
    {
        return settings;
    }

    public IEnumerator<KeyValuePair<long, long>> GetEnumerator()
    {
       return settings.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override int GetHashCode()
    {
        return settings.GetHashCode();
    }

    public override bool Equals(object o)
    {
        if (ReferenceEquals(this, o))
        {
            return true;
        }
        if (o == null || GetType() != o.GetType())
        {
            return false;
        }
        DefaultHttp3SettingsFrame that = (DefaultHttp3SettingsFrame) o;
        return that.settings.Equals(settings);
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(settings=" + settings + ')';
    }

    /**
     * Creates a new {@link DefaultHttp3SettingsFrame} which is a copy of the given settings.
     *
     * @param settingsFrame the frame to copy.
     * @return              the newly created copy.
     */
    public static DefaultHttp3SettingsFrame CopyOf(IHttp3SettingsFrame settingsFrame)
    {
        Http3Settings settingsCopy = new Http3Settings(settingsFrame.Settings().nonStandardSettingsValidator);
        DefaultHttp3SettingsFrame copy = new DefaultHttp3SettingsFrame(settingsCopy);
        if (settingsFrame is DefaultHttp3SettingsFrame)
        {
            copy.settings.PutAll(((DefaultHttp3SettingsFrame) settingsFrame).settings);
        }
        else
        {
            foreach (KeyValuePair<long, long> entry in settingsFrame)
            {
                ((IHttp3SettingsFrame) copy).Put(entry.Key, entry.Value);
            }
        }
        return copy;
    }
}
