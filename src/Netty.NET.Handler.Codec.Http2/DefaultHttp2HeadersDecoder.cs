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
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;

namespace Netty.NET.Handler.Codec.Http2;

// CLR: Configuration is IHttp2HeadersDecoder.IConfiguration. The package-private constructor taking the
// package-private HpackDecoder is internal. The public members of the non-final class are virtual, as is the
// protected non-final validateHeaderValues()/newHeaders(); the protected final members are non-virtual
// (docs/codec-http-porting.md, Cookie and CORS: "The members of the non-final DefaultCookie are virtual"). The
// @Deprecated annotation on the initialHuffmanDecodeCapacity parameter has no C# counterpart for a parameter (the
// constructor itself is not deprecated), and its Javadoc already says "Does nothing, do not use".
// catch (Http2Exception e) { throw e; } is a rethrow (throw;), catch (Throwable) is catch (Exception) and
// getMessage() is Message. The float moving average uses the same single-precision arithmetic.
public class DefaultHttp2HeadersDecoder : IHttp2HeadersDecoder, IHttp2HeadersDecoder.IConfiguration
{
    private const float HEADERS_COUNT_WEIGHT_NEW = 1 / 5f;
    private const float HEADERS_COUNT_WEIGHT_HISTORICAL = 1 - HEADERS_COUNT_WEIGHT_NEW;

    private readonly HpackDecoder hpackDecoder;
    private readonly bool validateHeaders;
    private readonly bool validateHeaderValues;
    private long maxHeaderListSizeGoAway;

    /**
     * Used to calculate an exponential moving average of header sizes to get an estimate of how large the data
     * structure for storing headers should be.
     */
    private float headerArraySizeAccumulator = 8;

    /**
     * Create a new instance with default settings.
     * Header validation is enabled by default.
     */
    public DefaultHttp2HeadersDecoder() : this(true)
    {
    }

    /**
     * Create a new instance.
     * @param validateHeaders {@code true} to validate headers are valid according to the RFC.
     * Validates both header name and header value.
     */
    public DefaultHttp2HeadersDecoder(bool validateHeaders)
        : this(validateHeaders, validateHeaders, DEFAULT_HEADER_LIST_SIZE)
    {
    }

    /**
     * Create a new instance.
     *
     * @param validateHeaders {@code true} to validate headers are valid according to the RFC.
     * This validates everything except header values.
     * @param validateHeaderValues {@code true} to validate that header <em>values</em> are valid according to the RFC.
     * Since this is potentially expensive, it can be enabled separately from {@code validateHeaders}.
     */
    public DefaultHttp2HeadersDecoder(bool validateHeaders, bool validateHeaderValues)
        : this(validateHeaders, validateHeaderValues, DEFAULT_HEADER_LIST_SIZE)
    {
    }

    /**
     * Create a new instance.
     * @param validateHeaders {@code true} to validate headers are valid according to the RFC.
     * Validates both header name and header value.
     * @param maxHeaderListSize This is the only setting that can be configured before notifying the peer.
     *  This is because <a href="https://tools.ietf.org/html/rfc7540#section-6.5.1">SETTINGS_MAX_HEADER_LIST_SIZE</a>
     *  allows a lower than advertised limit from being enforced, and the default limit is unlimited
     *  (which is dangerous).
     */
    public DefaultHttp2HeadersDecoder(bool validateHeaders, long maxHeaderListSize)
        : this(validateHeaders, validateHeaders, new HpackDecoder(maxHeaderListSize))
    {
    }

    /**
     * Create a new instance.
     * @param validateHeaders {@code true} to validate headers are valid according to the RFC.
     * This validates everything except header values.
     * @param validateHeaderValues {@code true} to validate that header <em>values</em> are valid according to the RFC.
     * Since this is potentially expensive, it can be enabled separately from {@code validateHeaders}.
     * @param maxHeaderListSize This is the only setting that can be configured before notifying the peer.
     *  This is because <a href="https://tools.ietf.org/html/rfc7540#section-6.5.1">SETTINGS_MAX_HEADER_LIST_SIZE</a>
     *  allows a lower than advertised limit from being enforced, and the default limit is unlimited
     *  (which is dangerous).
     */
    public DefaultHttp2HeadersDecoder(bool validateHeaders, bool validateHeaderValues, long maxHeaderListSize)
        : this(validateHeaders, validateHeaderValues, new HpackDecoder(maxHeaderListSize))
    {
    }

