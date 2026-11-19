/*
 * Copyright 2013 The Netty Project
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

using System.Diagnostics;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Memcache;
using static Netty.NET.Common.Internal.StringUtil;

namespace Netty.NET.Handler.Codec.Memcache.Binary;

/**
 * An object aggregator for the memcache binary protocol.
 *
 * It aggregates {@link BinaryMemcacheMessage}s and {@link MemcacheContent} into {@link FullBinaryMemcacheRequest}s
 * or {@link FullBinaryMemcacheResponse}s.
 */
[UnstableApi]
public class BinaryMemcacheObjectAggregator : AbstractMemcacheObjectAggregator<IBinaryMemcacheMessage>
{

    public BinaryMemcacheObjectAggregator(int maxContentLength) : base(maxContentLength)
    {
    }

    protected override bool IsStartMessage(IMemcacheObject msg)
    {
        return msg is IBinaryMemcacheMessage;
    }

    // CLR: java.lang.Error is UnreachableException, as in codec-base LengthFieldPrepender
    // (docs/codec-base-porting-manifest.json; https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.unreachableexception).
    protected override IFullMemcacheMessage BeginAggregation(IBinaryMemcacheMessage start, ByteBuf content)
    {
        if (start is IBinaryMemcacheRequest)
        {
            return ToFullRequest((IBinaryMemcacheRequest) start, content);
        }

        if (start is IBinaryMemcacheResponse)
        {
            return ToFullResponse((IBinaryMemcacheResponse) start, content);
        }

        // Should not reach here.
        throw new UnreachableException("Unexpected memcache message type: " + ClassName(start));
    }

    private static IFullBinaryMemcacheRequest ToFullRequest(IBinaryMemcacheRequest request, ByteBuf content)
    {
        ByteBuf key = request.Key() == null ? null : request.Key().Retain();
        ByteBuf extras = request.Extras() == null ? null : request.Extras().Retain();
        DefaultFullBinaryMemcacheRequest fullRequest =
                new DefaultFullBinaryMemcacheRequest(key, extras, content);

        fullRequest.SetMagic(request.Magic());
        fullRequest.SetOpcode(request.Opcode());
        fullRequest.SetKeyLength(request.KeyLength());
        fullRequest.SetExtrasLength(request.ExtrasLength());
        fullRequest.SetDataType(request.DataType());
        fullRequest.SetTotalBodyLength(request.TotalBodyLength());
        fullRequest.SetOpaque(request.Opaque());
        fullRequest.SetCas(request.Cas());
        fullRequest.SetReserved(request.Reserved());

        return fullRequest;
    }

    private static IFullBinaryMemcacheResponse ToFullResponse(IBinaryMemcacheResponse response, ByteBuf content)
    {
        ByteBuf key = response.Key() == null ? null : response.Key().Retain();
        ByteBuf extras = response.Extras() == null ? null : response.Extras().Retain();
        DefaultFullBinaryMemcacheResponse fullResponse =
                new DefaultFullBinaryMemcacheResponse(key, extras, content);

        fullResponse.SetMagic(response.Magic());
        fullResponse.SetOpcode(response.Opcode());
        fullResponse.SetKeyLength(response.KeyLength());
        fullResponse.SetExtrasLength(response.ExtrasLength());
        fullResponse.SetDataType(response.DataType());
        fullResponse.SetTotalBodyLength(response.TotalBodyLength());
        fullResponse.SetOpaque(response.Opaque());
        fullResponse.SetCas(response.Cas());
        fullResponse.SetStatus(response.Status());

        return fullResponse;
    }
}
