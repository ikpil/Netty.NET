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

using System.Collections.Generic;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Rtsp;

/**
 * The request getMethod of RTSP.
 */
// CLR: the final class with a private constructor is a static class; the static initializer block is the static
// constructor, which runs after the field initializers. HashMap<String, HttpMethod> is Dictionary<string, HttpMethod>
// and get() is TryGetValue (null when absent). toUpperCase(Locale.US) is ToUpperInvariant, whose invariant casing never
// maps 'i' to U+0130 (https://learn.microsoft.com/en-us/dotnet/api/system.string.toupperinvariant?view=net-10.0, the
// mapping core HttpVersion uses). HttpMethod.toString() is ToString(), which returns the name as in Java.
public static class RtspMethods
{

    /**
     * The OPTIONS getMethod represents a request for information about the communication options
     * available on the request/response chain identified by the Request-URI. This getMethod allows
     * the client to determine the options and/or requirements associated with a resource, or the
     * capabilities of a server, without implying a resource action or initiating a resource
     * retrieval.
     */
    public static readonly HttpMethod OPTIONS = HttpMethod.OPTIONS;

    /**
     * The DESCRIBE getMethod retrieves the description of a presentation or
     * media object identified by the request URL from a server.
     */
    public static readonly HttpMethod DESCRIBE = HttpMethod.ValueOf("DESCRIBE");

    /**
     * The ANNOUNCE posts the description of a presentation or media object
     * identified by the request URL to a server, or updates the client-side
     * session description in real-time.
     */
    public static readonly HttpMethod ANNOUNCE = HttpMethod.ValueOf("ANNOUNCE");

    /**
     * The SETUP request for a URI specifies the transport mechanism to be
     * used for the streamed media.
     */
    public static readonly HttpMethod SETUP = HttpMethod.ValueOf("SETUP");

    /**
     * The PLAY getMethod tells the server to start sending data via the
     * mechanism specified in SETUP.
     */
    public static readonly HttpMethod PLAY = HttpMethod.ValueOf("PLAY");

    /**
     * The PAUSE request causes the stream delivery to be interrupted
     * (halted) temporarily.
     */
    public static readonly HttpMethod PAUSE = HttpMethod.ValueOf("PAUSE");

    /**
     * The TEARDOWN request stops the stream delivery for the given URI,
     * freeing the resources associated with it.
     */
    public static readonly HttpMethod TEARDOWN = HttpMethod.ValueOf("TEARDOWN");

    /**
     * The GET_PARAMETER request retrieves the value of a parameter of a
     * presentation or stream specified in the URI.
     */
    public static readonly HttpMethod GET_PARAMETER = HttpMethod.ValueOf("GET_PARAMETER");

    /**
     * The SET_PARAMETER requests to set the value of a parameter for a
     * presentation or stream specified by the URI.
     */
    public static readonly HttpMethod SET_PARAMETER = HttpMethod.ValueOf("SET_PARAMETER");

    /**
     * The REDIRECT request informs the client that it must connect to another
     * server location.
     */
    public static readonly HttpMethod REDIRECT = HttpMethod.ValueOf("REDIRECT");

    /**
     * The RECORD getMethod initiates recording a range of media data according to
     * the presentation description.
     */
    public static readonly HttpMethod RECORD = HttpMethod.ValueOf("RECORD");

    private static readonly Dictionary<string, HttpMethod> methodMap = new Dictionary<string, HttpMethod>();

    static RtspMethods()
    {
        methodMap[DESCRIBE.ToString()] = DESCRIBE;
        methodMap[ANNOUNCE.ToString()] = ANNOUNCE;
        methodMap[GET_PARAMETER.ToString()] = GET_PARAMETER;
        methodMap[OPTIONS.ToString()] = OPTIONS;
        methodMap[PAUSE.ToString()] = PAUSE;
        methodMap[PLAY.ToString()] = PLAY;
        methodMap[RECORD.ToString()] = RECORD;
        methodMap[REDIRECT.ToString()] = REDIRECT;
        methodMap[SETUP.ToString()] = SETUP;
        methodMap[SET_PARAMETER.ToString()] = SET_PARAMETER;
        methodMap[TEARDOWN.ToString()] = TEARDOWN;
    }

    /**
     * Returns the {@link HttpMethod} represented by the specified name.
     * If the specified name is a standard RTSP getMethod name, a cached instance
     * will be returned.  Otherwise, a new instance will be returned.
     */
    public static HttpMethod ValueOf(string name)
    {
        // RFC 2326 RTSP method names are ASCII tokens. toUpperCase() without an explicit Locale
        // uses the JVM default, which in Turkish (tr_TR) maps 'i' to 'İ' (U+0130) and breaks the
        // lookup of methods such as "describe" or "redirect" against the cached uppercase keys.
        name = CheckNotNull(name, "name").ToUpperInvariant();
        if (methodMap.TryGetValue(name, out HttpMethod result))
        {
            return result;
        }
        return HttpMethod.ValueOf(name);
    }
}
