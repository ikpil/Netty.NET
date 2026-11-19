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

using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Smtp;

/**
 * Default implementation of {@link SmtpContent} that does no validation of the raw data passed in.
 */
// CLR: @UnstableApi is the Common [UnstableApi] attribute. The Java overrides narrow the DefaultByteBufHolder return
// types with C# 9 covariant overrides: "an overriding method can declare a more derived return type the method it
// overrides" (https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance).
[UnstableApi]
public class DefaultSmtpContent : DefaultByteBufHolder, ISmtpContent
{

    /**
     * Creates a new instance using the given data.
     */
    public DefaultSmtpContent(ByteBuf data) : base(data)
    {
    }

    public override ISmtpContent Copy()
    {
        return (ISmtpContent) base.Copy();
    }

    public override ISmtpContent Duplicate()
    {
        return (ISmtpContent) base.Duplicate();
    }

    public override ISmtpContent RetainedDuplicate()
    {
        return (ISmtpContent) base.RetainedDuplicate();
    }

    public override ISmtpContent Replace(ByteBuf content)
    {
        return new DefaultSmtpContent(content);
    }

    public override ISmtpContent Retain()
    {
        base.Retain();
        return this;
    }

    public override ISmtpContent Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override ISmtpContent Touch()
    {
        base.Touch();
        return this;
    }

    public override ISmtpContent Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }
}
