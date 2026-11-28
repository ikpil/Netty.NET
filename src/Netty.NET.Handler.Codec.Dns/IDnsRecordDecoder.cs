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

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * Decodes a DNS record into its object representation.
 *
 * @see DatagramDnsResponseDecoder
 */
// CLR: The interface field DEFAULT is a static readonly interface field (C# interfaces may declare static fields,
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface). 'throws Exception' has
// no C# counterpart; <T extends DnsRecord> is T : IDnsRecord (see IDnsMessage).
public interface IDnsRecordDecoder
{

    static readonly IDnsRecordDecoder DEFAULT = new DefaultDnsRecordDecoder();

    /**
     * Decodes a DNS question into its object representation.
     *
     * @param in the input buffer which contains a DNS question at its reader index
     */
    IDnsQuestion DecodeQuestion(ByteBuf @in);

    /**
     * Decodes a DNS record into its object representation.
     *
     * @param in the input buffer which contains a DNS record at its reader index
     *
     * @return the decoded record, or {@code null} if there are not enough data in the input buffer
     */
    T DecodeRecord<T>(ByteBuf @in) where T : IDnsRecord;
}
