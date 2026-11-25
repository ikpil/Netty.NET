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

using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * The default {@link Http2SettingsFrame} implementation.
 */
// CLR: the non-final Java methods are virtual. settings.equals/hashCode are the Http2Settings Equals/GetHashCode
// overrides, which keep CharObjectHashMap's content equality (see Http2Settings).
public class DefaultHttp2SettingsFrame : IHttp2SettingsFrame
{

    private readonly Http2Settings settings;

    public DefaultHttp2SettingsFrame(Http2Settings settings)
    {
        this.settings = ObjectUtil.CheckNotNull(settings, "settings");
    }

    public virtual Http2Settings Settings()
    {
        return settings;
    }

    public virtual string Name()
    {
        return "SETTINGS";
    }

    public override bool Equals(object o)
    {
        if (!(o is IHttp2SettingsFrame))
        {
            return false;
        }
        IHttp2SettingsFrame other = (IHttp2SettingsFrame) o;
        return settings.Equals(other.Settings());
    }

    public override int GetHashCode()
    {
        return settings.GetHashCode();
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(settings=" + settings + ')';
    }
}
