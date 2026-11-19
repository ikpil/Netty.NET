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

using System.Net;
using Netty.NET.Buffer;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Dns;

// CLR: the package-private abstract class is internal; <A extends SocketAddress> is A : EndPoint, the CLR base of
// socket addresses used by IChannel.LocalAddress/RemoteAddress. The package-private final decode(...) is internal and
// non-virtual. checkNotNull throws ArgumentNullException (docs/codec-base-porting.md). readUnsignedShort() returns the
// unsigned ushort of the Buffer port, widened to int. 'throws Exception' has no C# counterpart.
internal abstract class DnsResponseDecoder<A> where A : EndPoint
{

    private readonly IDnsRecordDecoder recordDecoder;

    /**
     * Creates a new decoder with the specified {@code recordDecoder}.
     */
    internal DnsResponseDecoder(IDnsRecordDecoder recordDecoder)
    {
        this.recordDecoder = CheckNotNull(recordDecoder, "recordDecoder");
    }

    internal IDnsResponse Decode(A sender, A recipient, ByteBuf buffer)
    {
        int id = buffer.ReadUnsignedShort();

        int flags = buffer.ReadUnsignedShort();
        if (flags >> 15 == 0)
        {
            throw new CorruptedFrameException("not a response");
        }

        IDnsResponse response = NewResponse(
                sender,
                recipient,
                id,
                DnsOpCode.ValueOf((byte) (flags >> 11 & 0xf)), DnsResponseCode.ValueOf((byte) (flags & 0xf)));

        response.SetRecursionDesired((flags >> 8 & 1) == 1);
        response.SetAuthoritativeAnswer((flags >> 10 & 1) == 1);
        response.SetTruncated((flags >> 9 & 1) == 1);
        response.SetRecursionAvailable((flags >> 7 & 1) == 1);
        response.SetZ(flags >> 4 & 0x7);

        bool success = false;
        try
        {
            int questionCount = DnsCodecUtil.CheckRecordCount(DnsSection.QUESTION, buffer.ReadUnsignedShort());
            int answerCount = DnsCodecUtil.CheckRecordCount(DnsSection.ANSWER, buffer.ReadUnsignedShort());
            int authorityRecordCount =
                    DnsCodecUtil.CheckRecordCount(DnsSection.AUTHORITY, buffer.ReadUnsignedShort());
            int additionalRecordCount =
                    DnsCodecUtil.CheckRecordCount(DnsSection.ADDITIONAL, buffer.ReadUnsignedShort());

            DecodeQuestions(response, buffer, questionCount);
            if (!DecodeRecords(response, DnsSection.ANSWER, buffer, answerCount))
            {
                success = true;
                return response;
            }
            if (!DecodeRecords(response, DnsSection.AUTHORITY, buffer, authorityRecordCount))
            {
                success = true;
                return response;
            }

            DecodeRecords(response, DnsSection.ADDITIONAL, buffer, additionalRecordCount);
            success = true;
            return response;
        }
        finally
        {
            if (!success)
            {
                response.Release();
            }
        }
    }

    protected abstract IDnsResponse NewResponse(A sender, A recipient, int id,
                                               DnsOpCode opCode, DnsResponseCode responseCode);

    private void DecodeQuestions(IDnsResponse response, ByteBuf buf, int questionCount)
    {
        for (int i = questionCount; i > 0; i --)
        {
            response.AddRecord(DnsSection.QUESTION, recordDecoder.DecodeQuestion(buf));
        }
    }

    private bool DecodeRecords(
            IDnsResponse response, DnsSection section, ByteBuf buf, int count)
    {
        for (int i = count; i > 0; i --)
        {
            IDnsRecord r = recordDecoder.DecodeRecord<IDnsRecord>(buf);
            if (r == null)
            {
                // Truncated response
                return false;
            }

            response.AddRecord(section, r);
        }
        return true;
    }
}
