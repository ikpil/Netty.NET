/*
 * Copyright 2012 The Netty Project
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

using Netty.NET.Handler.Codec.Http;

namespace Netty.NET.Handler.Codec.Rtsp;

/**
 * The getStatus code and its description of a RTSP response.
 */
// CLR: the final class with a private constructor is a static class of static readonly HttpResponseStatus
// constants.
public static class RtspResponseStatuses
{

    /**
     * 100 Continue
     */
    public static readonly HttpResponseStatus CONTINUE = HttpResponseStatus.CONTINUE;

    /**
     * 200 OK
     */
    public static readonly HttpResponseStatus OK = HttpResponseStatus.OK;

    /**
     * 201 Created
     */
    public static readonly HttpResponseStatus CREATED = HttpResponseStatus.CREATED;

    /**
     * 250 Low on Storage Space
     */
    public static readonly HttpResponseStatus LOW_STORAGE_SPACE = new HttpResponseStatus(
            250, "Low on Storage Space");

    /**
     * 300 Multiple Choices
     */
    public static readonly HttpResponseStatus MULTIPLE_CHOICES = HttpResponseStatus.MULTIPLE_CHOICES;

    /**
     * 301 Moved Permanently
     */
    public static readonly HttpResponseStatus MOVED_PERMANENTLY = HttpResponseStatus.MOVED_PERMANENTLY;

    /**
     * 302 Moved Temporarily
     */
    public static readonly HttpResponseStatus MOVED_TEMPORARILY = new HttpResponseStatus(
            302, "Moved Temporarily");
    /**
     * 304 Not Modified
     */
    public static readonly HttpResponseStatus NOT_MODIFIED = HttpResponseStatus.NOT_MODIFIED;

    /**
     * 305 Use Proxy
     */
    public static readonly HttpResponseStatus USE_PROXY = HttpResponseStatus.USE_PROXY;

    /**
     * 400 Bad Request
     */
    public static readonly HttpResponseStatus BAD_REQUEST = HttpResponseStatus.BAD_REQUEST;

    /**
     * 401 Unauthorized
     */
    public static readonly HttpResponseStatus UNAUTHORIZED = HttpResponseStatus.UNAUTHORIZED;

    /**
     * 402 Payment Required
     */
    public static readonly HttpResponseStatus PAYMENT_REQUIRED = HttpResponseStatus.PAYMENT_REQUIRED;

    /**
     * 403 Forbidden
     */
    public static readonly HttpResponseStatus FORBIDDEN = HttpResponseStatus.FORBIDDEN;

    /**
     * 404 Not Found
     */
    public static readonly HttpResponseStatus NOT_FOUND = HttpResponseStatus.NOT_FOUND;

    /**
     * 405 Method Not Allowed
     */
    public static readonly HttpResponseStatus METHOD_NOT_ALLOWED = HttpResponseStatus.METHOD_NOT_ALLOWED;

    /**
     * 406 Not Acceptable
     */
    public static readonly HttpResponseStatus NOT_ACCEPTABLE = HttpResponseStatus.NOT_ACCEPTABLE;

    /**
     * 407 Proxy Authentication Required
     */
    public static readonly HttpResponseStatus PROXY_AUTHENTICATION_REQUIRED =
            HttpResponseStatus.PROXY_AUTHENTICATION_REQUIRED;

    /**
     * 408 Request Timeout
     */
    public static readonly HttpResponseStatus REQUEST_TIMEOUT = HttpResponseStatus.REQUEST_TIMEOUT;

    /**
     * 410 Gone
     */
    public static readonly HttpResponseStatus GONE = HttpResponseStatus.GONE;

    /**
     * 411 Length Required
     */
    public static readonly HttpResponseStatus LENGTH_REQUIRED = HttpResponseStatus.LENGTH_REQUIRED;

    /**
     * 412 Precondition Failed
     */
    public static readonly HttpResponseStatus PRECONDITION_FAILED = HttpResponseStatus.PRECONDITION_FAILED;

    /**
     * 413 Request Entity Too Large
     */
    public static readonly HttpResponseStatus REQUEST_ENTITY_TOO_LARGE = HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE;

    /**
     * 414 Request-URI Too Long
     */
    public static readonly HttpResponseStatus REQUEST_URI_TOO_LONG = HttpResponseStatus.REQUEST_URI_TOO_LONG;

    /**
     * 415 Unsupported Media Type
     */
    public static readonly HttpResponseStatus UNSUPPORTED_MEDIA_TYPE = HttpResponseStatus.UNSUPPORTED_MEDIA_TYPE;

    /**
     * 451 Parameter Not Understood
     */
    public static readonly HttpResponseStatus PARAMETER_NOT_UNDERSTOOD = new HttpResponseStatus(
            451, "Parameter Not Understood");

    /**
     * 452 Conference Not Found
     */
    public static readonly HttpResponseStatus CONFERENCE_NOT_FOUND = new HttpResponseStatus(
            452, "Conference Not Found");

