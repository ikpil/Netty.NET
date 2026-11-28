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
 * Content that is sent after the {@code DATA} request.
 * Be aware that a {@link SmtpContent} / {@link LastSmtpContent} sequence must always use CRLF as line delimiter
 * and the lines that start with a DOT must be escaped with an extra DOT as
 * specified by <a href="https://www.ietf.org/rfc/rfc2821.txt">RFC2821</a>.
 */
// CLR: Every Java member re-declares an IByteBufHolder member only to narrow its return type to SmtpContent; they
// are inherited unchanged, as for the Transport interfaces: C# 9 covariant returns cover class overrides only.
// DefaultSmtpContent keeps the narrowed return types.
[UnstableApi]
public interface ISmtpContent : IByteBufHolder
{
}
