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
 * A DNS response message.
 */
// CLR: The Java members that re-declare IDnsMessage members only to narrow their return type to DnsResponse are
// inherited unchanged, as for the Transport interfaces: C# 9 covariant returns cover class overrides only.
// DefaultDnsResponse and DatagramDnsResponse keep the narrowed return types.
public interface IDnsResponse : IDnsMessage
{

    /**
     * Returns {@code true} if responding server is authoritative for the domain
     * name in the query message.
     */
    bool IsAuthoritativeAnswer();

    /**
     * Set to {@code true} if responding server is authoritative for the domain
     * name in the query message.
     *
     * @param authoritativeAnswer flag for authoritative answer
     */
    IDnsResponse SetAuthoritativeAnswer(bool authoritativeAnswer);

    /**
     * Returns {@code true} if response has been truncated, usually if it is
     * over 512 bytes.
     */
    bool IsTruncated();

    /**
     * Set to {@code true} if response has been truncated (usually happens for
     * responses over 512 bytes).
     *
     * @param truncated flag for truncation
     */
    IDnsResponse SetTruncated(bool truncated);

    /**
     * Returns {@code true} if DNS server can handle recursive queries.
     */
    bool IsRecursionAvailable();

    /**
     * Set to {@code true} if DNS server can handle recursive queries.
     *
     * @param recursionAvailable flag for recursion availability
     */
    IDnsResponse SetRecursionAvailable(bool recursionAvailable);

    /**
     * Returns the 4 bit return code.
     */
    DnsResponseCode Code();

    /**
     * Sets the response code for this message.
     *
     * @param code the response code
     */
    IDnsResponse SetCode(DnsResponseCode code);
}
