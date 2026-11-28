/*
 * Copyright 2015 The Netty Project
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

namespace Netty.NET.Handler.Codec.Dns;

/**
 * A DNS query message.
 */
// CLR: Every Java member re-declares an IDnsMessage member only to narrow its return type to DnsQuery; they are
// inherited unchanged, as for the Transport interfaces: C# 9 covariant returns cover class overrides only.
// DefaultDnsQuery and DatagramDnsQuery keep the narrowed return types.
public interface IDnsQuery : IDnsMessage
{
}