    /**
     * 453 Not Enough Bandwidth
     */
    public static readonly HttpResponseStatus NOT_ENOUGH_BANDWIDTH = new HttpResponseStatus(
            453, "Not Enough Bandwidth");

    /**
     * 454 Session Not Found
     */
    public static readonly HttpResponseStatus SESSION_NOT_FOUND = new HttpResponseStatus(
            454, "Session Not Found");

    /**
     * 455 Method Not Valid in This State
     */
    public static readonly HttpResponseStatus METHOD_NOT_VALID = new HttpResponseStatus(
            455, "Method Not Valid in This State");

    /**
     * 456 Header Field Not Valid for Resource
     */
    public static readonly HttpResponseStatus HEADER_FIELD_NOT_VALID = new HttpResponseStatus(
            456, "Header Field Not Valid for Resource");

    /**
     * 457 Invalid Range
     */
    public static readonly HttpResponseStatus INVALID_RANGE = new HttpResponseStatus(
            457, "Invalid Range");

    /**
     * 458 Parameter Is Read-Only
     */
    public static readonly HttpResponseStatus PARAMETER_IS_READONLY = new HttpResponseStatus(
            458, "Parameter Is Read-Only");

    /**
     * 459 Aggregate operation not allowed
     */
    public static readonly HttpResponseStatus AGGREGATE_OPERATION_NOT_ALLOWED = new HttpResponseStatus(
            459, "Aggregate operation not allowed");

    /**
     * 460 Only Aggregate operation allowed
     */
    public static readonly HttpResponseStatus ONLY_AGGREGATE_OPERATION_ALLOWED = new HttpResponseStatus(
            460, "Only Aggregate operation allowed");

    /**
     * 461 Unsupported transport
     */
    public static readonly HttpResponseStatus UNSUPPORTED_TRANSPORT = new HttpResponseStatus(
            461, "Unsupported transport");

    /**
     * 462 Destination unreachable
     */
    public static readonly HttpResponseStatus DESTINATION_UNREACHABLE = new HttpResponseStatus(
            462, "Destination unreachable");

    /**
     * 463 Key management failure
     */
    public static readonly HttpResponseStatus KEY_MANAGEMENT_FAILURE = new HttpResponseStatus(
            463, "Key management failure");

    /**
     * 500 Internal Server Error
     */
    public static readonly HttpResponseStatus INTERNAL_SERVER_ERROR = HttpResponseStatus.INTERNAL_SERVER_ERROR;

    /**
     * 501 Not Implemented
     */
    public static readonly HttpResponseStatus NOT_IMPLEMENTED = HttpResponseStatus.NOT_IMPLEMENTED;

    /**
     * 502 Bad Gateway
     */
    public static readonly HttpResponseStatus BAD_GATEWAY = HttpResponseStatus.BAD_GATEWAY;

    /**
     * 503 Service Unavailable
     */
    public static readonly HttpResponseStatus SERVICE_UNAVAILABLE = HttpResponseStatus.SERVICE_UNAVAILABLE;

    /**
     * 504 Gateway Timeout
     */
    public static readonly HttpResponseStatus GATEWAY_TIMEOUT = HttpResponseStatus.GATEWAY_TIMEOUT;

    /**
     * 505 RTSP Version not supported
     */
    public static readonly HttpResponseStatus RTSP_VERSION_NOT_SUPPORTED = new HttpResponseStatus(
            505, "RTSP Version not supported");

    /**
     * 551 Option not supported
     */
    public static readonly HttpResponseStatus OPTION_NOT_SUPPORTED = new HttpResponseStatus(
            551, "Option not supported");

    /**
     * Returns the {@link HttpResponseStatus} represented by the specified code.
     * If the specified code is a standard RTSP getStatus code, a cached instance
     * will be returned.  Otherwise, a new instance will be returned.
     */
    public static HttpResponseStatus ValueOf(int code)
    {
        switch (code)
        {
        case 250:
                return LOW_STORAGE_SPACE;
        case 302:
                return MOVED_TEMPORARILY;
        case 451:
                return PARAMETER_NOT_UNDERSTOOD;
        case 452:
                return CONFERENCE_NOT_FOUND;
        case 453:
                return NOT_ENOUGH_BANDWIDTH;
        case 454:
                return SESSION_NOT_FOUND;
        case 455:
                return METHOD_NOT_VALID;
        case 456:
                return HEADER_FIELD_NOT_VALID;
        case 457:
                return INVALID_RANGE;
        case 458:
                return PARAMETER_IS_READONLY;
        case 459:
                return AGGREGATE_OPERATION_NOT_ALLOWED;
        case 460:
                return ONLY_AGGREGATE_OPERATION_ALLOWED;
        case 461:
                return UNSUPPORTED_TRANSPORT;
        case 462:
                return DESTINATION_UNREACHABLE;
        case 463:
                return KEY_MANAGEMENT_FAILURE;
        case 505:
                return RTSP_VERSION_NOT_SUPPORTED;
        case 551:
                return OPTION_NOT_SUPPORTED;
        default:
                return HttpResponseStatus.ValueOf(code);
        }
    }
}
