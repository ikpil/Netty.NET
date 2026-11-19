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
 * Encodes a {@link DnsRecord} into binary representation.
 *
 * @see DatagramDnsQueryEncoder
 */
// CLR: interface with the I prefix. The interface field DEFAULT is a static readonly interface field (C# interfaces may
// declare static fields, https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface).
// 'throws Exception' has no C# counterpart.
public interface IDnsRecordEncoder
{

    static readonly IDnsRecordEncoder DEFAULT = new DefaultDnsRecordEncoder();

    /**
     * Encodes a {@link DnsQuestion}.
     *
     * @param out the output buffer where the encoded question will be written to
     */
    void EncodeQuestion(IDnsQuestion question, ByteBuf @out);

    /**
     * Encodes a {@link DnsRecord}.
     *
     * @param out the output buffer where the encoded record will be written to
     */
    void EncodeRecord(IDnsRecord record, ByteBuf @out);
}
