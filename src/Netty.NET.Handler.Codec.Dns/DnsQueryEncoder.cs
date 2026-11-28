/*
 * Copyright 2019 The Netty Project
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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Dns;

// CLR: the package-private final class and its package-private members are internal; checkNotNull throws
// ArgumentNullException (docs/codec-base-porting.md). 'throws Exception' has no C# counterpart.
internal sealed class DnsQueryEncoder
{

    private readonly IDnsRecordEncoder recordEncoder;

    /**
     * Creates a new encoder with the specified {@code recordEncoder}.
     */
    internal DnsQueryEncoder(IDnsRecordEncoder recordEncoder)
    {
        this.recordEncoder = CheckNotNull(recordEncoder, "recordEncoder");
    }

    /**
     * Encodes the given {@link DnsQuery} into a {@link ByteBuf}.
     */
    internal void Encode(IDnsQuery query, ByteBuf @out)
    {
        EncodeHeader(query, @out);
        EncodeQuestions(query, @out);
        EncodeRecords(query, DnsSection.ADDITIONAL, @out);
    }

    /**
     * Encodes the header that is always 12 bytes long.
     *
     * @param query the query header being encoded
     * @param buf   the buffer the encoded data should be written to
     */
    private static void EncodeHeader(IDnsQuery query, ByteBuf buf)
    {
        buf.WriteShort(query.Id());
        int flags = 0;
        flags |= (query.OpCode().ByteValue() & 0xF) << 11;

        if (query.IsRecursionDesired())
        {
            flags |= 1 << 8;
        }

        buf.WriteShort(flags);
        buf.WriteShort(query.Count(DnsSection.QUESTION));
        buf.WriteShort(0); // answerCount
        buf.WriteShort(0); // authorityResourceCount
        buf.WriteShort(query.Count(DnsSection.ADDITIONAL));
    }

    private void EncodeQuestions(IDnsQuery query, ByteBuf buf)
    {
        int count = query.Count(DnsSection.QUESTION);

        for (int i = 0; i < count; i++)
        {
            recordEncoder.EncodeQuestion(query.RecordAt<IDnsQuestion>(DnsSection.QUESTION, i), buf);
        }
    }

    private void EncodeRecords(IDnsQuery query, DnsSection section, ByteBuf buf)
    {
        int count = query.Count(section);

        for (int i = 0; i < count; i++)
        {
            recordEncoder.EncodeRecord(query.RecordAt<IDnsRecord>(section, i), buf);
        }
    }
}
