/*
 * Copyright 2015 The Netty Project
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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A decorator around another {@link Http2ConnectionEncoder} instance.
 */
// CLR: the non-final Java methods are virtual (the limit encoders and StreamBufferingEncoder override them); the
// field and parameter delegate are @delegate (a C# keyword). Http2SettingsReceivedConsumer.class in the message is
// typeof(IHttp2SettingsReceivedConsumer), whose ToString() is the type's full name, where Java's Class.toString()
// is "the string "class" or "interface", followed by a space, and then by the name of the class"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Class.html#toString()).
// IllegalStateException is InvalidOperationException.
public class DecoratingHttp2ConnectionEncoder : DecoratingHttp2FrameWriter, IHttp2ConnectionEncoder,
        IHttp2SettingsReceivedConsumer
{
    private readonly IHttp2ConnectionEncoder @delegate;

    public DecoratingHttp2ConnectionEncoder(IHttp2ConnectionEncoder @delegate) : base(@delegate)
    {
        this.@delegate = CheckNotNull(@delegate, "delegate");
    }

    public virtual void LifecycleManager(IHttp2LifecycleManager lifecycleManager)
    {
        @delegate.LifecycleManager(lifecycleManager);
    }

    public virtual IHttp2Connection Connection()
    {
        return @delegate.Connection();
    }

    public virtual IHttp2RemoteFlowController FlowController()
    {
        return @delegate.FlowController();
    }

    public virtual IHttp2FrameWriter FrameWriter()
    {
        return @delegate.FrameWriter();
    }

    public virtual Http2Settings PollSentSettings()
    {
        return @delegate.PollSentSettings();
    }

    public virtual void RemoteSettings(Http2Settings settings)
    {
        @delegate.RemoteSettings(settings);
    }

    public virtual void ConsumeReceivedSettings(Http2Settings settings)
    {
        if (@delegate is IHttp2SettingsReceivedConsumer)
        {
            ((IHttp2SettingsReceivedConsumer) @delegate).ConsumeReceivedSettings(settings);
        }
        else
        {
            throw new InvalidOperationException("delegate " + @delegate + " is not an instance of " +
                    typeof(IHttp2SettingsReceivedConsumer));
        }
    }
}
