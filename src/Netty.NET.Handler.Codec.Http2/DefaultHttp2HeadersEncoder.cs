/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Buffer;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

// CLR: SensitivityDetector is IHttp2HeadersEncoder.ISensitivityDetector, and the interface constant
// NEVER_SENSITIVE is reached through the interface name (an implementing class does not expose interface statics
// by its simple name). The public members of the non-final class are virtual. catch (Http2Exception e) { throw e;
// } is a rethrow (throw;), which keeps the stack trace as Java's throw e does; catch (Throwable) is catch
// (Exception) and getMessage() is Message.
public class DefaultHttp2HeadersEncoder :
    IHttp2HeadersEncoder, IHttp2HeadersEncoder.IConfiguration, IDisposable
{

    private readonly HpackEncoder hpackEncoder;
    private readonly IHttp2HeadersEncoder.ISensitivityDetector sensitivityDetector;

    public DefaultHttp2HeadersEncoder() : this(IHttp2HeadersEncoder.NEVER_SENSITIVE)
    {
    }

    public DefaultHttp2HeadersEncoder(IHttp2HeadersEncoder.ISensitivityDetector sensitivityDetector)
        : this(sensitivityDetector, new HpackEncoder())
    {
    }

    public DefaultHttp2HeadersEncoder(IHttp2HeadersEncoder.ISensitivityDetector sensitivityDetector,
                                      bool ignoreMaxHeaderListSize)
        : this(sensitivityDetector, new HpackEncoder(ignoreMaxHeaderListSize))
    {
    }

    public DefaultHttp2HeadersEncoder(IHttp2HeadersEncoder.ISensitivityDetector sensitivityDetector,
                                      bool ignoreMaxHeaderListSize, int dynamicTableArraySizeHint)
        : this(sensitivityDetector, ignoreMaxHeaderListSize, dynamicTableArraySizeHint,
               HpackEncoder.HUFF_CODE_THRESHOLD)
    {
    }

    public DefaultHttp2HeadersEncoder(IHttp2HeadersEncoder.ISensitivityDetector sensitivityDetector,
                                      bool ignoreMaxHeaderListSize, int dynamicTableArraySizeHint,
                                      int huffCodeThreshold)
        : this(sensitivityDetector,
               new HpackEncoder(ignoreMaxHeaderListSize, dynamicTableArraySizeHint, huffCodeThreshold))
    {
    }

    /**
     * Exposed Used for testing only! Default values used in the initial settings frame are overridden intentionally
     * for testing but violate the RFC if used outside the scope of testing.
     */
    internal DefaultHttp2HeadersEncoder(IHttp2HeadersEncoder.ISensitivityDetector sensitivityDetector,
                                        HpackEncoder hpackEncoder)
    {
        this.sensitivityDetector = CheckNotNull(sensitivityDetector, "sensitiveDetector");
        this.hpackEncoder = CheckNotNull(hpackEncoder, "hpackEncoder");
    }

    public virtual void EncodeHeaders(int streamId, IHttp2Headers headers, ByteBuf buffer)
    {
        try
        {
            hpackEncoder.EncodeHeaders(streamId, buffer, headers, sensitivityDetector);
        }
        catch (Http2Exception)
        {
            throw;
        }
        catch (Exception t)
        {
            throw ConnectionError(COMPRESSION_ERROR, t, "Failed encoding headers block: %s", t.Message);
        }
    }

    public virtual void MaxHeaderTableSize(long max)
    {
        hpackEncoder.SetMaxHeaderTableSize(max);
    }

    public virtual long MaxHeaderTableSize()
    {
        return hpackEncoder.GetMaxHeaderTableSize();
    }

    public virtual void MaxHeaderListSize(long max)
    {
        hpackEncoder.SetMaxHeaderListSize(max);
    }

    public virtual long MaxHeaderListSize()
    {
        return hpackEncoder.GetMaxHeaderListSize();
    }

    public virtual IHttp2HeadersEncoder.IConfiguration Configuration()
    {
        return this;
    }

    /**
     * Close the encoder and release all its associated data.
     */
    public virtual void Close()
    {
    }

    void IDisposable.Dispose()
    {
        Close();
    }
}