    /**
     * Create a new instance.
     * @param validateHeaders {@code true} to validate headers are valid according to the RFC.
     * Validates both header name and header value.
     * @param maxHeaderListSize This is the only setting that can be configured before notifying the peer.
     *  This is because <a href="https://tools.ietf.org/html/rfc7540#section-6.5.1">SETTINGS_MAX_HEADER_LIST_SIZE</a>
     *  allows a lower than advertised limit from being enforced, and the default limit is unlimited
     *  (which is dangerous).
     * @param initialHuffmanDecodeCapacity Does nothing, do not use.
     */
    public DefaultHttp2HeadersDecoder(bool validateHeaders, long maxHeaderListSize,
                                      int initialHuffmanDecodeCapacity)
        : this(validateHeaders, validateHeaders, new HpackDecoder(maxHeaderListSize))
    {
    }

    /**
     * Exposed for testing only! Default values used in the initial settings frame are overridden intentionally
     * for testing but violate the RFC if used outside the scope of testing.
     */
    internal DefaultHttp2HeadersDecoder(bool validateHeaders, bool validateHeaderValues, HpackDecoder hpackDecoder)
    {
        this.hpackDecoder = ObjectUtil.CheckNotNull(hpackDecoder, "hpackDecoder");
        this.validateHeaders = validateHeaders;
        this.validateHeaderValues = validateHeaderValues;
        maxHeaderListSizeGoAway =
                Http2CodecUtil.CalculateMaxHeaderListSizeGoAway(hpackDecoder.GetMaxHeaderListSize());
    }

    public virtual void MaxHeaderTableSize(long max)
    {
        hpackDecoder.SetMaxHeaderTableSize(max);
    }

    public virtual long MaxHeaderTableSize()
    {
        return hpackDecoder.GetMaxHeaderTableSize();
    }

    public virtual void MaxHeaderListSize(long max, long goAwayMax)
    {
        if (goAwayMax < max || goAwayMax < 0)
        {
            throw ConnectionError(INTERNAL_ERROR, "Header List Size GO_AWAY %d must be non-negative and >= %d",
                    goAwayMax, max);
        }

        hpackDecoder.SetMaxHeaderListSize(max);
        maxHeaderListSizeGoAway = goAwayMax;
    }

    public virtual long MaxHeaderListSize()
    {
        return hpackDecoder.GetMaxHeaderListSize();
    }

    public virtual long MaxHeaderListSizeGoAway()
    {
        return maxHeaderListSizeGoAway;
    }

    public virtual IHttp2HeadersDecoder.IConfiguration Configuration()
    {
        return this;
    }

    public virtual IHttp2Headers DecodeHeaders(int streamId, ByteBuf headerBlock)
    {
        try
        {
            IHttp2Headers headers = NewHeaders();
            hpackDecoder.Decode(streamId, headerBlock, headers, validateHeaders);
            headerArraySizeAccumulator = HEADERS_COUNT_WEIGHT_NEW * headers.Size() +
                                         HEADERS_COUNT_WEIGHT_HISTORICAL * headerArraySizeAccumulator;
            return headers;
        }
        catch (Http2Exception)
        {
            throw;
        }
        catch (Exception e)
        {
            // Default handler for any other types of errors that may have occurred. For example,
            // the Header builder throws IllegalArgumentException if the key or value was invalid
            // for any reason (e.g. the key was an invalid pseudo-header).
            throw ConnectionError(COMPRESSION_ERROR, e, "Error decoding headers: %s", e.Message);
        }
    }

    /**
     * A weighted moving average estimating how many headers are expected during the decode process.
     * @return an estimate of how many headers are expected during the decode process.
     */
    protected int NumberOfHeadersGuess()
    {
        return (int) headerArraySizeAccumulator;
    }

    /**
     * Determines if the headers should be validated as a result of the decode operation.
     * <p>
     * <strong>Note:</strong> This does not include validation of header <em>values</em>, since that is potentially
     * expensive to do. Value validation is instead {@linkplain #validateHeaderValues() enabled separately}.
     *
     * @return {@code true} if the headers should be validated as a result of the decode operation.
     */
    protected bool ValidateHeaders()
    {
        return validateHeaders;
    }

    /**
     * Determines if the header values should be validated as a result of the decode operation.
     * <p>
     * <strong>Note:</strong> This <em>only</em> validates the values of headers. All other header validations are
     * instead {@linkplain #validateHeaders() enabled separately}.
     *
     * @return {@code true} if the header values should be validated as a result of the decode operation.
     */
    protected virtual bool ValidateHeaderValues()
    { // Not 'final' due to backwards compatibility.
        return validateHeaderValues;
    }

    /**
     * Create a new {@link Http2Headers} object which will store the results of the decode operation.
     * @return a new {@link Http2Headers} object which will store the results of the decode operation.
     */
    protected virtual IHttp2Headers NewHeaders()
    {
        return new DefaultHttp2Headers(validateHeaders, validateHeaderValues, (int) headerArraySizeAccumulator);
    }
}
